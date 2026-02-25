using ClaudeAdoCompanion.Models;

namespace ClaudeAdoCompanion.Services;

public interface ISessionService
{
    Task<List<SessionSummary>> ListSessionsAsync(int max = 20, bool triageOnly = false);

    Task<List<SessionMessage>> GetSessionAsync(string sessionId);

    string? GetActiveSessionId();

    IAsyncEnumerable<SessionMessage> StreamSessionAsync(string sessionId, CancellationToken cancellationToken);
}
