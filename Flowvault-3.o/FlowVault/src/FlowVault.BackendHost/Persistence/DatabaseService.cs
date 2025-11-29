using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;
using System.Text.Json;

namespace FlowVault.BackendHost.Persistence;

/// <summary>
/// SQLite database service with DPAPI encryption for sensitive data
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FlowVault_Entropy_2024_v3");
    private bool _disposed;

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        _logger = logger;
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataFolder = Path.Combine(appData, "FlowVault");
        Directory.CreateDirectory(dataFolder);
        
        _databasePath = Path.Combine(dataFolder, "flowvault.db");
        _connectionString = $"Data Source={_databasePath}";
        
        InitializeDatabase();
        _logger.LogInformation("Database initialized at {Path}", _databasePath);
    }

    #region Initialization

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            -- Projects
            CREATE TABLE IF NOT EXISTS Projects (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL,
                Branch TEXT,
                LastIndexedAt TEXT,
                IsActive INTEGER DEFAULT 1,
                IgnorePatterns TEXT,
                IncludeExtensions TEXT,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            -- Tasks
            CREATE TABLE IF NOT EXISTS Tasks (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                Description TEXT,
                EstimatedMinutes INTEGER DEFAULT 60,
                Difficulty INTEGER DEFAULT 3,
                Importance INTEGER DEFAULT 2,
                AiPriorityScore REAL DEFAULT 0,
                Confidence REAL DEFAULT 0.8,
                DueDate TEXT,
                Dependencies TEXT,
                Subtasks TEXT,
                ContextScope TEXT,
                Status INTEGER DEFAULT 0,
                TimeSpentSeconds INTEGER DEFAULT 0,
                PriorityRationale TEXT,
                ProjectId TEXT,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                CompletedAt TEXT,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id)
            );

            -- File Summaries
            CREATE TABLE IF NOT EXISTS FileSummaries (
                Id TEXT PRIMARY KEY,
                FilePath TEXT NOT NULL UNIQUE,
                FileName TEXT NOT NULL,
                ProjectId TEXT,
                SummaryText TEXT,
                Language TEXT,
                FileSizeBytes INTEGER,
                LastModified TEXT,
                IndexedAt TEXT,
                Functions TEXT,
                Classes TEXT,
                Todos TEXT,
                Imports TEXT,
                LineCount INTEGER,
                TodoCount INTEGER,
                ComplexityEstimate REAL,
                HotspotScore REAL,
                HasRecentFailingTests INTEGER DEFAULT 0,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id)
            );

            -- Folder Summaries
            CREATE TABLE IF NOT EXISTS FolderSummaries (
                Id TEXT PRIMARY KEY,
                FolderPath TEXT NOT NULL UNIQUE,
                FolderName TEXT NOT NULL,
                ProjectId TEXT,
                SummaryText TEXT,
                IndexedAt TEXT,
                TotalFiles INTEGER,
                TotalLines INTEGER,
                TotalTodos INTEGER,
                AverageComplexity REAL,
                AggregateHotspotScore REAL,
                TopRiskyFiles TEXT,
                RecentChanges TEXT,
                LanguageBreakdown TEXT,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id)
            );

            -- Chat Messages
            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id TEXT PRIMARY KEY,
                ConversationId TEXT NOT NULL,
                Role INTEGER NOT NULL,
                Content TEXT NOT NULL,
                Timestamp TEXT DEFAULT CURRENT_TIMESTAMP,
                Scope TEXT,
                IncludeContext INTEGER DEFAULT 1,
                AttachedFilePaths TEXT,
                TokenMeta TEXT
            );

            -- Calendar Blocks
            CREATE TABLE IF NOT EXISTS CalendarBlocks (
                Id TEXT PRIMARY KEY,
                TaskId TEXT,
                Title TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT NOT NULL,
                Status INTEGER DEFAULT 0,
                IsExternal INTEGER DEFAULT 0,
                FOREIGN KEY (TaskId) REFERENCES Tasks(Id)
            );

            -- Pinned Tiles
            CREATE TABLE IF NOT EXISTS PinnedTiles (
                Id TEXT PRIMARY KEY,
                TileId TEXT NOT NULL,
                Type INTEGER NOT NULL,
                ReferenceId TEXT,
                Scope TEXT,
                DockRegion INTEGER DEFAULT 0,
                PositionX REAL DEFAULT 0,
                PositionY REAL DEFAULT 0,
                Width REAL DEFAULT 400,
                Height REAL DEFAULT 300,
                IsPinned INTEGER DEFAULT 1,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            -- API Keys (encrypted)
            CREATE TABLE IF NOT EXISTS ApiKeys (
                Id TEXT PRIMARY KEY,
                Provider INTEGER NOT NULL,
                DisplayName TEXT,
                EncryptedKey BLOB,
                IsActive INTEGER DEFAULT 1,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                LastTestedAt TEXT,
                LastTestSuccess INTEGER
            );

            -- Create indexes
            CREATE INDEX IF NOT EXISTS idx_tasks_project ON Tasks(ProjectId);
            CREATE INDEX IF NOT EXISTS idx_tasks_status ON Tasks(Status);
            CREATE INDEX IF NOT EXISTS idx_files_project ON FileSummaries(ProjectId);
            CREATE INDEX IF NOT EXISTS idx_folders_project ON FolderSummaries(ProjectId);
            CREATE INDEX IF NOT EXISTS idx_chat_conversation ON ChatMessages(ConversationId);
            CREATE INDEX IF NOT EXISTS idx_calendar_time ON CalendarBlocks(StartTime, EndTime);
        ";
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region DPAPI Encryption

    public byte[] EncryptKey(string plainKey)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainKey);
        return ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
    }

    public string DecryptKey(byte[] encryptedKey)
    {
        var plainBytes = ProtectedData.Unprotect(encryptedKey, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    #endregion

    #region Projects

    public async Task SaveProjectAsync(ProjectDto project)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Projects (Id, Name, Path, Branch, LastIndexedAt, IsActive, IgnorePatterns, IncludeExtensions, CreatedAt)
            VALUES ($id, $name, $path, $branch, $lastIndexed, $isActive, $ignore, $include, $created)";
        
        cmd.Parameters.AddWithValue("$id", project.Id.ToString());
        cmd.Parameters.AddWithValue("$name", project.Name);
        cmd.Parameters.AddWithValue("$path", project.RootPath);
        cmd.Parameters.AddWithValue("$branch", DBNull.Value);
        cmd.Parameters.AddWithValue("$lastIndexed", DBNull.Value);
        cmd.Parameters.AddWithValue("$isActive", project.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$ignore", "[]");
        cmd.Parameters.AddWithValue("$include", "[]");
        cmd.Parameters.AddWithValue("$created", project.CreatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync()
    {
        var projects = new List<ProjectDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Path, IsActive, CreatedAt FROM Projects WHERE IsActive = 1";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            projects.Add(new ProjectDto
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                RootPath = reader.GetString(2),
                IsActive = reader.GetInt32(3) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(4))
            });
        }

        return projects;
    }

    public async Task<ProjectDto?> GetProjectAsync(Guid projectId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Path, IsActive, CreatedAt FROM Projects WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", projectId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ProjectDto
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                RootPath = reader.GetString(2),
                IsActive = reader.GetInt32(3) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(4))
            };
        }
        return null;
    }

    public async Task DeleteProjectAsync(Guid projectId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Projects SET IsActive = 0 WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", projectId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // Legacy method for IndexerService compatibility
    public async Task<ProjectConfig> SaveProjectConfigAsync(ProjectConfig project)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Projects (Id, Name, Path, Branch, LastIndexedAt, IsActive, IgnorePatterns, IncludeExtensions, CreatedAt)
            VALUES ($id, $name, $path, $branch, $lastIndexed, $isActive, $ignore, $include, $created)";
        
        cmd.Parameters.AddWithValue("$id", project.Id);
        cmd.Parameters.AddWithValue("$name", project.Name);
        cmd.Parameters.AddWithValue("$path", project.Path);
        cmd.Parameters.AddWithValue("$branch", project.Branch ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$lastIndexed", project.LastIndexedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$isActive", project.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$ignore", JsonSerializer.Serialize(project.IgnorePatterns));
        cmd.Parameters.AddWithValue("$include", JsonSerializer.Serialize(project.IncludeExtensions));
        cmd.Parameters.AddWithValue("$created", project.CreatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
        return project;
    }

    #endregion

    #region Tasks

    public async Task<TaskDto> SaveTaskAsync(TaskDto task)
    {
        task.UpdatedAt = DateTime.UtcNow;
        
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Tasks 
            (Id, Title, Description, EstimatedMinutes, Difficulty, Importance, AiPriorityScore, Confidence,
             DueDate, Dependencies, Subtasks, ContextScope, Status, TimeSpentSeconds, PriorityRationale,
             ProjectId, CreatedAt, UpdatedAt, CompletedAt)
            VALUES ($id, $title, $desc, $est, $diff, $imp, $aiScore, $conf, $due, $deps, $subs, $scope,
                    $status, $time, $rationale, $project, $created, $updated, $completed)";

        cmd.Parameters.AddWithValue("$id", task.Id);
        cmd.Parameters.AddWithValue("$title", task.Title);
        cmd.Parameters.AddWithValue("$desc", task.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$est", task.EstimatedMinutes);
        cmd.Parameters.AddWithValue("$diff", (int)task.Difficulty);
        cmd.Parameters.AddWithValue("$imp", (int)task.Importance);
        cmd.Parameters.AddWithValue("$aiScore", task.AiPriorityScore);
        cmd.Parameters.AddWithValue("$conf", task.Confidence);
        cmd.Parameters.AddWithValue("$due", task.DueDate?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$deps", JsonSerializer.Serialize(task.Dependencies));
        cmd.Parameters.AddWithValue("$subs", JsonSerializer.Serialize(task.Subtasks));
        cmd.Parameters.AddWithValue("$scope", task.ContextScope != null ? JsonSerializer.Serialize(task.ContextScope) : DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (int)task.Status);
        cmd.Parameters.AddWithValue("$time", task.TimeSpentSeconds);
        cmd.Parameters.AddWithValue("$rationale", task.PriorityRationale ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$project", task.ProjectId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$created", task.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$completed", task.CompletedAt?.ToString("O") ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        return task;
    }

    public async Task<List<TaskDto>> GetTasksAsync(TaskQuery? query = null)
    {
        var tasks = new List<TaskDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT * FROM Tasks WHERE 1=1";
        var cmd = connection.CreateCommand();

        if (query?.ProjectId != null)
        {
            sql += " AND ProjectId = $project";
            cmd.Parameters.AddWithValue("$project", query.ProjectId);
        }
        if (query?.Status != null)
        {
            sql += " AND Status = $status";
            cmd.Parameters.AddWithValue("$status", (int)query.Status);
        }
        if (query?.MinImportance != null)
        {
            sql += " AND Importance >= $minImp";
            cmd.Parameters.AddWithValue("$minImp", (int)query.MinImportance);
        }

        sql += $" ORDER BY {query?.SortBy ?? "AiPriorityScore"} {(query?.Descending ?? true ? "DESC" : "ASC")}";
        
        if (query?.Limit != null)
        {
            sql += " LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", query.Limit);
        }

        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tasks.Add(MapTaskFromReader(reader));
        }

        return tasks;
    }

    public async Task<TaskDto?> GetTaskAsync(string taskId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Tasks WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", taskId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapTaskFromReader(reader);
        }
        return null;
    }

    public async Task DeleteTaskAsync(string taskId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Tasks WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", taskId);
        await cmd.ExecuteNonQueryAsync();
    }

    private TaskDto MapTaskFromReader(SqliteDataReader reader)
    {
        return new TaskDto
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            EstimatedMinutes = reader.GetInt32(3),
            Difficulty = (TaskDifficulty)reader.GetInt32(4),
            Importance = (TaskPriority)reader.GetInt32(5),
            AiPriorityScore = reader.GetDouble(6),
            Confidence = reader.GetDouble(7),
            DueDate = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
            Dependencies = reader.IsDBNull(9) ? new() : JsonSerializer.Deserialize<List<string>>(reader.GetString(9)) ?? new(),
            Subtasks = reader.IsDBNull(10) ? new() : JsonSerializer.Deserialize<List<SubtaskDto>>(reader.GetString(10)) ?? new(),
            ContextScope = reader.IsDBNull(11) ? null : JsonSerializer.Deserialize<ContextScopeDto>(reader.GetString(11)),
            Status = (Shared.Models.TaskStatus)reader.GetInt32(12),
            TimeSpentSeconds = reader.GetInt32(13),
            PriorityRationale = reader.IsDBNull(14) ? null : reader.GetString(14),
            ProjectId = reader.IsDBNull(15) ? null : reader.GetString(15),
            CreatedAt = DateTime.Parse(reader.GetString(16)),
            UpdatedAt = DateTime.Parse(reader.GetString(17)),
            CompletedAt = reader.IsDBNull(18) ? null : DateTime.Parse(reader.GetString(18))
        };
    }

    #endregion

    #region File Summaries

    public async Task SaveFileSummaryAsync(FileSummaryDto summary)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO FileSummaries 
            (Id, FilePath, FileName, ProjectId, SummaryText, Language, FileSizeBytes, LastModified, IndexedAt,
             Functions, Classes, Todos, Imports, LineCount, TodoCount, ComplexityEstimate, HotspotScore, HasRecentFailingTests)
            VALUES ($id, $path, $name, $project, $summary, $lang, $size, $modified, $indexed,
                    $functions, $classes, $todos, $imports, $lines, $todoCount, $complexity, $hotspot, $failingTests)";

        cmd.Parameters.AddWithValue("$id", summary.Id);
        cmd.Parameters.AddWithValue("$path", summary.FilePath);
        cmd.Parameters.AddWithValue("$name", summary.FileName);
        cmd.Parameters.AddWithValue("$project", summary.ProjectId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$summary", summary.SummaryText ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$lang", summary.Language ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$size", summary.FileSizeBytes);
        cmd.Parameters.AddWithValue("$modified", summary.LastModified.ToString("O"));
        cmd.Parameters.AddWithValue("$indexed", summary.IndexedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$functions", JsonSerializer.Serialize(summary.Functions));
        cmd.Parameters.AddWithValue("$classes", JsonSerializer.Serialize(summary.Classes));
        cmd.Parameters.AddWithValue("$todos", JsonSerializer.Serialize(summary.Todos));
        cmd.Parameters.AddWithValue("$imports", JsonSerializer.Serialize(summary.Imports));
        cmd.Parameters.AddWithValue("$lines", summary.LineCount);
        cmd.Parameters.AddWithValue("$todoCount", summary.TodoCount);
        cmd.Parameters.AddWithValue("$complexity", summary.ComplexityEstimate);
        cmd.Parameters.AddWithValue("$hotspot", summary.HotspotScore);
        cmd.Parameters.AddWithValue("$failingTests", summary.HasRecentFailingTests ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<FileSummaryDto?> GetFileSummaryAsync(string filePath)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM FileSummaries WHERE FilePath = $path";
        cmd.Parameters.AddWithValue("$path", filePath);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FileSummaryDto
            {
                Id = reader.GetString(0),
                FilePath = reader.GetString(1),
                FileName = reader.GetString(2),
                ProjectId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                SummaryText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Language = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                FileSizeBytes = reader.GetInt64(6),
                LastModified = DateTime.Parse(reader.GetString(7)),
                IndexedAt = DateTime.Parse(reader.GetString(8)),
                Functions = reader.IsDBNull(9) ? new() : JsonSerializer.Deserialize<List<FunctionSignature>>(reader.GetString(9)) ?? new(),
                Classes = reader.IsDBNull(10) ? new() : JsonSerializer.Deserialize<List<ClassSignature>>(reader.GetString(10)) ?? new(),
                Todos = reader.IsDBNull(11) ? new() : JsonSerializer.Deserialize<List<TodoComment>>(reader.GetString(11)) ?? new(),
                Imports = reader.IsDBNull(12) ? new() : JsonSerializer.Deserialize<List<string>>(reader.GetString(12)) ?? new(),
                LineCount = reader.GetInt32(13),
                TodoCount = reader.GetInt32(14),
                ComplexityEstimate = reader.GetDouble(15),
                HotspotScore = reader.GetDouble(16),
                HasRecentFailingTests = reader.GetInt32(17) == 1
            };
        }
        return null;
    }

    #endregion

    #region Folder Summaries

    public async Task SaveFolderSummaryAsync(FolderSummaryDto summary)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO FolderSummaries 
            (Id, FolderPath, FolderName, ProjectId, SummaryText, IndexedAt, TotalFiles, TotalLines, TotalTodos,
             AverageComplexity, AggregateHotspotScore, TopRiskyFiles, RecentChanges, LanguageBreakdown)
            VALUES ($id, $path, $name, $project, $summary, $indexed, $files, $lines, $todos,
                    $complexity, $hotspot, $risky, $changes, $langs)";

        cmd.Parameters.AddWithValue("$id", summary.Id);
        cmd.Parameters.AddWithValue("$path", summary.FolderPath);
        cmd.Parameters.AddWithValue("$name", summary.FolderName);
        cmd.Parameters.AddWithValue("$project", summary.ProjectId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$summary", summary.SummaryText ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$indexed", summary.IndexedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$files", summary.TotalFiles);
        cmd.Parameters.AddWithValue("$lines", summary.TotalLines);
        cmd.Parameters.AddWithValue("$todos", summary.TotalTodos);
        cmd.Parameters.AddWithValue("$complexity", summary.AverageComplexity);
        cmd.Parameters.AddWithValue("$hotspot", summary.AggregateHotspotScore);
        cmd.Parameters.AddWithValue("$risky", JsonSerializer.Serialize(summary.TopRiskyFiles));
        cmd.Parameters.AddWithValue("$changes", JsonSerializer.Serialize(summary.RecentChanges));
        cmd.Parameters.AddWithValue("$langs", JsonSerializer.Serialize(summary.LanguageBreakdown));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<FolderSummaryDto?> GetFolderSummaryAsync(string folderPath)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM FolderSummaries WHERE FolderPath = $path";
        cmd.Parameters.AddWithValue("$path", folderPath);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FolderSummaryDto
            {
                Id = reader.GetString(0),
                FolderPath = reader.GetString(1),
                FolderName = reader.GetString(2),
                ProjectId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                SummaryText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                IndexedAt = DateTime.Parse(reader.GetString(5)),
                TotalFiles = reader.GetInt32(6),
                TotalLines = reader.GetInt32(7),
                TotalTodos = reader.GetInt32(8),
                AverageComplexity = reader.GetDouble(9),
                AggregateHotspotScore = reader.GetDouble(10),
                TopRiskyFiles = reader.IsDBNull(11) ? new() : JsonSerializer.Deserialize<List<TopRiskyFile>>(reader.GetString(11)) ?? new(),
                RecentChanges = reader.IsDBNull(12) ? new() : JsonSerializer.Deserialize<List<string>>(reader.GetString(12)) ?? new(),
                LanguageBreakdown = reader.IsDBNull(13) ? new() : JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(13)) ?? new()
            };
        }
        return null;
    }

    public async Task<IReadOnlyList<FolderSummaryDto>> GetFolderSummariesAsync(Guid projectId)
    {
        var folders = new List<FolderSummaryDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM FolderSummaries WHERE ProjectId = $projectId";
        cmd.Parameters.AddWithValue("$projectId", projectId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            folders.Add(new FolderSummaryDto
            {
                Id = reader.GetString(0),
                FolderPath = reader.GetString(1),
                FolderName = reader.GetString(2),
                ProjectId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                SummaryText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                IndexedAt = DateTime.Parse(reader.GetString(5)),
                TotalFiles = reader.GetInt32(6),
                TotalLines = reader.GetInt32(7),
                TotalTodos = reader.GetInt32(8),
                AverageComplexity = reader.GetDouble(9),
                AggregateHotspotScore = reader.GetDouble(10),
                TopRiskyFiles = reader.IsDBNull(11) ? new() : JsonSerializer.Deserialize<List<TopRiskyFile>>(reader.GetString(11)) ?? new(),
                RecentChanges = reader.IsDBNull(12) ? new() : JsonSerializer.Deserialize<List<string>>(reader.GetString(12)) ?? new(),
                LanguageBreakdown = reader.IsDBNull(13) ? new() : JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(13)) ?? new()
            });
        }
        return folders;
    }

    public async Task<IReadOnlyList<FileSummaryDto>> GetFileSummariesByFolderAsync(string folderPath)
    {
        var files = new List<FileSummaryDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM FileSummaries WHERE FilePath LIKE $path";
        cmd.Parameters.AddWithValue("$path", folderPath + "%");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            files.Add(MapFileSummaryFromReader(reader));
        }
        return files;
    }

    public async Task<IReadOnlyList<FileSummaryDto>> SearchFilesAsync(string query, Guid? projectId)
    {
        var files = new List<FileSummaryDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        var sql = "SELECT * FROM FileSummaries WHERE (FileName LIKE $query OR SummaryText LIKE $query)";
        if (projectId.HasValue)
        {
            sql += " AND ProjectId = $projectId";
            cmd.Parameters.AddWithValue("$projectId", projectId.Value.ToString());
        }
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$query", $"%{query}%");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            files.Add(MapFileSummaryFromReader(reader));
        }
        return files;
    }

    private FileSummaryDto MapFileSummaryFromReader(SqliteDataReader reader)
    {
        return new FileSummaryDto
        {
            Id = reader.GetString(0),
            FilePath = reader.GetString(1),
            FileName = reader.GetString(2),
            ProjectId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            SummaryText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            Language = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            FileSizeBytes = reader.GetInt64(6),
            LastModified = DateTime.Parse(reader.GetString(7)),
            IndexedAt = DateTime.Parse(reader.GetString(8)),
            Functions = reader.IsDBNull(9) ? new() : JsonSerializer.Deserialize<List<FunctionSignature>>(reader.GetString(9)) ?? new(),
            Classes = reader.IsDBNull(10) ? new() : JsonSerializer.Deserialize<List<ClassSignature>>(reader.GetString(10)) ?? new(),
            Todos = reader.IsDBNull(11) ? new() : JsonSerializer.Deserialize<List<TodoComment>>(reader.GetString(11)) ?? new(),
            Imports = reader.IsDBNull(12) ? new() : JsonSerializer.Deserialize<List<string>>(reader.GetString(12)) ?? new(),
            LineCount = reader.GetInt32(13),
            TodoCount = reader.GetInt32(14),
            ComplexityEstimate = reader.GetDouble(15),
            HotspotScore = reader.GetDouble(16),
            HasRecentFailingTests = reader.GetInt32(17) == 1
        };
    }

    #endregion

    #region API Keys

    public async Task SaveApiKeyAsync(ApiKeyDto apiKey, string? plainKey = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO ApiKeys (Id, Provider, DisplayName, EncryptedKey, IsActive, CreatedAt, LastTestedAt, LastTestSuccess)
            VALUES ($id, $provider, $name, $key, $active, $created, $tested, $success)";

        cmd.Parameters.AddWithValue("$id", apiKey.Id);
        cmd.Parameters.AddWithValue("$provider", (int)apiKey.Provider);
        cmd.Parameters.AddWithValue("$name", apiKey.DisplayName ?? (object)DBNull.Value);
        
        if (plainKey != null)
        {
            cmd.Parameters.AddWithValue("$key", EncryptKey(plainKey));
        }
        else if (apiKey.EncryptedKey != null)
        {
            cmd.Parameters.AddWithValue("$key", Convert.FromBase64String(apiKey.EncryptedKey));
        }
        else
        {
            cmd.Parameters.AddWithValue("$key", DBNull.Value);
        }
        
        cmd.Parameters.AddWithValue("$active", apiKey.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", apiKey.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$tested", apiKey.LastTestedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$success", apiKey.LastTestSuccess.HasValue ? (apiKey.LastTestSuccess.Value ? 1 : 0) : DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ApiKeyDto>> GetApiKeysAsync()
    {
        var keys = new List<ApiKeyDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Provider, DisplayName, IsActive, CreatedAt, LastTestedAt, LastTestSuccess FROM ApiKeys";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(new ApiKeyDto
            {
                Id = reader.GetString(0),
                Provider = (LlmProvider)reader.GetInt32(1),
                DisplayName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                IsActive = reader.GetInt32(3) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(4)),
                LastTestedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                LastTestSuccess = reader.IsDBNull(6) ? null : reader.GetInt32(6) == 1,
                MaskedKey = "***" // Keys are never returned in plain text
            });
        }

        return keys;
    }

    public async Task<string?> GetDecryptedKeyAsync(LlmProvider provider)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT EncryptedKey FROM ApiKeys WHERE Provider = $provider AND IsActive = 1 ORDER BY CreatedAt DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$provider", (int)provider);

        var result = await cmd.ExecuteScalarAsync();
        if (result is byte[] encrypted)
        {
            return DecryptKey(encrypted);
        }
        return null;
    }

    public async Task DeleteApiKeyAsync(string keyId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ApiKeys WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", keyId);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Pinned Tiles

    public async Task SavePinnedTileAsync(PinnedTileDto tile)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO PinnedTiles 
            (Id, TileId, Type, ReferenceId, Scope, DockRegion, PositionX, PositionY, Width, Height, IsPinned, CreatedAt)
            VALUES ($id, $tileId, $type, $refId, $scope, $dock, $x, $y, $w, $h, $pinned, $created)";

        cmd.Parameters.AddWithValue("$id", tile.Id.ToString());
        cmd.Parameters.AddWithValue("$tileId", tile.TileId);
        cmd.Parameters.AddWithValue("$type", (int)tile.TileType);
        cmd.Parameters.AddWithValue("$refId", tile.ReferenceId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$scope", tile.Scope != null ? JsonSerializer.Serialize(tile.Scope) : DBNull.Value);
        cmd.Parameters.AddWithValue("$dock", (int)tile.DockRegion);
        cmd.Parameters.AddWithValue("$x", tile.PositionX);
        cmd.Parameters.AddWithValue("$y", tile.PositionY);
        cmd.Parameters.AddWithValue("$w", tile.Width);
        cmd.Parameters.AddWithValue("$h", tile.Height);
        cmd.Parameters.AddWithValue("$pinned", tile.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", tile.CreatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<PinnedTileDto>> GetPinnedTilesAsync()
    {
        var tiles = new List<PinnedTileDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, TileId, Type, ReferenceId, Scope, DockRegion, PositionX, PositionY, Width, Height, IsPinned, CreatedAt FROM PinnedTiles WHERE IsPinned = 1";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tiles.Add(new PinnedTileDto
            {
                Id = Guid.Parse(reader.GetString(0)),
                TileId = reader.GetString(1),
                TileType = (TileType)reader.GetInt32(2),
                ReferenceId = reader.IsDBNull(3) ? null : reader.GetString(3),
                Scope = reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<ContextScopeDto>(reader.GetString(4)),
                DockRegion = (DockRegion)reader.GetInt32(5),
                PositionX = reader.GetDouble(6),
                PositionY = reader.GetDouble(7),
                Width = reader.GetDouble(8),
                Height = reader.GetDouble(9),
                IsPinned = reader.GetInt32(10) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(11))
            });
        }

        return tiles;
    }

    public async Task DeletePinnedTileAsync(Guid tileId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PinnedTiles WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", tileId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Calendar Blocks

    public async Task SaveCalendarBlockAsync(CalendarBlockDto block)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO CalendarBlocks (Id, TaskId, Title, StartTime, EndTime, Status, IsExternal)
            VALUES ($id, $taskId, $title, $start, $end, $status, $external)";

        cmd.Parameters.AddWithValue("$id", block.Id);
        cmd.Parameters.AddWithValue("$taskId", block.TaskId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$title", block.Title);
        cmd.Parameters.AddWithValue("$start", block.StartTime.ToString("O"));
        cmd.Parameters.AddWithValue("$end", block.EndTime.ToString("O"));
        cmd.Parameters.AddWithValue("$status", (int)block.Status);
        cmd.Parameters.AddWithValue("$external", block.IsExternal ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<CalendarBlockDto>> GetCalendarBlocksAsync(DateTime start, DateTime end)
    {
        var blocks = new List<CalendarBlockDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM CalendarBlocks WHERE StartTime >= $start AND EndTime <= $end ORDER BY StartTime";
        cmd.Parameters.AddWithValue("$start", start.ToString("O"));
        cmd.Parameters.AddWithValue("$end", end.ToString("O"));

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            blocks.Add(new CalendarBlockDto
            {
                Id = reader.GetString(0),
                TaskId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Title = reader.GetString(2),
                StartTime = DateTime.Parse(reader.GetString(3)),
                EndTime = DateTime.Parse(reader.GetString(4)),
                Status = (ScheduleBlockStatus)reader.GetInt32(5),
                IsExternal = reader.GetInt32(6) == 1
            });
        }

        return blocks;
    }

    #endregion

    #region Calendar Events

    public async Task<IReadOnlyList<CalendarEventDto>> GetCalendarEventsAsync(DateTime start, DateTime end)
    {
        var events = new List<CalendarEventDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Use CalendarBlocks table for now as events - can add separate CalendarEvents table later
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, StartTime, EndTime, IsExternal FROM CalendarBlocks WHERE StartTime >= $start AND EndTime <= $end ORDER BY StartTime";
        cmd.Parameters.AddWithValue("$start", start.ToString("O"));
        cmd.Parameters.AddWithValue("$end", end.ToString("O"));

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new CalendarEventDto
            {
                Id = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                StartTime = DateTime.Parse(reader.GetString(2)),
                EndTime = DateTime.Parse(reader.GetString(3)),
                IsAllDay = false
            });
        }

        return events;
    }

    public async Task SaveCalendarEventAsync(CalendarEventDto evt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO CalendarBlocks (Id, TaskId, Title, StartTime, EndTime, Status, IsExternal)
            VALUES ($id, NULL, $title, $start, $end, 0, 1)";

        cmd.Parameters.AddWithValue("$id", evt.Id.ToString());
        cmd.Parameters.AddWithValue("$title", evt.Title);
        cmd.Parameters.AddWithValue("$start", evt.StartTime.ToString("O"));
        cmd.Parameters.AddWithValue("$end", evt.EndTime.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteCalendarEventAsync(Guid eventId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM CalendarBlocks WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", eventId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
