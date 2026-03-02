# Claude ADO Companion — Technical Spec

## Overview

A self-contained ASP.NET 8 web app that serves a bug triage dashboard for any Azure DevOps team. It connects to ADO to display bugs, runs Claude Code skills for AI-powered triage, and streams triage session output in real time via a floating panel.

Runs at `http://localhost:5200`. Ships as a single-file self-contained exe (`win-x64`). Has a `--demo` mode with mock data for UI preview without ADO access, and a `--debug` mode that logs exceptions to a temp file. On first run, presents a setup form for ADO connection details; settings are persisted to `appsettings.local.json` and reload without restart via `IOptionsMonitor<T>`.

## Architecture

```
install.ps1                        # Prerequisite checker + shortcut creator
.claude/
  skills/
    triage-bug/SKILL.md            # Single-bug triage skill (auto-detected by Claude)
    triage-bugs/SKILL.md           # Batch triage skill (auto-detected by Claude)
src/
  Program.cs                     # Minimal API host — all HTTP endpoints defined here
  Models/
    DashboardSettings.cs         # Config POCO bound from appsettings.json "Dashboard" section
    TriagedBug.cs                # Core bug model returned by /api/bugs
    TriageStatus.cs              # Tag-derived triage state
    AdoResponses.cs              # ADO REST API response DTOs (WIQL, batch, work item)
    SessionMessage.cs            # Single message in a Claude Code session
    SessionSummary.cs            # Lightweight session list entry (id, timestamp, preview)
  Services/
    IAdoService.cs               # Interface for bug operations + AssignCopilotResult record
    AdoService.cs                # Live implementation — calls ADO REST APIs via az CLI token
    DemoDataService.cs           # --demo implementation — returns hardcoded mock bugs
    TriageTagParser.cs           # Static parser: System.Tags string → TriageStatus
    AzCliTokenService.cs         # Gets/caches ADO bearer tokens via `az account get-access-token`
    ISessionService.cs           # Interface for triage panel session streaming
    SessionService.cs            # Reads JSONL files from auto-detected Claude projects path
  wwwroot/
    index.html                   # Single-page HTML shell (no tabs — bug table only)
    js/app.js                    # All client-side logic (fetch, render, filter, sort, triage panel)
    css/styles.css               # Full stylesheet
```

## Key Concepts

### Configuration & First-Run Setup

On first launch, `DashboardSettings.IsConfigured` returns `false` (all defaults are empty strings). The frontend detects this via `GET /api/config` and shows a setup form instead of the bug table. On submit, `POST /api/config` writes the user's settings to `appsettings.local.json`, which is loaded as an additional config source with `reloadOnChange: true`.

All services use `IOptionsMonitor<DashboardSettings>` so config changes take effect without restarting the app. The frontend reloads the page 1.5 seconds after save to allow the file watcher to pick up changes.

A settings gear icon in the header lets users reopen the form at any time with current values pre-filled. When bug loading fails (e.g., 401 from bad config), an inline "Open Settings" button appears in the error state.

### Triage Model (tag-based)

Triage skills apply tags directly to ADO work items. The companion reads triage status from `System.Tags` — no per-bug comment fetching needed.

**Known triage tags:** `triaged`, `needs-info`, `high-roi`, `copilot-ready`, `copilot-possible`, `human-required`

`TriageTagParser.ParseTags(string tags)` splits on `;`, trims, and produces:

```csharp
public class TriageStatus
{
    public bool IsTriaged { get; set; }
    public bool NeedsInfo { get; set; }
    public bool HighRoi { get; set; }
    public string CopilotReadiness { get; set; } // "Ready", "Possible", "Human Required", or ""
}
```

### Bug Lifecycle

1. User selects bugs via checkboxes and clicks "Run Claude Analysis (N)", or clicks per-bug "Re-triage"
2. Claude CLI analyzes the bug(s) and applies triage tags to the ADO work items
3. Dashboard fetches bugs via WIQL + batch work item API, parses tags inline in `MapWorkItem()`
4. User can "Assign to Copilot" — conditionally patches `System.AssignedTo`, adds `copilot-ready` tag, and links branch (only for configured values)

### Assign to Copilot

`AssignToCopilotAsync` builds a JSON Patch (`application/json-patch+json`) with only the operations that have configured values:

1. **Always**: adds `copilot-ready` to `System.Tags` (merged with existing tags)
2. **If `CopilotUserId` is set**: patches `System.AssignedTo` to the Copilot service account
3. **If `RepoProjectGuid` and `RepoGuid` are set**: adds an `ArtifactLink` relation for the branch

Returns an `AssignCopilotResult(bool Assigned, bool BranchLinked, string Message)` so the frontend can display exactly what happened.

### In-App Triage (fire-and-forget with live streaming)

Triage is triggered directly from the dashboard UI. The backend starts the Claude CLI process in the background and returns immediately. The frontend opens a **floating session panel** (bottom-right, non-blocking) that shows the Claude session logs in real time.

**Flow:**
1. User selects bugs via checkboxes and clicks "Run Claude Analysis (N)", or clicks per-bug "Re-triage" button
2. Frontend checks Claude CLI authentication via `GET /api/claude/status`
3. If not authenticated, `POST /api/claude/launch-auth` opens a terminal window for interactive Claude login; frontend polls until auth succeeds
4. Once auth'd (cached for the session), the triage endpoint fires the Claude CLI process (fire-and-forget)
5. The triage panel opens and polls `GET /api/sessions/active` until the new session appears
6. Panel connects to `GET /api/sessions/{id}/stream` (SSE) and renders messages in real time
7. When the SSE stream closes (Claude finishes), a "Triage complete!" toast fires and the bug list auto-refreshes
8. The panel can be minimized (collapse to header bar) or closed independently; the main bug grid remains fully interactive while it's open

In `--demo` mode, the auth check is bypassed and the panel shows a demo placeholder message.

### ADO API Flow (AdoService)

1. **WIQL query** — `POST _apis/wit/wiql` — gets bug IDs in the configured area path (non-Closed, ordered by ChangedDate DESC, capped at `MaxBugsDefault`). Optionally filters by `IterationPath` if configured.
2. **Batch fetch** — `POST _apis/wit/workitemsbatch` — fetches fields in chunks of 200
3. **MapWorkItem** — maps ADO fields to `TriagedBug`, calls `TriageTagParser.ParseTags()` on `System.Tags`

No N+1 comment fetching. Total API calls: 1 WIQL + ceil(N/200) batch requests.

Logging: the area path is logged at `Information` on every query. If WIQL returns 0 results, a `Warning` is logged with the full query text — useful for diagnosing area path mismatches.

### Authentication

**ADO:** `AzCliTokenService` shells out to `az account get-access-token --resource 499b84ac-...` (ADO resource ID). Caches the token until 5 minutes before expiry. Thread-safe via `SemaphoreSlim`. Requires `az login` before running (except `--demo` mode).

**Claude CLI:** On first triage action per session, the app checks if Claude CLI is installed and authenticated by running a quick test command. If not authenticated, it opens a terminal window (`cmd /k claude`) for the user to complete interactive login. The auth result is cached for the lifetime of the app process. In `--demo` mode, auth is always reported as successful.

**Triage skills** (`.claude/skills/triage-bug/SKILL.md`, `.claude/skills/triage-bugs/SKILL.md`) use `az boards` CLI commands exclusively — no MCP server dependency. Skills are auto-detected by Claude based on their descriptions (e.g., when the user asks to assess or triage a bug) and can also be invoked manually via `/triage-bug` or `/triage-bugs`. The skills read ADO connection details from `appsettings.local.json` (falling back to `appsettings.json`) and use those values for all `az boards` commands.

### Session Path Auto-Detection

`SessionService` reads Claude Code JSONL files for the triage panel. The session path is resolved in order:

1. **Explicit config** — if `ClaudeProjectsPath` is set and exists, use it
2. **Repo root match** — walk up from the exe to find `.git`, encode the path Claude Code style (`C:\Repos\foo` → `C--Repos-foo`), check under `~/.claude/projects/`
3. **Most recent fallback** — scan all directories under `~/.claude/projects/`, pick the one with the most recently modified `.jsonl` file

This means the app works whether run from inside the repo or from a standalone download location.

### Debug Mode

When launched with `--debug`:
- Logging minimum level set to `Debug`
- Global exception-handling middleware logs every unhandled exception (method, path, exception type, message, stack trace, inner exception) to `%TEMP%\claude-ado-companion-debug.log`
- `GET /api/debug/log` endpoint serves the log file as plain text (only registered in debug mode)
- Log file path printed to console on startup

### Port Conflict Handling

`app.Run()` is wrapped in a try/catch for `IOException` with `SocketException` inner. On port-in-use, displays a friendly error message with remediation steps and waits for a keypress before exiting.

## HTTP Endpoints (Program.cs)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/me` | Current user display name (from `az account show`) |
| GET | `/api/config` | Config dump including `isConfigured` flag, all Dashboard settings, and demo flag |
| POST | `/api/config` | Save settings to `appsettings.local.json` |
| GET | `/api/claude/status` | Check if Claude CLI is installed and authenticated (cached) |
| POST | `/api/claude/launch-auth` | Open a terminal window for interactive Claude login |
| GET | `/api/bugs` | All open bugs with triage status |
| GET | `/api/bugs/{id}` | Single bug by ID |
| POST | `/api/bugs/{id}/assign-copilot` | Tag + conditionally assign to Copilot + link branch; returns `AssignCopilotResult` |
| POST | `/api/bugs/{id}/retriage` | Fire-and-forget: start `claude -p "/triage-bug {id} --force"` |
| POST | `/api/triage/batch` | Fire-and-forget: start `claude -p "/triage-bugs --max={N}"` (kept for backward compat with skills) |
| POST | `/api/triage/selected` | Fire-and-forget: triage specific bug IDs. Body: `{ bugIds: [int] }` |
| GET | `/api/sessions/active` | Most recently modified session (within 5 min) |
| GET | `/api/sessions/{id}/stream` | SSE stream of session messages |
| GET | `/api/debug/log` | Debug log contents (only in `--debug` mode) |

## Frontend (app.js)

### First-Run Flow
On load, fetches `GET /api/config`. If `isConfigured` is `false` and not in demo mode, shows the setup banner (modal form) instead of the bug table. On submit, POSTs settings and reloads the page after 1.5 seconds.

### Error State
When bug loading fails, the table shows the error message with a contextual hint (e.g., "This usually means your ADO connection details are wrong or your `az login` has expired") and an inline "Open Settings" button.

### Table Columns
☐ | ID | Title | Sev | ROI | Copilot | Actions

- **☐** — Checkbox column (40px, center-aligned). Header has a master "select all" checkbox; each row has an individual checkbox. Selection state is maintained in a `Set` and survives filter/sort changes. The master checkbox shows indeterminate state when some (but not all) visible bugs are selected.
- **Sev, ROI, Copilot, Actions** columns are center-aligned
- **ROI** — "High" (green badge) if `triageStatus.highRoi`, otherwise "—"
- **Copilot** — "Ready" (green) / "Possible" (amber) / "Human Required" (red) / "—"
- **Needs Info** — small amber badge rendered inline after the title text
- **Actions** — Assign to Copilot button (shown for Ready/Possible), Re-triage button (always)

### Filters
All | Triaged | Untriaged | Copilot Ready | Copilot Possible | Human Required

Plus an "Assigned to Me" toggle that layers on top.

### Sorting
- **ROI** — boolean sort (high-roi first in desc)
- **Copilot** — categorical order: Ready (3) > Possible (2) > Human Required (1) > none (0)
- **ID, Title, Severity** — standard sorts

### Triage Panel
A floating panel docked to the bottom-right of the screen (`position: fixed; bottom: 0; right: 24px`). Non-blocking — the bug grid remains fully interactive while it's open. Shows:
- Header with title ("Claude Analysis"), live status text, minimize (–) button, and close (×) button
- Scrollable message body (`overscroll-behavior: contain`) that renders streamed session messages
- Polls for active session, connects via SSE, auto-scrolls to latest message
- When SSE stream closes (triage complete), shows a success toast and auto-refreshes the bug list
- Minimize collapses the panel to just its header bar; click minimize again to restore
- Close stops the stream and refreshes bugs

## Configuration (DashboardSettings)

All settings default to empty strings. On first run, the setup UI collects values and writes them to `appsettings.local.json` (gitignored). The app loads config from `appsettings.json` (defaults) → `appsettings.local.json` (user overrides) with `reloadOnChange: true`.

| Key | Default | Description |
|-----|---------|-------------|
| `AdoOrg` | `""` | ADO organization URL (e.g., `https://dev.azure.com/myorg`) |
| `AdoProject` | `""` | ADO project name |
| `AreaPath` | `""` | Bug area path filter |
| `IterationPath` | `null` | Optional sprint/iteration path filter |
| `CopilotUserId` | `""` | Full unique name for the Copilot service account (`guid@tenant`) |
| `RepoProjectGuid` | `""` | ADO project GUID for branch linking |
| `RepoGuid` | `""` | ADO repo GUID for branch linking |
| `BranchRef` | `GBmain` | Branch ref for Copilot assignment linking (`GB` prefix + branch name) |
| `TriagePipelineName` | `""` | Pipeline name (reserved for future use) |
| `MaxBugsDefault` | `100` | Max bugs returned by WIQL |
| `ClaudeProjectsPath` | `""` | Path to Claude Code session JSONL files (auto-detected if empty) |

`IsConfigured` returns `true` when `AdoOrg`, `AdoProject`, and `AreaPath` are all non-empty.

## Install Script (install.ps1)

Checks prerequisites (Azure CLI, Azure login, Claude CLI) and reports status. If the exe is present in the same directory, offers to create shortcuts:

- **Desktop shortcut** — `.lnk` file on the user's desktop
- **Start Menu shortcut** — `.lnk` file in the user's Programs folder

Shortcuts launch via `cmd /k` so the console window stays open for output and error visibility. Working directory is set to the script's directory for session auto-detection.

## NuGet Dependencies

- `Microsoft.Extensions.FileProviders.Embedded` — serves wwwroot files embedded in the assembly
- `System.Text.Json` — JSON serialization

## Build & Run

```powershell
cd src
dotnet build              # Build
dotnet run                # Run (requires az login + claude auth)
dotnet run -- --demo      # Run with mock data
dotnet run -- --debug     # Run with exception logging
```

## Publish

```powershell
dotnet publish src/ClaudeAdoCompanion.csproj -c Release
# Output: src/bin/Release/net8.0/win-x64/publish/ClaudeAdoCompanion.exe
```

Single-file, self-contained, win-x64. wwwroot files are embedded in the assembly via `<EmbeddedResource>`.
