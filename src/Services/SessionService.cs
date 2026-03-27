using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeAdoCompanion.Models;
using Microsoft.Extensions.Options;

namespace ClaudeAdoCompanion.Services;

public class SessionService : ISessionService
{
    private readonly IOptionsMonitor<DashboardSettings> _settingsMonitor;
    private readonly ILogger<SessionService> _logger;
    private DashboardSettings _settings => _settingsMonitor.CurrentValue;

    private readonly string _resolvedProjectsPath;

    public SessionService(IOptionsMonitor<DashboardSettings> settings, ILogger<SessionService> logger)
    {
        _settingsMonitor = settings;
        _logger = logger;
        _resolvedProjectsPath = ResolveProjectsPath(settings.CurrentValue.ClaudeProjectsPath);
    }

    private string ResolveProjectsPath(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var claudeProjectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");

        // Strategy 1: Derive from repo root (fast, exact match)
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Directory.GetParent(dir)?.FullName;

        if (dir != null)
        {
            var claudeDir = Path.Combine(claudeProjectsRoot,
                dir.Replace(":", "-").Replace(Path.DirectorySeparatorChar, '-').Replace('/', '-'));

            if (Directory.Exists(claudeDir))
            {
                _logger.LogInformation("Auto-detected Claude projects path from repo root: {Path}", claudeDir);
                return claudeDir;
            }
        }

        // Strategy 2: Scan ~/.claude/projects/ for the directory with the most recent .jsonl file
        if (Directory.Exists(claudeProjectsRoot))
        {
            var mostRecent = Directory.GetDirectories(claudeProjectsRoot)
                .Select(d => new
                {
                    Path = d,
                    Latest = Directory.GetFiles(d, "*.jsonl").DefaultIfEmpty()
                        .Max(f => f != null ? File.GetLastWriteTimeUtc(f) : DateTime.MinValue)
                })
                .Where(x => x.Latest > DateTime.MinValue)
                .OrderByDescending(x => x.Latest)
                .FirstOrDefault();

            if (mostRecent != null)
            {
                _logger.LogInformation("Auto-detected Claude projects path from most recent activity: {Path}", mostRecent.Path);
                return mostRecent.Path;
            }
        }

        _logger.LogWarning("Could not auto-detect Claude projects path");
        return configured;
    }

    public Task<List<SessionSummary>> ListSessionsAsync(int max = 20, bool triageOnly = false)
    {
        var results = new List<SessionSummary>();
        var dir = _resolvedProjectsPath;

        if (!Directory.Exists(dir))
        {
            _logger.LogWarning("Claude projects directory not found: {Dir}", dir);
            return Task.FromResult(results);
        }

        var files = Directory.GetFiles(dir, "*.jsonl")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(max * 3); // Fetch extra since we may filter

        foreach (var file in files)
        {
            try
            {
                var (isTriageSession, preview) = ScanSessionFile(file.FullName);

                if (triageOnly && !isTriageSession)
                {
                    continue;
                }

                var sessionId = Path.GetFileNameWithoutExtension(file.Name);
                results.Add(new SessionSummary
                {
                    Id = sessionId,
                    Timestamp = file.LastWriteTimeUtc,
                    Preview = preview,
                });

                if (results.Count >= max)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan session file: {File}", file.FullName);
            }
        }

        return Task.FromResult(results);
    }

    public Task<List<SessionMessage>> GetSessionAsync(string sessionId)
    {
        var messages = new List<SessionMessage>();
        var filePath = Path.Combine(_resolvedProjectsPath, $"{sessionId}.jsonl");

        if (!File.Exists(filePath))
        {
            return Task.FromResult(messages);
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var type = GetStringProp(root, "type");
                var timestamp = GetTimestamp(root);

                if (type == "user" || type == "assistant")
                {
                    ParseMessageBlocks(root, type, timestamp, messages);
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        // Also try to read subagent files
        var subagentsDir = Path.Combine(_resolvedProjectsPath, sessionId, "subagents");
        if (Directory.Exists(subagentsDir))
        {
            foreach (var subFile in Directory.GetFiles(subagentsDir, "*.jsonl").OrderBy(f => f))
            {
                ParseSubagentFile(subFile, messages);
            }
        }

        messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return Task.FromResult(messages);
    }

    public string? GetActiveSessionId(DateTimeOffset? createdAfter = null)
    {
        var dir = _resolvedProjectsPath;
        if (!Directory.Exists(dir))
        {
            return null;
        }

        // Find the most recently modified triage session.
        // ScanSessionFile only matches sessions whose FIRST user message contains
        // a triage command, so interactive sessions won't match even if they
        // discuss triage.
        var files = Directory.GetFiles(dir, "*.jsonl")
            .Select(f => new FileInfo(f))
            .Where(f => DateTimeOffset.UtcNow - f.LastWriteTimeUtc < TimeSpan.FromMinutes(5));

        // If a launch timestamp is provided, only consider sessions created after it.
        // This prevents matching old triage sessions from previous runs.
        if (createdAfter.HasValue)
        {
            files = files.Where(f => new DateTimeOffset(f.CreationTimeUtc, TimeSpan.Zero) >= createdAfter.Value);
        }

        var candidates = files.OrderByDescending(f => f.LastWriteTimeUtc);

        foreach (var file in candidates)
        {
            try
            {
                var (isTriageSession, _) = ScanSessionFile(file.FullName);
                if (isTriageSession)
                {
                    return Path.GetFileNameWithoutExtension(file.Name);
                }
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    public async IAsyncEnumerable<SessionMessage> StreamSessionAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_resolvedProjectsPath, $"{sessionId}.jsonl");

        if (!File.Exists(filePath))
        {
            yield break;
        }

        long lastPosition = 0;
        var seenLines = new HashSet<int>(); // Track line hashes to avoid duplicates
        int stalePollCount = 0;
        const int maxStalePollsBeforeComplete = 120; // 120 * 500ms = 60 seconds of no new content

        while (!cancellationToken.IsCancellationRequested)
        {
            var newMessages = new List<SessionMessage>();
            bool hasNewContent = false;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(lastPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var lineHash = line.GetHashCode();
                    if (seenLines.Contains(lineHash))
                    {
                        continue;
                    }

                    seenLines.Add(lineHash);
                    hasNewContent = true;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        var type = GetStringProp(root, "type");
                        var timestamp = GetTimestamp(root);

                        if (type == "user" || type == "assistant")
                        {
                            ParseMessageBlocks(root, type, timestamp, newMessages);
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed lines
                    }
                }

                lastPosition = stream.Position;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "IO error reading session file for streaming");
            }

            foreach (var msg in newMessages)
            {
                yield return msg;
            }

            // Detect completion: if no new content for 10 seconds, the session is done
            if (hasNewContent)
            {
                stalePollCount = 0;
            }
            else
            {
                stalePollCount++;
                if (stalePollCount >= maxStalePollsBeforeComplete && lastPosition > 0)
                {
                    _logger.LogInformation("Session {SessionId} stream ending: no new content for {Seconds}s",
                        sessionId, maxStalePollsBeforeComplete * 500 / 1000);
                    yield break;
                }
            }

            // Poll every 500ms for new content
            try
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                yield break;
            }
        }
    }

    private static (bool IsTriageSession, string Preview) ScanSessionFile(string filePath)
    {
        string preview = string.Empty;
        bool foundTriage = false;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = GetStringProp(root, "type");

                if (type == "user" && string.IsNullOrEmpty(preview))
                {
                    preview = ExtractUserPreview(root);

                    // Only consider it a triage session if the FIRST user message
                    // contains a triage command. This prevents interactive sessions
                    // (where the user discusses triage) from matching.
                    if (preview.Contains("/triage-bug", StringComparison.OrdinalIgnoreCase) ||
                        preview.Contains("/triage-bugs", StringComparison.OrdinalIgnoreCase))
                    {
                        foundTriage = true;
                    }

                    break; // First user message is enough to decide
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return (foundTriage, preview);
    }

    private static string ExtractUserPreview(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message))
        {
            return string.Empty;
        }

        if (!message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return Truncate(content.GetString() ?? string.Empty, 120);
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (GetStringProp(block, "type") == "text")
                {
                    return Truncate(GetStringProp(block, "text"), 120);
                }
            }
        }

        return string.Empty;
    }

    private static void ParseMessageBlocks(JsonElement root, string type, DateTimeOffset timestamp, List<SessionMessage> messages)
    {
        if (!root.TryGetProperty("message", out var message))
        {
            return;
        }

        if (!message.TryGetProperty("content", out var content))
        {
            return;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            messages.Add(new SessionMessage
            {
                Type = type,
                Timestamp = timestamp,
                Text = content.GetString() ?? string.Empty,
            });
            return;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in content.EnumerateArray())
        {
            var blockType = GetStringProp(block, "type");

            switch (blockType)
            {
                case "thinking":
                    var thinking = GetStringProp(block, "thinking");
                    if (!string.IsNullOrEmpty(thinking))
                    {
                        messages.Add(new SessionMessage
                        {
                            Type = "thinking",
                            Timestamp = timestamp,
                            Text = thinking,
                        });
                    }

                    break;

                case "text":
                    var text = GetStringProp(block, "text");
                    if (!string.IsNullOrEmpty(text))
                    {
                        messages.Add(new SessionMessage
                        {
                            Type = type == "user" ? "user" : "text",
                            Timestamp = timestamp,
                            Text = text,
                        });
                    }

                    break;

                case "tool_use":
                    messages.Add(new SessionMessage
                    {
                        Type = "tool_call",
                        Timestamp = timestamp,
                        ToolName = GetStringProp(block, "name"),
                        ToolInput = block.TryGetProperty("input", out var input)
                            ? TruncateJson(input)
                            : string.Empty,
                    });
                    break;

                case "tool_result":
                    var resultContent = string.Empty;
                    if (block.TryGetProperty("content", out var rc))
                    {
                        resultContent = rc.ValueKind == JsonValueKind.String
                            ? rc.GetString() ?? string.Empty
                            : rc.ToString();
                    }

                    messages.Add(new SessionMessage
                    {
                        Type = "tool_result",
                        Timestamp = timestamp,
                        Text = Truncate(resultContent, 2000),
                    });
                    break;
            }
        }
    }

    private void ParseSubagentFile(string filePath, List<SessionMessage> messages)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = GetStringProp(root, "type");
                var timestamp = GetTimestamp(root);

                if (type == "user" || type == "assistant")
                {
                    ParseMessageBlocks(root, type, timestamp, messages);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse subagent file: {File}", filePath);
        }
    }

    private static string GetStringProp(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static DateTimeOffset GetTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out var ts))
        {
            if (ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), out var date))
            {
                return date;
            }
        }

        return DateTimeOffset.UtcNow;
    }

    private static string Truncate(string s, int maxLength)
    {
        if (s.Length <= maxLength)
        {
            return s;
        }

        return s[..maxLength] + "...";
    }

    private static string TruncateJson(JsonElement el)
    {
        var json = el.ToString();
        return Truncate(json ?? string.Empty, 1000);
    }
}
