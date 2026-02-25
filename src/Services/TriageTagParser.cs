using ClaudeAdoCompanion.Models;

namespace ClaudeAdoCompanion.Services;

public static class TriageTagParser
{
    public static TriageStatus ParseTags(string tags)
    {
        var tagSet = new HashSet<string>(
            (tags ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        var copilotReadiness = string.Empty;
        if (tagSet.Contains("copilot-ready"))
            copilotReadiness = "Ready";
        else if (tagSet.Contains("copilot-possible"))
            copilotReadiness = "Possible";
        else if (tagSet.Contains("human-required"))
            copilotReadiness = "Human Required";

        return new TriageStatus
        {
            IsTriaged = tagSet.Contains("triaged"),
            NeedsInfo = tagSet.Contains("needs-info"),
            HighRoi = tagSet.Contains("high-roi"),
            CopilotReadiness = copilotReadiness,
        };
    }
}
