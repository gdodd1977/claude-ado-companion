namespace ClaudeAdoCompanion.Models;

public class SessionMessage
{
    public string Type { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? ToolName { get; set; }

    public string? ToolInput { get; set; }
}
