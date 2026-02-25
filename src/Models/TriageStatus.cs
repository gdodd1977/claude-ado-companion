namespace ClaudeAdoCompanion.Models;

public class TriageStatus
{
    public bool IsTriaged { get; set; }

    public bool NeedsInfo { get; set; }

    public bool HighRoi { get; set; }

    public string CopilotReadiness { get; set; } = string.Empty; // "Ready", "Possible", "Human Required", or ""
}
