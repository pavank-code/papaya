using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;
using FlowVault.BackendHost.Persistence;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// Incremental file indexer with prioritized processing and debouncing
/// </summary>
public class IndexerService : IDisposable
{
    private readonly ILogger<IndexerService> _logger;
    private readonly DatabaseService _database;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _recentlyIndexed = new();
    private readonly ConcurrentQueue<string> _indexQueue = new();
    private readonly ConcurrentDictionary<string, IndexStatus> _indexStatuses = new();
    
    private CancellationTokenSource? _indexingCts;
    private Task? _indexingTask;
    private const int DebounceMs = 500;
    private const int MaxFileSizeBytes = 30_000;
    private bool _disposed;

    public IndexerService(ILogger<IndexerService> logger, DatabaseService database)
    {
        _logger = logger;
        _database = database;
    }

    #region Public API

    /// <summary>
    /// Reindex a specific project path
    /// </summary>
    public async Task ReindexProjectAsync(string path, Guid projectId, CancellationToken ct)
    {
        _logger.LogInformation("Starting reindex for project {ProjectId} at {Path}", projectId, path);
        
        var project = new ProjectConfig
        {
            Id = projectId.ToString(),
            Name = Path.GetFileName(path),
            Path = path,
            IsActive = true
        };

        await StartIndexingAsync(project, ct);
    }

    public async Task StartIndexingAsync(ProjectConfig project, CancellationToken ct)
    {
        if (_watchers.ContainsKey(project.Id))
        {
            _logger.LogWarning("Project {Id} is already being indexed", project.Id);
            return;
        }

        var status = new IndexStatus
        {
            ProjectId = project.Id,
            Status = IndexingStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        _indexStatuses[project.Id] = status;

        // Initial full scan
        await PerformFullScanAsync(project, status, ct);

        // Setup file watcher for incremental updates
        SetupFileWatcher(project);

        // Start background processing
        _indexingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _indexingTask = Task.Run(() => ProcessIndexQueueAsync(_indexingCts.Token), _indexingCts.Token);

        _logger.LogInformation("Started indexing project {Name} at {Path}", project.Name, project.Path);
    }

    public Task StopIndexingAsync(string projectId, CancellationToken ct)
    {
        if (_watchers.TryRemove(projectId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        if (_indexStatuses.TryGetValue(projectId, out var status))
        {
            status.Status = IndexingStatus.Idle;
            status.CompletedAt = DateTime.UtcNow;
        }

        _logger.LogInformation("Stopped indexing project {Id}", projectId);
        return Task.CompletedTask;
    }

    public Task<IndexStatus> GetIndexStatusAsync(string projectId, CancellationToken ct)
    {
        if (_indexStatuses.TryGetValue(projectId, out var status))
        {
            return Task.FromResult(status);
        }

        return Task.FromResult(new IndexStatus
        {
            ProjectId = projectId,
            Status = IndexingStatus.Idle
        });
    }

    public async Task<FileSummaryDto> GetFileSummaryAsync(string filePath, CancellationToken ct)
    {
        var existing = await _database.GetFileSummaryAsync(filePath);
        if (existing != null)
        {
            return existing;
        }

        // Index on demand
        var summary = await IndexFileAsync(filePath);
        return summary;
    }

    public async Task<FolderSummaryDto> GetFolderSummaryAsync(string folderPath, CancellationToken ct)
    {
        var existing = await _database.GetFolderSummaryAsync(folderPath);
        if (existing != null)
        {
            return existing;
        }

        // Generate on demand
        var summary = await GenerateFolderSummaryAsync(folderPath);
        return summary;
    }

    #endregion

    #region Indexing Logic

    private async Task PerformFullScanAsync(ProjectConfig project, IndexStatus status, CancellationToken ct)
    {
        var files = GetProjectFiles(project);
        status.TotalFiles = files.Count;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                status.CurrentFile = file;
                await IndexFileAsync(file, project.Id);
                status.IndexedFiles++;
            }
            catch (Exception ex)
            {
                status.FailedFiles++;
                status.Errors.Add($"{file}: {ex.Message}");
                _logger.LogWarning(ex, "Failed to index file {File}", file);
            }
        }

        status.Status = IndexingStatus.Completed;
        status.CompletedAt = DateTime.UtcNow;
        status.CurrentFile = null;
    }

    private List<string> GetProjectFiles(ProjectConfig project)
    {
        var files = new List<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(project.Path, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(project.Path, file);
                
                // Check ignore patterns
                if (project.IgnorePatterns.Any(p => relativePath.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Check extensions
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (project.IncludeExtensions.Count > 0 && !project.IncludeExtensions.Contains(ext))
                    continue;

                files.Add(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning project directory {Path}", project.Path);
        }

        return files;
    }

    private async Task<FileSummaryDto> IndexFileAsync(string filePath, string? projectId = null)
    {
        var fileInfo = new FileInfo(filePath);
        var content = await ReadFileHeadAsync(filePath, MaxFileSizeBytes);
        var parsed = ParseFileContent(content, filePath);

        var summary = new FileSummaryDto
        {
            Id = Guid.NewGuid().ToString(),
            FilePath = filePath,
            FileName = fileInfo.Name,
            ProjectId = projectId ?? string.Empty,
            Language = DetectLanguage(filePath),
            FileSizeBytes = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            IndexedAt = DateTime.UtcNow,
            Functions = parsed.Functions,
            Classes = parsed.Classes,
            Todos = parsed.Todos,
            Imports = parsed.Imports,
            LineCount = content.Split('\n').Length,
            TodoCount = parsed.Todos.Count,
            ComplexityEstimate = EstimateComplexity(parsed),
            HotspotScore = CalculateHotspotScore(parsed),
            SummaryText = BuildNaiveSummary(parsed, fileInfo.Name)
        };

        await _database.SaveFileSummaryAsync(summary);
        _recentlyIndexed[filePath] = DateTime.UtcNow;

        return summary;
    }

    private async Task<FolderSummaryDto> GenerateFolderSummaryAsync(string folderPath)
    {
        var folderName = Path.GetFileName(folderPath);
        var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
        
        var totalLines = 0;
        var totalTodos = 0;
        var complexitySum = 0.0;
        var hotspotSum = 0.0;
        var languageBreakdown = new Dictionary<string, int>();
        var riskyFiles = new List<TopRiskyFile>();

        foreach (var file in files.Take(100)) // Limit to 100 files for performance
        {
            var fileSummary = await _database.GetFileSummaryAsync(file);
            if (fileSummary == null) continue;

            totalLines += fileSummary.LineCount;
            totalTodos += fileSummary.TodoCount;
            complexitySum += fileSummary.ComplexityEstimate;
            hotspotSum += fileSummary.HotspotScore;

            if (!string.IsNullOrEmpty(fileSummary.Language))
            {
                languageBreakdown.TryGetValue(fileSummary.Language, out var count);
                languageBreakdown[fileSummary.Language] = count + 1;
            }

            if (fileSummary.HotspotScore > 0.5)
            {
                riskyFiles.Add(new TopRiskyFile
                {
                    FilePath = fileSummary.FilePath,
                    FileName = fileSummary.FileName,
                    HotspotScore = fileSummary.HotspotScore,
                    TodoCount = fileSummary.TodoCount,
                    RiskReason = fileSummary.TodoCount > 5 ? "High TODO count" : "High complexity"
                });
            }
        }

        var summary = new FolderSummaryDto
        {
            Id = Guid.NewGuid().ToString(),
            FolderPath = folderPath,
            FolderName = folderName,
            IndexedAt = DateTime.UtcNow,
            TotalFiles = files.Length,
            TotalLines = totalLines,
            TotalTodos = totalTodos,
            AverageComplexity = files.Length > 0 ? complexitySum / files.Length : 0,
            AggregateHotspotScore = hotspotSum,
            TopRiskyFiles = riskyFiles.OrderByDescending(f => f.HotspotScore).Take(5).ToList(),
            LanguageBreakdown = languageBreakdown,
            SummaryText = BuildFolderSummaryText(folderName, files.Length, totalTodos, riskyFiles)
        };

        await _database.SaveFolderSummaryAsync(summary);
        return summary;
    }

    #endregion

    #region Parsing

    private record ParseResult(
        List<FunctionSignature> Functions,
        List<ClassSignature> Classes,
        List<TodoComment> Todos,
        List<string> Imports
    );

    private ParseResult ParseFileContent(string content, string filePath)
    {
        var functions = new List<FunctionSignature>();
        var classes = new List<ClassSignature>();
        var todos = new List<TodoComment>();
        var imports = new List<string>();

        var lines = content.Split('\n');
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Parse TODOs
            var todoMatch = Regex.Match(line, @"(TODO|FIXME|HACK|BUG|XXX)[\s:]+(.+)", RegexOptions.IgnoreCase);
            if (todoMatch.Success)
            {
                todos.Add(new TodoComment
                {
                    Type = todoMatch.Groups[1].Value.ToUpper(),
                    Text = todoMatch.Groups[2].Value.Trim(),
                    LineNumber = lineNum
                });
            }

            // Parse imports based on language
            if (ext == ".cs")
            {
                if (line.TrimStart().StartsWith("using ") && !line.Contains("("))
                {
                    imports.Add(line.Trim().TrimEnd(';'));
                }

                // Parse C# class
                var classMatch = Regex.Match(line, @"(public|internal|private)?\s*(partial\s+)?(class|interface|struct|record)\s+(\w+)");
                if (classMatch.Success)
                {
                    classes.Add(new ClassSignature
                    {
                        Name = classMatch.Groups[4].Value,
                        LineNumber = lineNum
                    });
                }

                // Parse C# method
                var methodMatch = Regex.Match(line, @"(public|private|protected|internal)\s+(?:async\s+)?(?:static\s+)?(\w+(?:<[\w,\s]+>)?)\s+(\w+)\s*\(");
                if (methodMatch.Success && !line.Contains("class ") && !line.Contains("interface "))
                {
                    functions.Add(new FunctionSignature
                    {
                        Name = methodMatch.Groups[3].Value,
                        ReturnType = methodMatch.Groups[2].Value,
                        LineNumber = lineNum
                    });
                }
            }
            else if (ext == ".ts" || ext == ".js")
            {
                if (line.TrimStart().StartsWith("import "))
                {
                    imports.Add(line.Trim());
                }

                var funcMatch = Regex.Match(line, @"(export\s+)?(async\s+)?function\s+(\w+)|(\w+)\s*[=:]\s*(async\s+)?\(.*\)\s*=>|(\w+)\s*\(.*\)\s*{");
                if (funcMatch.Success)
                {
                    var name = funcMatch.Groups[3].Value;
                    if (string.IsNullOrEmpty(name)) name = funcMatch.Groups[4].Value;
                    if (string.IsNullOrEmpty(name)) name = funcMatch.Groups[6].Value;
                    
                    if (!string.IsNullOrEmpty(name))
                    {
                        functions.Add(new FunctionSignature
                        {
                            Name = name,
                            LineNumber = lineNum
                        });
                    }
                }
            }
            else if (ext == ".py")
            {
                if (line.TrimStart().StartsWith("import ") || line.TrimStart().StartsWith("from "))
                {
                    imports.Add(line.Trim());
                }

                var defMatch = Regex.Match(line, @"def\s+(\w+)\s*\(");
                if (defMatch.Success)
                {
                    functions.Add(new FunctionSignature
                    {
                        Name = defMatch.Groups[1].Value,
                        LineNumber = lineNum
                    });
                }

                var classMatch = Regex.Match(line, @"class\s+(\w+)");
                if (classMatch.Success)
                {
                    classes.Add(new ClassSignature
                    {
                        Name = classMatch.Groups[1].Value,
                        LineNumber = lineNum
                    });
                }
            }
        }

        return new ParseResult(functions, classes, todos, imports);
    }

    private double EstimateComplexity(ParseResult parsed)
    {
        // Simple complexity estimate based on number of functions and classes
        return Math.Min(1.0, (parsed.Functions.Count * 0.1 + parsed.Classes.Count * 0.2) / 5.0);
    }

    private double CalculateHotspotScore(ParseResult parsed)
    {
        // Hotspot = normalized TODOs + complexity
        var todoWeight = Math.Min(1.0, parsed.Todos.Count / 10.0);
        var complexityWeight = EstimateComplexity(parsed);
        return (todoWeight * 0.6 + complexityWeight * 0.4);
    }

    private string BuildNaiveSummary(ParseResult parsed, string fileName)
    {
        var parts = new List<string>();

        if (parsed.Classes.Count > 0)
        {
            var classNames = string.Join(", ", parsed.Classes.Take(3).Select(c => c.Name));
            parts.Add($"Defines {parsed.Classes.Count} class(es): {classNames}");
        }

        if (parsed.Functions.Count > 0)
        {
            parts.Add($"Contains {parsed.Functions.Count} function(s)");
        }

        if (parsed.Todos.Count > 0)
        {
            parts.Add($"Has {parsed.Todos.Count} TODO item(s)");
        }

        if (parts.Count == 0)
        {
            return $"File: {fileName}";
        }

        return string.Join(". ", parts) + ".";
    }

    private string BuildFolderSummaryText(string folderName, int fileCount, int todoCount, List<TopRiskyFile> riskyFiles)
    {
        var summary = $"Folder '{folderName}' contains {fileCount} files";
        
        if (todoCount > 0)
        {
            summary += $" with {todoCount} TODOs";
        }

        if (riskyFiles.Count > 0)
        {
            summary += $". Top risky file: {riskyFiles[0].FileName}";
        }

        return summary + ".";
    }

    private string DetectLanguage(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".ts" => "TypeScript",
            ".js" => "JavaScript",
            ".py" => "Python",
            ".json" => "JSON",
            ".xaml" => "XAML",
            ".xml" => "XML",
            ".html" => "HTML",
            ".css" => "CSS",
            ".md" => "Markdown",
            _ => "Unknown"
        };
    }

    private async Task<string> ReadFileHeadAsync(string filePath, int maxBytes)
    {
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[maxBytes];
            var bytesRead = await stream.ReadAsync(buffer, 0, maxBytes);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
        catch
        {
            return string.Empty;
        }
    }

    #endregion

    #region File Watcher

    private void SetupFileWatcher(ProjectConfig project)
    {
        var watcher = new FileSystemWatcher(project.Path)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        watcher.Changed += (s, e) => EnqueueFile(e.FullPath, project);
        watcher.Created += (s, e) => EnqueueFile(e.FullPath, project);
        watcher.Renamed += (s, e) => EnqueueFile(e.FullPath, project);

        _watchers[project.Id] = watcher;
    }

    private void EnqueueFile(string filePath, ProjectConfig project)
    {
        // Check if recently indexed (debounce)
        if (_recentlyIndexed.TryGetValue(filePath, out var lastIndexed))
        {
            if ((DateTime.UtcNow - lastIndexed).TotalMilliseconds < DebounceMs)
            {
                return;
            }
        }

        // Check filters
        var relativePath = Path.GetRelativePath(project.Path, filePath);
        if (project.IgnorePatterns.Any(p => relativePath.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (project.IncludeExtensions.Count > 0 && !project.IncludeExtensions.Contains(ext))
            return;

        _indexQueue.Enqueue(filePath);
    }

    private async Task ProcessIndexQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                while (_indexQueue.TryDequeue(out var filePath))
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        await IndexFileAsync(filePath);
                        
                        // Update folder summary
                        var folder = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(folder))
                        {
                            await GenerateFolderSummaryAsync(folder);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to re-index {File}", filePath);
                    }
                }

                await Task.Delay(100, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _indexingCts?.Cancel();
            _indexingTask?.Wait(1000);
            
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
            
            _disposed = true;
        }
    }
}
