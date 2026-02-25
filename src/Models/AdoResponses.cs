using System.Text.Json.Serialization;

namespace ClaudeAdoCompanion.Models;

public class WiqlResponse
{
    [JsonPropertyName("workItems")]
    public List<WiqlWorkItemRef> WorkItems { get; set; } = [];
}

public class WiqlWorkItemRef
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

public class WorkItemBatchRequest
{
    [JsonPropertyName("ids")]
    public List<int> Ids { get; set; } = [];

    [JsonPropertyName("fields")]
    public List<string> Fields { get; set; } = [];
}

public class WorkItemBatchResponse
{
    [JsonPropertyName("value")]
    public List<WorkItemResponse> Value { get; set; } = [];
}

public class WorkItemResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, object?> Fields { get; set; } = [];
}
