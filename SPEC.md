# Claude ADO Companion — Technical Spec

## Overview

A self-contained ASP.NET 8 web app that serves a bug triage dashboard and Claude Code session viewer for the Copilot Flows team. It connects to Azure DevOps (ADO) to display bugs, and reads local Claude Code JSONL session files for the session viewer.

Runs at `http://localhost:5200`. Ships as a single-file self-contained exe (`win-x64`). Has a `--demo` mode with mock data for UI preview without ADO access.

## Architecture

```
install.ps1                        # Prerequisite checker (az CLI, az login, Claude CLI)
src/
  Program.cs                     # Minimal API host — all HTTP endpoints defined here
  Models/
    DashboardSettings.cs         # Config POCO bound from appsettings.json "Dashboard" section
    TriagedBug.cs                # Core bug model returned by /api/bugs
    TriageStatus.cs              # Tag-derived triage state (replaces old numeric TriageScores)
    AdoResponses.cs              # ADO REST API response DTOs (WIQL, batch, work item)
    SessionMessage.cs            # Single message in a Claude Code session
    SessionSummary.cs            # Lightweight session list entry (id, timestamp, preview)
  Services/
    IAdoService.cs               # Interface for bug operations (live + demo implementations)
    AdoService.cs                # Live implementation — calls ADO REST APIs via az CLI token
    DemoDataService.cs           # --demo implementation — returns hardcoded mock bugs
    TriageTagParser.cs           # Static parser: System.Tags string → TriageStatus
    AzCliTokenService.cs         # Gets/caches ADO bearer tokens via `az account get-access-token`
    ISessionService.cs           # Interface for Claude Code session browsing
    SessionService.cs            # Reads JSONL files from ~/.claude/projects/<project>/
  wwwroot/
    index.html                   # Single-page HTML shell
    js/app.js                    # All client-side logic (fetch, render, filter, sort, SSE)
    css/styles.css               # Full stylesheet
```

## Key Concepts

### Triage Model (tag-based)

The CopilotFlow pipeline applies tags directly to ADO work items. The companion reads triage status from `System.Tags` — no per-bug comment fetching needed.

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

1. User triggers "Run Claude Analysis" or "Re-triage" from the dashboard (or pipeline runs externally)
2. Claude CLI analyzes the bug and applies triage tags to the ADO work item
3. Dashboard fetches bugs via WIQL + batch work item API, parses tags inline in `MapWorkItem()`
4. User can "Assign to Copilot" — patches `System.AssignedTo` to the Copilot user ID, adds `copilot-ready` tag, links the main branch

### In-App Triage (fire-and-forget with live streaming)

Triage is triggered directly from the dashboard UI. The backend starts the Claude CLI process in the background and returns immediately. The frontend opens a **floating session panel** (bottom-right, non-blocking) that shows the Claude session logs in real time.

**Flow:**
1. User clicks "Run Claude Analysis" or per-bug "Re-triage" button
2. Frontend checks Claude CLI authentication via `GET /api/claude/status`
3. If not authenticated, `POST /api/claude/launch-auth` opens a terminal window for interactive Claude login; frontend polls until auth succeeds
4. Once auth'd (cached for the session), the triage endpoint fires the Claude CLI process (fire-and-forget)
5. The triage panel opens and polls `GET /api/sessions/active` until the new session appears
6. Panel connects to `GET /api/sessions/{id}/stream` (SSE) and renders messages in real time
7. When the SSE stream closes (Claude finishes), a "Triage complete!" toast fires and the bug list auto-refreshes
8. The panel can be minimized (collapse to header bar) or closed independently; the main bug grid remains fully interactive while it's open

In `--demo` mode, the auth check is bypassed and the panel shows a demo placeholder message.

### ADO API Flow (AdoService)

1. **WIQL query** — `POST _apis/wit/wiql` — gets bug IDs in the configured area path (non-Closed, ordered by ChangedDate DESC, capped at `MaxBugsDefault`)
2. **Batch fetch** — `POST _apis/wit/workitemsbatch` — fetches fields in chunks of 200
3. **MapWorkItem** — maps ADO fields to `TriagedBug`, calls `TriageTagParser.ParseTags()` on `System.Tags`

No N+1 comment fetching. Total API calls: 1 WIQL + ceil(N/200) batch requests.

Logging: the area path is logged at `Information` on every query. If WIQL returns 0 results, a `Warning` is logged with the full query text — useful for diagnosing area path mismatches.

### Authentication

**ADO:** `AzCliTokenService` shells out to `az account get-access-token --resource 499b84ac-...` (ADO resource ID). Caches the token until 5 minutes before expiry. Thread-safe via `SemaphoreSlim`. Requires `az login` before running (except `--demo` mode).

**Claude CLI:** On first triage action per session, the app checks if Claude CLI is installed and authenticated by running a quick test command. If not authenticated, it opens a terminal window (`cmd /k claude`) for the user to complete interactive login. The auth result is cached for the lifetime of the app process. In `--demo` mode, auth is always reported as successful.

**Triage skills** (`.claude/commands/triage-bug.md`, `triage-bugs.md`) use `az boards` CLI commands exclusively — no MCP server dependency. The triage skills fetch work items via `az boards work-item show` and update tags via `az boards work-item update`.

### Session Viewer

`SessionService` reads Claude Code JSONL files from the configured `ClaudeProjectsPath`. Supports:
- **List sessions** — scans `.jsonl` files by last-modified, optionally filtering to triage sessions
- **Load session** — parses all messages (user, assistant text, thinking, tool_call, tool_result) including subagent files
- **Live streaming** — SSE endpoint that polls the JSONL file every 500ms for new content

## HTTP Endpoints (Program.cs)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/me` | Current user display name (from `az account show`) |
| GET | `/api/config` | Read-only config dump (AdoOrg, AdoProject, AreaPath, MaxBugsDefault, ClaudeWorkingDirectory, demo flag) |
| GET | `/api/claude/status` | Check if Claude CLI is installed and authenticated (cached) |
| POST | `/api/claude/launch-auth` | Open a terminal window for interactive Claude login |
| GET | `/api/bugs` | All open bugs with triage status |
| GET | `/api/bugs/{id}` | Single bug by ID |
| POST | `/api/bugs/{id}/assign-copilot` | Assign bug to Copilot user + link branch + add tag |
| POST | `/api/bugs/{id}/retriage` | Fire-and-forget: start `claude -p "/triage-bug {id} --force"` |
| POST | `/api/triage/batch` | Fire-and-forget: start `claude -p "/triage-bugs --max={N}"` |
| GET | `/api/sessions` | List recent sessions (optional `?max=` and `?triageOnly=`) |
| GET | `/api/sessions/active` | Most recently modified session (within 5 min) |
| GET | `/api/sessions/{id}` | All messages in a session |
| GET | `/api/sessions/{id}/stream` | SSE stream of session messages |

## Frontend (app.js)

### Table Columns
ID | Title | Sev | ROI | Copilot | Actions

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
- Scrollable message body (`overscroll-behavior: contain`) that renders streamed session messages using the same `renderMessage()` function as the sessions tab
- Polls for active session, connects via SSE, auto-scrolls to latest message
- When SSE stream closes (triage complete), shows a success toast and auto-refreshes the bug list
- Minimize collapses the panel to just its header bar; click minimize again (□ → –) to restore
- Close stops the stream and refreshes bugs

## Configuration (appsettings.json → DashboardSettings)

| Key | Default | Description |
|-----|---------|-------------|
| `AdoOrg` | `https://msazure.visualstudio.com` | ADO organization URL |
| `AdoProject` | `One` | ADO project name |
| `AreaPath` | `One\Azure\Flow\DAX\AI First Experiences` | Bug area path filter |
| `CopilotUserId` | (GUID) | AAD object ID for the Copilot service account |
| `RepoProjectGuid` | (GUID) | ADO project GUID for branch linking |
| `RepoGuid` | (GUID) | ADO repo GUID for branch linking |
| `BranchRef` | `GBmain` | Branch ref for Copilot assignment linking |
| `MaxBugsDefault` | `100` | Max bugs returned by WIQL |
| `ClaudeWorkingDirectory` | `C:\Repos\CopilotFlow` | Working directory for Claude CLI (where triage skills live) |
| `ClaudeProjectsPath` | `~/.claude/projects/C--Repos-CopilotFlow` | Path to Claude Code session JSONL files |

## NuGet Dependencies

- `Microsoft.Extensions.FileProviders.Embedded` — serves wwwroot files embedded in the assembly
- `System.Text.Json` — JSON serialization

## Build & Run

```powershell
cd src
dotnet build              # Build
dotnet run                # Run (requires az login + claude auth)
dotnet run -- --demo      # Run with mock data
```

## Publish

```powershell
dotnet publish src/ClaudeAdoCompanion.csproj -c Release
# Output: src/bin/Release/net8.0/win-x64/publish/ClaudeAdoCompanion.exe
```

Single-file, self-contained, win-x64. wwwroot files are embedded in the assembly via `<EmbeddedResource>`.
