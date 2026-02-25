using ClaudeAdoCompanion.Models;

namespace ClaudeAdoCompanion.Services;

public class DemoDataService : IAdoService
{
    private static readonly List<TriagedBug> DemoBugs =
    [
        new()
        {
            Id = 12001, Title = "AI Step Planner returns wrong trigger for SharePoint events",
            State = "Active", Severity = "2 - High", Priority = 1,
            AssignedTo = "Jane Smith", Tags = "triaged; copilot-ready; high-roi; ai-planner",
            CreatedDate = DateTimeOffset.Now.AddDays(-12), ChangedDate = DateTimeOffset.Now.AddDays(-1),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12001",
            TriageStatus = new TriageStatus
            {
                IsTriaged = true, HighRoi = true, CopilotReadiness = "Ready",
            },
        },
        new()
        {
            Id = 12002, Title = "Flow Q&A hallucinates connector names when user asks about premium connectors",
            State = "Active", Severity = "2 - High", Priority = 2,
            AssignedTo = "", Tags = "triaged; copilot-ready; high-roi; ai-planner",
            CreatedDate = DateTimeOffset.Now.AddDays(-8), ChangedDate = DateTimeOffset.Now.AddDays(-3),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12002",
            TriageStatus = new TriageStatus
            {
                IsTriaged = true, HighRoi = true, CopilotReadiness = "Ready",
            },
        },
        new()
        {
            Id = 12003, Title = "Planner generates duplicate actions when user says 'also send a Teams message'",
            State = "Active", Severity = "3 - Medium", Priority = 2,
            AssignedTo = "Bob Jones", Tags = "triaged; copilot-possible; ai-planner; duplicate-actions",
            CreatedDate = DateTimeOffset.Now.AddDays(-20), ChangedDate = DateTimeOffset.Now.AddDays(-5),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12003",
            TriageStatus = new TriageStatus
            {
                IsTriaged = true, CopilotReadiness = "Possible",
            },
        },
        new()
        {
            Id = 12004, Title = "Runtime step execution times out for Dataverse bulk operations",
            State = "Active", Severity = "1 - Critical", Priority = 1,
            AssignedTo = "Alice Chen", Tags = "triaged; human-required; runtime; timeout",
            CreatedDate = DateTimeOffset.Now.AddDays(-3), ChangedDate = DateTimeOffset.Now.AddHours(-6),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12004",
            TriageStatus = new TriageStatus
            {
                IsTriaged = true, CopilotReadiness = "Human Required",
            },
        },
        new()
        {
            Id = 12005, Title = "Step planner suggests deprecated 'Send an HTTP request' action instead of HTTP connector",
            State = "Active", Severity = "3 - Medium", Priority = 3,
            AssignedTo = "", Tags = "triaged; copilot-possible; needs-info; ai-planner; deprecated",
            CreatedDate = DateTimeOffset.Now.AddDays(-15), ChangedDate = DateTimeOffset.Now.AddDays(-7),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12005",
            TriageStatus = new TriageStatus
            {
                IsTriaged = true, NeedsInfo = true, CopilotReadiness = "Possible",
            },
        },
        new()
        {
            Id = 12006, Title = "Canvas crashes when AI suggests flow with 50+ actions",
            State = "Active", Severity = "1 - Critical", Priority = 1,
            AssignedTo = "Dave Wilson", Tags = "triaged; human-required; high-roi; canvas; crash",
            CreatedDate = DateTimeOffset.Now.AddDays(-2), ChangedDate = DateTimeOffset.Now.AddHours(-2),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12006",
            TriageStatus = new TriageStatus
            {
                IsTriaged = true, HighRoi = true, CopilotReadiness = "Human Required",
            },
        },
        new()
        {
            Id = 12007, Title = "Intermittent 429 errors from OpenAI during peak hours",
            State = "New", Severity = "2 - High", Priority = 2,
            AssignedTo = "", Tags = "",
            CreatedDate = DateTimeOffset.Now.AddDays(-1), ChangedDate = DateTimeOffset.Now.AddHours(-8),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12007",
            TriageStatus = new TriageStatus(),
        },
        new()
        {
            Id = 12008, Title = "User reports AI chat ignores context from existing flow definition",
            State = "New", Severity = "3 - Medium", Priority = 3,
            AssignedTo = "", Tags = "",
            CreatedDate = DateTimeOffset.Now.AddDays(-4), ChangedDate = DateTimeOffset.Now.AddDays(-4),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12008",
            TriageStatus = new TriageStatus(),
        },
        new()
        {
            Id = 12009, Title = "Planner action ordering wrong for approval workflows with parallel branches",
            State = "Active", Severity = "2 - High", Priority = 2,
            AssignedTo = "", Tags = "triaged; copilot-ready; high-roi; ai-planner",
            CreatedDate = DateTimeOffset.Now.AddDays(-6), ChangedDate = DateTimeOffset.Now.AddDays(-1),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12009",
            TriageStatus = new TriageStatus
            {
                IsTriaged = true, HighRoi = true, CopilotReadiness = "Ready",
            },
        },
        new()
        {
            Id = 12010, Title = "Token count exceeds limit when flow definition has many nested conditions",
            State = "Active", Severity = "4 - Low", Priority = 4,
            AssignedTo = "Eve Park", Tags = "triaged; human-required; token-limit",
            CreatedDate = DateTimeOffset.Now.AddDays(-30), ChangedDate = DateTimeOffset.Now.AddDays(-10),
            AdoUrl = "https://dev.azure.com/demo/DemoProject/_workitems/edit/12010",
            TriageStatus = new TriageStatus
            {
                IsTriaged = true, CopilotReadiness = "Human Required",
            },
        },
    ];

    public Task<List<TriagedBug>> GetTriagedBugsAsync() => Task.FromResult(DemoBugs);

    public Task<TriagedBug?> GetBugAsync(int id) =>
        Task.FromResult(DemoBugs.FirstOrDefault(b => b.Id == id));

    public Task AssignToCopilotAsync(int id) => Task.CompletedTask;

    public Task RetriageAsync(int id) => Task.CompletedTask;

    public Task BatchTriageAsync(int max) => Task.CompletedTask;
}
