namespace ClaudeAdoCompanion.Models;

public class TriagedBug
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public int Priority { get; set; }

    public string AssignedTo { get; set; } = string.Empty;

    public string Tags { get; set; } = string.Empty;

    public DateTimeOffset CreatedDate { get; set; }

    public DateTimeOffset ChangedDate { get; set; }

    public TriageStatus? TriageStatus { get; set; }

    public string AdoUrl { get; set; } = string.Empty;
}
