using ClaudeAdoCompanion.Models;

namespace ClaudeAdoCompanion.Services;

public interface IAdoService
{
    Task<List<TriagedBug>> GetTriagedBugsAsync();

    Task<TriagedBug?> GetBugAsync(int id);

    Task AssignToCopilotAsync(int id);

    Task RetriageAsync(int id);

    Task BatchTriageAsync(int max);
}
