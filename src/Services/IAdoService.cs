using ClaudeAdoCompanion.Models;

namespace ClaudeAdoCompanion.Services;

public record AssignCopilotResult(bool Assigned, bool BranchLinked, string Message);

public interface IAdoService
{
    Task<List<TriagedBug>> GetTriagedBugsAsync();

    Task<TriagedBug?> GetBugAsync(int id);

    Task<AssignCopilotResult> AssignToCopilotAsync(int id);

    Task RetriageAsync(int id);

    Task BatchTriageAsync(int max);
}
