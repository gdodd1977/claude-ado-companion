namespace ClaudeAdoCompanion.Models;

public class SessionSummary
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    public string Preview { get; set; } = string.Empty;
}
