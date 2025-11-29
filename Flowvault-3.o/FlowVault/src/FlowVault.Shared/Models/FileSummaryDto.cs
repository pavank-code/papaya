namespace FlowVault.Shared.Models;

/// <summary>
/// File summary data transfer object
/// </summary>
public class FileSummaryDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
    
    // Parsed metadata
    public List<FunctionSignature> Functions { get; set; } = new();
    public List<ClassSignature> Classes { get; set; } = new();
    public List<TodoComment> Todos { get; set; } = new();
    public List<string> Imports { get; set; } = new();
    
    // Metrics
    public int LineCount { get; set; }
    public int TodoCount { get; set; }
    public double ComplexityEstimate { get; set; }
    public double HotspotScore { get; set; }
    public bool HasRecentFailingTests { get; set; }
}

/// <summary>
/// Function signature extracted from file
/// </summary>
public class FunctionSignature
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = new();
    public int LineNumber { get; set; }
    public string? Documentation { get; set; }
}

/// <summary>
/// Class signature extracted from file
/// </summary>
public class ClassSignature
{
    public string Name { get; set; } = string.Empty;
    public string? BaseClass { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public int LineNumber { get; set; }
    public string? Documentation { get; set; }
}

/// <summary>
/// TODO comment found in file
/// </summary>
public class TodoComment
{
    public string Text { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string Type { get; set; } = "TODO"; // TODO, FIXME, HACK, etc.
}
