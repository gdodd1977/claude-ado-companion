using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClaudeAdoCompanion.Models;
using ClaudeAdoCompanion.Services;
using Microsoft.Extensions.FileProviders;

// For single-file publish, set content root to the exe's directory so appsettings.json is found
var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = exeDir,
});

builder.Configuration.AddJsonFile(
    Path.Combine(exeDir, "appsettings.local.json"), optional: true, reloadOnChange: true);

// Default to port 5200 if no URL is configured
builder.WebHost.UseUrls(
    builder.Configuration["Urls"] ?? "http://localhost:5200");

var isDemo = args.Contains("--demo");

builder.Services.Configure<DashboardSettings>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddSingleton<AzCliTokenService>();

if (isDemo)
{
    builder.Services.AddSingleton<IAdoService, DemoDataService>();
}
else
{
    builder.Services.AddHttpClient<IAdoService, AdoService>();
}

builder.Services.AddSingleton<ISessionService, SessionService>();

var app = builder.Build();

var embeddedProvider = new ManifestEmbeddedFileProvider(
    typeof(Program).Assembly, "wwwroot");

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = embeddedProvider,
});

// === User Endpoint ===

app.MapGet("/api/me", async (AzCliTokenService tokenService) =>
{
    if (isDemo)
    {
        return Results.Ok(new { displayName = "Demo User" });
    }

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c az account show --output json",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        if (process == null) return Results.Ok(new { displayName = "" });
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        using var doc = JsonDocument.Parse(output);
        var name = doc.RootElement.GetProperty("user").GetProperty("name").GetString() ?? "";
        return Results.Ok(new { displayName = name });
    }
    catch
    {
        return Results.Ok(new { displayName = "" });
    }
});

// === Config Endpoint ===

app.MapGet("/api/config", (Microsoft.Extensions.Options.IOptions<DashboardSettings> settings) =>
{
    var s = settings.Value;
    return Results.Ok(new
    {
        adoOrg = s.AdoOrg,
        adoProject = s.AdoProject,
        areaPath = s.AreaPath,
        iterationPath = s.IterationPath ?? "",
        copilotUserId = s.CopilotUserId,
        repoProjectGuid = s.RepoProjectGuid,
        repoGuid = s.RepoGuid,
        branchRef = s.BranchRef,
        triagePipelineName = s.TriagePipelineName,
        maxBugsDefault = s.MaxBugsDefault,
        isConfigured = s.IsConfigured,
        demo = isDemo,
    });
});

app.MapPost("/api/config", async (HttpContext ctx) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
    if (body == null) return Results.BadRequest("Invalid request body");

    // Read existing local config or start fresh
    var localConfigPath = Path.Combine(exeDir, "appsettings.local.json");
    Dictionary<string, object?> root;
    if (File.Exists(localConfigPath))
    {
        var existing = await File.ReadAllTextAsync(localConfigPath);
        root = JsonSerializer.Deserialize<Dictionary<string, object?>>(existing) ?? new();
    }
    else
    {
        root = new();
    }

    // Build the Dashboard section from the incoming values
    var dashboard = new Dictionary<string, object?>();
    foreach (var (key, value) in body)
    {
        dashboard[key] = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt32(out var i) ? i : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value.ToString(),
        };
    }

    root["Dashboard"] = dashboard;

    var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(localConfigPath, json);

    return Results.Ok(new { success = true, message = "Settings saved. Restart the app to apply changes." });
});

// === Bug Endpoints ===

app.MapGet("/api/bugs", async (IAdoService adoService) =>
{
    var bugs = await adoService.GetTriagedBugsAsync();
    return Results.Ok(bugs);
});

app.MapGet("/api/bugs/{id:int}", async (int id, IAdoService adoService) =>
{
    var bug = await adoService.GetBugAsync(id);
    return bug == null ? Results.NotFound() : Results.Ok(bug);
});

app.MapPost("/api/bugs/{id:int}/assign-copilot", async (int id, IAdoService adoService) =>
{
    await adoService.AssignToCopilotAsync(id);
    return Results.Ok(new { success = true, message = $"Bug {id} assigned to Copilot" });
});

app.MapPost("/api/bugs/{id:int}/retriage", async (int id, IAdoService adoService) =>
{
    await adoService.RetriageAsync(id);
    return Results.Ok(new { success = true, message = $"Triage started for bug {id}", demo = isDemo });
});

app.MapPost("/api/triage/batch", async (HttpContext ctx, IAdoService adoService) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<BatchTriageRequest>();
    var max = body?.Max ?? 10;
    await adoService.BatchTriageAsync(max);
    return Results.Ok(new { success = true, message = "Batch triage started", demo = isDemo });
});

// === Claude Auth Endpoints ===

var claudeAuthVerified = false;

app.MapGet("/api/claude/status", async () =>
{
    if (isDemo)
    {
        return Results.Ok(new { installed = true, authenticated = true });
    }

    if (claudeAuthVerified)
    {
        return Results.Ok(new { installed = true, authenticated = true });
    }

    // Check if Claude CLI is installed and authenticated by running a quick command
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = "-p \"respond with just the word ok\" --max-turns 1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return Results.Ok(new { installed = false, authenticated = false });
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            claudeAuthVerified = true;
            return Results.Ok(new { installed = true, authenticated = true });
        }

        return Results.Ok(new { installed = true, authenticated = false });
    }
    catch (System.ComponentModel.Win32Exception)
    {
        // claude not found on PATH
        return Results.Ok(new { installed = false, authenticated = false });
    }
    catch
    {
        return Results.Ok(new { installed = true, authenticated = false });
    }
});

app.MapPost("/api/claude/launch-auth", () =>
{
    if (isDemo)
    {
        return Results.Ok(new { success = true, message = "Demo mode — no auth needed" });
    }

    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/k claude",
            UseShellExecute = true,
            CreateNoWindow = false,
        });

        return Results.Ok(new { success = true, message = "Authentication terminal opened" });
    }
    catch
    {
        return Results.Ok(new { success = false, message = "Failed to open terminal. Please run 'claude' manually in a terminal to authenticate." });
    }
});

// === Session Endpoints ===

app.MapGet("/api/sessions", async (ISessionService sessionService, int? max, bool? triageOnly) =>
{
    var sessions = await sessionService.ListSessionsAsync(max ?? 20, triageOnly ?? false);
    return Results.Ok(sessions);
});

app.MapGet("/api/sessions/active", (ISessionService sessionService) =>
{
    var id = sessionService.GetActiveSessionId();
    return id == null
        ? Results.Json(new { active = false })
        : Results.Json(new { active = true, id });
});

app.MapGet("/api/sessions/{id}", async (string id, ISessionService sessionService) =>
{
    var messages = await sessionService.GetSessionAsync(id);
    return Results.Ok(messages);
});

// SSE streaming endpoint — tails the JSONL file in real time
app.MapGet("/api/sessions/{id}/stream", async (string id, HttpContext ctx, ISessionService sessionService) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var cancellationToken = ctx.RequestAborted;

    await foreach (var msg in sessionService.StreamSessionAsync(id, cancellationToken))
    {
        var json = JsonSerializer.Serialize(msg, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await ctx.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await ctx.Response.Body.FlushAsync(cancellationToken);
    }
});

// Fallback: serve embedded index.html for SPA-style routing
app.MapFallback(async context =>
{
    var file = embeddedProvider.GetFileInfo("index.html");
    if (file.Exists)
    {
        context.Response.ContentType = "text/html";
        using var stream = file.CreateReadStream();
        await stream.CopyToAsync(context.Response.Body);
    }
    else
    {
        context.Response.StatusCode = 404;
    }
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = "http://localhost:5200";
    Console.WriteLine($"Claude ADO Companion running at {url}");
    if (isDemo)
    {
        Console.WriteLine("  [DEMO MODE] Using mock data — no ADO connection required");
    }
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch
    {
        // Browser launch failed — user can open manually
    }
});

app.Run();

record BatchTriageRequest(int? Max);
