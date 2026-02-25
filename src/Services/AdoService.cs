using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeAdoCompanion.Models;
using Microsoft.Extensions.Options;

namespace ClaudeAdoCompanion.Services;

public class AdoService : IAdoService
{
    private readonly HttpClient _httpClient;
    private readonly DashboardSettings _settings;
    private readonly AzCliTokenService _tokenService;
    private readonly ILogger<AdoService> _logger;
    private readonly string _repoRoot;

    private static readonly string[] WorkItemFields =
    [
        "System.Id",
        "System.Title",
        "System.State",
        "Microsoft.VSTS.Common.Severity",
        "Microsoft.VSTS.Common.Priority",
        "System.AssignedTo",
        "System.Tags",
        "System.CreatedDate",
        "System.ChangedDate",
    ];

    public AdoService(HttpClient httpClient, IOptions<DashboardSettings> settings, AzCliTokenService tokenService, ILogger<AdoService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _tokenService = tokenService;
        _logger = logger;

        // Walk up from the binary to find the repo root (contains .git)
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        _repoRoot = dir ?? Directory.GetCurrentDirectory();
    }

    public async Task<List<TriagedBug>> GetTriagedBugsAsync()
    {
        await ConfigureAuthAsync();

        // 1. WIQL query for all bugs in area path
        var iterationClause = !string.IsNullOrWhiteSpace(_settings.IterationPath)
            ? $" AND [System.IterationPath] UNDER '{_settings.IterationPath}'"
            : "";
        var wiql = $@"SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Bug' AND [System.AreaPath] UNDER '{_settings.AreaPath}'{iterationClause} AND [System.State] <> 'Closed' ORDER BY [System.ChangedDate] DESC";

        _logger.LogInformation("Querying ADO for bugs under area path: {AreaPath}", _settings.AreaPath);
        _logger.LogDebug("WIQL query: {Query}", wiql);

        var wiqlBody = JsonSerializer.Serialize(new { query = wiql });
        var wiqlRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{_settings.AdoOrg}/{_settings.AdoProject}/_apis/wit/wiql?api-version=7.1&$top={_settings.MaxBugsDefault}");
        wiqlRequest.Content = new StringContent(wiqlBody, Encoding.UTF8, "application/json");

        var wiqlResponse = await _httpClient.SendAsync(wiqlRequest);
        wiqlResponse.EnsureSuccessStatusCode();

        var wiqlResult = await JsonSerializer.DeserializeAsync<WiqlResponse>(
            await wiqlResponse.Content.ReadAsStreamAsync());

        if (wiqlResult?.WorkItems == null || wiqlResult.WorkItems.Count == 0)
        {
            _logger.LogWarning("WIQL returned 0 work items for area path: {AreaPath}. Query: {Query}", _settings.AreaPath, wiql);
            return [];
        }

        _logger.LogInformation("WIQL returned {Count} work items", wiqlResult.WorkItems.Count);

        // 2. Batch fetch work item fields (tags are parsed inline by MapWorkItem)
        var ids = wiqlResult.WorkItems.Select(w => w.Id).ToList();
        return await BatchFetchWorkItemsAsync(ids);
    }

    public async Task<TriagedBug?> GetBugAsync(int id)
    {
        await ConfigureAuthAsync();

        var response = await _httpClient.GetAsync(
            $"{_settings.AdoOrg}/{_settings.AdoProject}/_apis/wit/workitems/{id}?fields={string.Join(",", WorkItemFields)}&api-version=7.1");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var workItem = await JsonSerializer.DeserializeAsync<WorkItemResponse>(
            await response.Content.ReadAsStreamAsync());

        if (workItem == null)
        {
            return null;
        }

        return MapWorkItem(workItem);
    }

    public async Task AssignToCopilotAsync(int id)
    {
        await ConfigureAuthAsync();

        // Build patch operations: assign to Copilot + link main branch + add copilot-ready tag
        var currentBug = await GetBugAsync(id);
        var currentTags = currentBug?.Tags ?? string.Empty;
        var tagSet = new HashSet<string>(
            currentTags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        tagSet.Add("copilot-ready");
        var mergedTags = string.Join("; ", tagSet);

        var patchOps = new object[]
        {
            new { op = "add", path = "/fields/System.AssignedTo", value = _settings.CopilotUserId },
            new { op = "add", path = "/fields/System.Tags", value = mergedTags },
            new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "ArtifactLink",
                    url = $"vstfs:///Git/Ref/{_settings.RepoProjectGuid}/{_settings.RepoGuid}/{_settings.BranchRef}",
                    attributes = new { name = "Branch" },
                },
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Patch,
            $"{_settings.AdoOrg}/{_settings.AdoProject}/_apis/wit/workitems/{id}?api-version=7.1");
        request.Content = new StringContent(
            JsonSerializer.Serialize(patchOps), Encoding.UTF8, "application/json-patch+json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public Task RetriageAsync(int id)
    {
        RunClaudeCodeFireAndForget($"-p \"/triage-bug {id} --force\"");
        return Task.CompletedTask;
    }

    public Task BatchTriageAsync(int max)
    {
        RunClaudeCodeFireAndForget($"-p \"/triage-bugs --max={max}\"");
        return Task.CompletedTask;
    }

    private async Task<List<TriagedBug>> BatchFetchWorkItemsAsync(List<int> ids)
    {
        var allBugs = new List<TriagedBug>();

        foreach (var chunk in ids.Chunk(200))
        {
            var batchBody = JsonSerializer.Serialize(new WorkItemBatchRequest
            {
                Ids = chunk.ToList(),
                Fields = WorkItemFields.ToList(),
            });

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_settings.AdoOrg}/{_settings.AdoProject}/_apis/wit/workitemsbatch?api-version=7.1");
            request.Content = new StringContent(batchBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var batchResult = await JsonSerializer.DeserializeAsync<WorkItemBatchResponse>(
                await response.Content.ReadAsStreamAsync());

            if (batchResult?.Value != null)
            {
                allBugs.AddRange(batchResult.Value.Select(MapWorkItem));
            }
        }

        return allBugs;
    }

    private TriagedBug MapWorkItem(WorkItemResponse workItem)
    {
        var fields = workItem.Fields;
        var tags = GetStringField(fields, "System.Tags");

        return new TriagedBug
        {
            Id = workItem.Id,
            Title = GetStringField(fields, "System.Title"),
            State = GetStringField(fields, "System.State"),
            Severity = GetStringField(fields, "Microsoft.VSTS.Common.Severity"),
            Priority = GetIntField(fields, "Microsoft.VSTS.Common.Priority"),
            AssignedTo = GetAssignedTo(fields),
            Tags = tags,
            CreatedDate = GetDateField(fields, "System.CreatedDate"),
            ChangedDate = GetDateField(fields, "System.ChangedDate"),
            TriageStatus = TriageTagParser.ParseTags(tags),
            AdoUrl = $"{_settings.AdoOrg}/{_settings.AdoProject}/_workitems/edit/{workItem.Id}",
        };
    }

    private static string GetStringField(Dictionary<string, object?> fields, string key)
    {
        if (fields.TryGetValue(key, out var value) && value is JsonElement el && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static int GetIntField(Dictionary<string, object?> fields, string key)
    {
        if (fields.TryGetValue(key, out var value) && value is JsonElement el && el.ValueKind == JsonValueKind.Number)
        {
            return el.GetInt32();
        }

        return 0;
    }

    private static DateTimeOffset GetDateField(Dictionary<string, object?> fields, string key)
    {
        if (fields.TryGetValue(key, out var value) && value is JsonElement el && el.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(el.GetString(), out var date))
            {
                return date;
            }
        }

        return DateTimeOffset.MinValue;
    }

    private static string GetAssignedTo(Dictionary<string, object?> fields)
    {
        if (fields.TryGetValue("System.AssignedTo", out var value) && value is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("displayName", out var nameEl))
            {
                return nameEl.GetString() ?? string.Empty;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                return el.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private async Task ConfigureAuthAsync()
    {
        var token = await _tokenService.GetTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void RunClaudeCodeFireAndForget(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _repoRoot,
        };

        _logger.LogInformation("Starting Claude Code: claude {Arguments} in {WorkDir}", arguments, _repoRoot);

        var process = Process.Start(psi);
        if (process == null)
        {
            _logger.LogError("Failed to start Claude Code process for arguments: {Arguments}", arguments);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync();
                _logger.LogInformation("Claude Code process completed with exit code {ExitCode} for: {Arguments}",
                    process.ExitCode, arguments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for Claude Code process: {Arguments}", arguments);
            }
            finally
            {
                process.Dispose();
            }
        });
    }
}
