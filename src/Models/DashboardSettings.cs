namespace ClaudeAdoCompanion.Models;

public class DashboardSettings
{
    public string AdoOrg { get; set; } = "";

    public string AdoProject { get; set; } = "";

    public string AreaPath { get; set; } = "";

    public string CopilotUserId { get; set; } = "";

    public string RepoProjectGuid { get; set; } = "";

    public string RepoGuid { get; set; } = "";

    public string BranchRef { get; set; } = "GBmain";

    public string TriagePipelineName { get; set; } = "";

    public string? IterationPath { get; set; }

    public int MaxBugsDefault { get; set; } = 100;

    public string ClaudeProjectsPath { get; set; } = "";

    /// <summary>
    /// True when the minimum required settings (org, project, area path) are configured.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AdoOrg)
        && !string.IsNullOrWhiteSpace(AdoProject)
        && !string.IsNullOrWhiteSpace(AreaPath);
}
