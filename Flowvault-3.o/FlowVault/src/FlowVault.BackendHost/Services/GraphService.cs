using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;
using FlowVault.BackendHost.Persistence;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// Workflow graph generation and layout service
/// </summary>
public class GraphService
{
    private readonly ILogger<GraphService> _logger;
    private readonly DatabaseService _database;

    private const double NodeWidth = 140;
    private const double NodeHeight = 60;
    private const double HorizontalSpacing = 40;
    private const double VerticalSpacing = 80;

    public GraphService(ILogger<GraphService> logger, DatabaseService database)
    {
        _logger = logger;
        _database = database;
    }

    /// <summary>
    /// Generate workflow graph for a project
    /// </summary>
    public async Task<WorkflowGraphDto> GenerateGraphAsync(Guid? projectId, CancellationToken ct)
    {
        var query = projectId.HasValue 
            ? new TaskQuery { ProjectId = projectId.Value.ToString() }
            : new TaskQuery();

        var tasks = await _database.GetTasksAsync(query);
        var taskIds = tasks.Select(t => t.Id).ToArray();
        
        return await GenerateWorkflowGraphAsync(taskIds, ct);
    }

    /// <summary>
    /// Generate workflow graph from tasks and their dependencies
    /// </summary>
    public async Task<WorkflowGraphDto> GenerateWorkflowGraphAsync(string[] taskIds, CancellationToken ct)
    {
        var graph = new WorkflowGraphDto();
        var tasks = new Dictionary<string, TaskDto>();

        // Load all tasks
        foreach (var taskId in taskIds)
        {
            var task = await _database.GetTaskAsync(taskId);
            if (task != null)
            {
                tasks[task.Id] = task;
            }
        }

        if (tasks.Count == 0)
        {
            _logger.LogWarning("No tasks found for workflow graph generation");
            return graph;
        }

        // Create nodes
        foreach (var task in tasks.Values)
        {
            graph.Nodes.Add(new WorkflowNodeDto
            {
                Id = $"node_{task.Id}",
                TaskId = task.Id,
                Label = TruncateLabel(task.Title, 20),
                Width = NodeWidth,
                Height = NodeHeight,
                Status = task.Status,
                Color = GetStatusColor(task.Status)
            });
        }

        // Create edges from dependencies
        foreach (var task in tasks.Values)
        {
            foreach (var depId in task.Dependencies)
            {
                if (tasks.ContainsKey(depId))
                {
                    graph.Edges.Add(new WorkflowEdgeDto
                    {
                        Id = $"edge_{depId}_{task.Id}",
                        SourceId = $"node_{depId}",
                        TargetId = $"node_{task.Id}"
                    });
                }
            }
        }

        // Apply layout algorithm
        ApplyHierarchicalLayout(graph, tasks);

        _logger.LogInformation("Generated workflow graph with {Nodes} nodes and {Edges} edges", 
            graph.Nodes.Count, graph.Edges.Count);

        return graph;
    }

    #region Layout Algorithm

    /// <summary>
    /// Apply hierarchical (layered) layout using topological sort
    /// </summary>
    private void ApplyHierarchicalLayout(WorkflowGraphDto graph, Dictionary<string, TaskDto> tasks)
    {
        // Build adjacency list for topological sort
        var inDegree = new Dictionary<string, int>();
        var adjacency = new Dictionary<string, List<string>>();

        foreach (var node in graph.Nodes)
        {
            inDegree[node.Id] = 0;
            adjacency[node.Id] = new List<string>();
        }

        foreach (var edge in graph.Edges)
        {
            adjacency[edge.SourceId].Add(edge.TargetId);
            inDegree[edge.TargetId]++;
        }

        // Kahn's algorithm for layered assignment
        var layers = new List<List<string>>();
        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = new HashSet<string>();

        while (queue.Count > 0)
        {
            var currentLayer = new List<string>();
            var levelSize = queue.Count;

            for (int i = 0; i < levelSize; i++)
            {
                var nodeId = queue.Dequeue();
                if (visited.Contains(nodeId)) continue;
                
                visited.Add(nodeId);
                currentLayer.Add(nodeId);

                foreach (var neighbor in adjacency[nodeId])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (currentLayer.Count > 0)
            {
                layers.Add(currentLayer);
            }
        }

        // Handle any remaining nodes (cycles or disconnected)
        var remaining = graph.Nodes.Where(n => !visited.Contains(n.Id)).Select(n => n.Id).ToList();
        if (remaining.Count > 0)
        {
            layers.Add(remaining);
        }

        // Assign positions based on layers
        for (int layer = 0; layer < layers.Count; layer++)
        {
            var nodesInLayer = layers[layer];
            var layerWidth = nodesInLayer.Count * (NodeWidth + HorizontalSpacing) - HorizontalSpacing;
            var startX = -layerWidth / 2;

            for (int i = 0; i < nodesInLayer.Count; i++)
            {
                var node = graph.Nodes.FirstOrDefault(n => n.Id == nodesInLayer[i]);
                if (node != null)
                {
                    node.X = startX + i * (NodeWidth + HorizontalSpacing);
                    node.Y = layer * (NodeHeight + VerticalSpacing);
                }
            }
        }

        // Center the graph
        if (graph.Nodes.Count > 0)
        {
            var minX = graph.Nodes.Min(n => n.X);
            var minY = graph.Nodes.Min(n => n.Y);

            foreach (var node in graph.Nodes)
            {
                node.X -= minX - 50;
                node.Y -= minY - 50;
            }
        }
    }

    #endregion

    #region Helpers

    private string TruncateLabel(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
    }

    private string GetStatusColor(Shared.Models.TaskStatus status)
    {
        return status switch
        {
            Shared.Models.TaskStatus.NotStarted => "#6B7280", // Gray
            Shared.Models.TaskStatus.InProgress => "#3B82F6", // Blue
            Shared.Models.TaskStatus.Blocked => "#EF4444",    // Red
            Shared.Models.TaskStatus.Completed => "#10B981",  // Green
            Shared.Models.TaskStatus.Cancelled => "#9CA3AF", // Light gray
            _ => "#6B7280"
        };
    }

    #endregion
}
