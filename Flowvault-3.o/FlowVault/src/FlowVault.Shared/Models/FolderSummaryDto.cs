namespace FlowVault.Shared.Models;

/// <summary>
/// Folder summary data transfer object
/// </summary>
public class FolderSummaryDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
    
    // Aggregated metrics
    public int TotalFiles { get; set; }
    public int TotalLines { get; set; }
    public int TotalTodos { get; set; }
    public double AverageComplexity { get; set; }
    public double AggregateHotspotScore { get; set; }
    
    // Top items for quick display
    public List<TopRiskyFile> TopRiskyFiles { get; set; } = new();
    public List<string> RecentChanges { get; set; } = new();
    public Dictionary<string, int> LanguageBreakdown { get; set; } = new();
}

/// <summary>
/// Top risky file summary
/// </summary>
public class TopRiskyFile
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public double HotspotScore { get; set; }
    public int TodoCount { get; set; }
    public string RiskReason { get; set; } = string.Empty;
}
