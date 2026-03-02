# Claude ADO Companion

A generic Azure DevOps bug triage companion powered by Claude Code. Connect it to any ADO project to:

- **Bug Triage Dashboard** — View, sort, filter, and triage ADO bugs with AI-generated scores
- **Claude Code Triage Skills** — `/triage-bug` and `/triage-bugs` skills that assess actionability, ROI, and Copilot agent readiness
- **Copilot Assignment** — One-click assign Copilot-ready bugs to GitHub Copilot coding agent with branch linking

## Table of Contents

- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
  - [Option A: Download a release](#option-a-download-a-release-recommended)
  - [Option B: Build from source](#option-b-build-from-source)
  - [First-run setup](#first-run-setup)
    - [Required settings](#required-settings)
    - [Optional settings (for Copilot assignment)](#optional-settings-for-copilot-assignment)
    - [Finding the Copilot User ID](#finding-the-copilot-user-id)
    - [Finding the Repo GUIDs](#finding-the-repo-guids)
- [Usage Guide](#usage-guide)
  - [The bug table](#the-bug-table)
  - [Filtering bugs](#filtering-bugs)
  - [Running triage](#running-triage)
  - [Understanding triage scores](#understanding-triage-scores)
  - [Assigning to Copilot](#assigning-to-copilot)
  - [Suggested workflow](#suggested-workflow)
- [Updating](#updating)
- [Demo Mode](#demo-mode)
- [Debug Mode](#debug-mode)
- [Triage Skills](#triage-skills)
- [Development](#development)

## Prerequisites

| Prerequisite | Required for | Install |
|---|---|---|
| [GitHub CLI](https://cli.github.com/) | Downloading releases | `winget install --id GitHub.cli -e`, then `gh auth login` |
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli-windows) | Dashboard + triage | `winget install --id Microsoft.AzureCLI -e` |
| Azure login | Dashboard + triage | `az login` (needs access to your ADO organization) |
| [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code/overview) | Triage skills | `npm install -g @anthropic-ai/claude-code` (requires Node.js 18+) |

Run the prerequisite checker to verify your setup:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

It checks each prerequisite and prints what's ready vs what needs attention. It does **not** install anything automatically.

## Quick Start

### Option A: Download a release (recommended)

For best results, download the release **into the repo directory** so the triage panel can auto-detect Claude Code session logs. If run from elsewhere, it falls back to the most recently active Claude project.

```powershell
# Clone the repo (needed for Claude Code skills)
git clone https://github.com/gdodd1977/claude-ado-companion.git
cd claude-ado-companion

# Download the latest release assets into the repo
gh release download --repo gdodd1977/claude-ado-companion --pattern "*" --dir .
```

Then run the exe directly:

```powershell
.\ClaudeAdoCompanion.exe
```

### Option B: Build from source

Requires [.NET 9 SDK](https://dot.net/download).

```powershell
cd src
dotnet run
```

### First-run setup

On first launch the app opens a **setup form** prompting for your ADO connection details. Click the gear icon in the header to change settings at any time.

#### Required settings

| Field | Example | Where to find it |
|-------|---------|-------------------|
| ADO Organization URL | `https://dev.azure.com/myorg` | Your browser URL when viewing any ADO page |
| Project | `MyProject` | The project name in the ADO URL after the org |
| Area Path | `MyProject\Team\Component` | ADO > Project Settings > Boards > Team configuration > Areas |

#### Optional settings (for Copilot assignment)

These are only needed if you want the "Assign to Copilot" button to work. Without them, the button will tag the bug as `copilot-ready` but skip the actual assignment.

| Field | Example | Where to find it |
|-------|---------|-------------------|
| Copilot User ID | `66dda6c5-...@72f988bf-...` | See [Finding the Copilot User ID](#finding-the-copilot-user-id) below |
| Repo Project GUID | `b32aa71e-8ed2-41b2-...` | See [Finding the Repo GUIDs](#finding-the-repo-guids) below |
| Repo GUID | `a1b2c3d4-...` | Same as above |
| Branch Ref | `GBmain` | Default works for most repos (`GB` prefix + branch name) |
| Sprint / Iteration Path | `MyProject\Sprint 42` | ADO > Project Settings > Boards > Team configuration > Iterations |
| Max Bugs | `100` | Maximum bugs to load in the dashboard |

#### Finding the Copilot User ID

The Copilot User ID is the Azure AD identity of the GitHub Copilot service account in your ADO org. To find it:

1. Find a work item that is already assigned to GitHub Copilot in your project
2. Run the following command (replace the work item ID and org):
   ```powershell
   az boards work-item show --id <work-item-id> --org <your-org-url> --project <your-project> -o json
   ```
3. In the output, look for `System.AssignedTo.uniqueName` — it will look like `66dda6c5-07d0-4484-9979-116241219397@72f988bf-86f1-41af-91ab-2d7cd011db47`
4. Paste that full value into the **Copilot User ID** field

If you don't have Copilot set up in your ADO org yet, leave this blank — you can add it later via the settings gear.

#### Finding the Repo GUIDs

The Repo Project GUID and Repo GUID are used to link a branch to the work item when assigning to Copilot. To find them:

1. Run the following to list repos in your project:
   ```powershell
   az repos list --org <your-org-url> --project <your-project> -o json --query "[].{name:name, id:id, project:project.id}"
   ```
2. Find your repo in the list:
   - **Repo GUID** = the `id` field
   - **Repo Project GUID** = the `project` field

Settings are saved to `appsettings.local.json` (gitignored).

### Open the dashboard

Navigate to **http://localhost:5200** in your browser.

## Usage Guide

### The bug table

The dashboard loads all non-closed bugs from your configured area path. Each row shows:

| Column | What it means |
|--------|---------------|
| **ID** | Links directly to the bug in ADO |
| **Title** | Bug title. An amber **Needs Info** badge appears if the bug lacks enough detail to act on |
| **Sev** | Severity (1 = critical, 4 = low) |
| **ROI** | **High** (green) if the bug scores 70+ on the ROI assessment, otherwise **—** |
| **Copilot** | **Ready** (green) = assignable to Copilot now. **Possible** (amber) = could work with more detail. **Human Required** (red) = needs human judgment. **—** = not yet triaged |
| **Actions** | Assign to Copilot button (for Ready/Possible bugs) and Re-triage button |

Click any column header to sort. Click again to reverse direction.

### Filtering bugs

Use the filter chips above the table to focus on what matters:

- **All** — every bug in the area path
- **Triaged** / **Untriaged** — whether Claude has assessed the bug yet
- **Copilot Ready** / **Copilot Possible** / **Human Required** — filter by Copilot readiness
- **Assigned to Me** — toggle to show only bugs assigned to your ADO identity

### Running triage

There are two ways to triage bugs:

1. **Batch triage** — click **Run Claude Analysis** in the toolbar. You'll be prompted for how many bugs to process. Claude will assess each bug sequentially and apply tags.
2. **Single bug re-triage** — click the re-triage button (&#8635;) on any bug row. Useful for re-assessing a bug after its description has been updated.

Both launch a Claude Code process in the background. The **Claude Analysis panel** appears in the bottom-right corner streaming the session output in real time. You can minimize or close it — the bug table stays fully interactive while triage runs. When triage completes, the bug list auto-refreshes.

### Understanding triage scores

Each triage produces three scores applied as ADO tags:

**Actionability (0-100)** — Is there enough information to act on this bug?
- 80-100: Fully actionable — clear repro steps, expected/actual behavior, error details
- 50-79: Partially actionable — some info present but gaps remain
- 0-49: Needs more info — tagged `needs-info`

**ROI (0-100)** — How valuable is fixing this bug?
- 70-100: High ROI — tagged `high-roi` (high severity, customer-reported, regression, etc.)
- 40-69: Medium ROI
- 0-39: Low ROI — backlog candidate

**Copilot Readiness (0-100)** — Can GitHub Copilot coding agent fix this autonomously?
- 75-100: **Copilot Ready** — clear problem, code-scoped, small isolated fix. Tagged `copilot-ready`
- 50-74: **Copilot Possible** — could work if the issue description is enriched. Tagged `copilot-possible`
- 0-49: **Human Required** — needs investigation, design decisions, or cross-team coordination. Tagged `human-required`

Hard blockers (UI/UX decisions, infra changes, ambiguous root cause, AI quality issues) override to Human Required regardless of score.

### Assigning to Copilot

Click the robot button (&#129302;) on a Copilot Ready or Copilot Possible bug. Depending on your settings, this will:

1. **Always**: Add the `copilot-ready` tag to the work item
2. **If Copilot User ID is configured**: Assign the bug to the GitHub Copilot service account
3. **If Repo GUIDs are configured**: Link the configured branch to the work item

The toast notification tells you exactly what happened. If the Copilot User ID isn't configured, it will tag the bug but skip the assignment — see [Optional settings](#optional-settings-for-copilot-assignment) to set this up.

### Suggested workflow

1. **Run batch triage** on your area path to score all open bugs
2. **Filter to Copilot Ready** — these are your quick wins. Assign them to Copilot.
3. **Filter to Copilot Possible** — review these bugs, enrich their descriptions with missing details (specific files, expected behavior, acceptance criteria), then re-triage
4. **Filter to Human Required** — these need human investigation. Use the ROI column to prioritize which ones to tackle first
5. **Re-triage periodically** as bugs get updated with new information

## Updating

To update an existing install to the latest release:

```powershell
cd claude-ado-companion

# Stop the running instance (Windows can't overwrite a locked exe)
Get-Process ClaudeAdoCompanion -ErrorAction SilentlyContinue | Stop-Process -Force

# Remove the old exe, then download the latest release
Remove-Item .\ClaudeAdoCompanion.exe -Force
gh release download --repo gdodd1977/claude-ado-companion --pattern "*" --dir . --clobber
```

Your settings (`appsettings.local.json`) are preserved across updates.

To also pull the latest triage skills and source code:

```powershell
git pull
```

## Demo Mode

Run with `--demo` to see the dashboard with mock data — no ADO connection or `az login` required:

```powershell
.\ClaudeAdoCompanion.exe --demo
# or: dotnet run -- --demo
```

This loads 10 sample bugs covering all triage states (Copilot Ready, Copilot Possible, Human Required, and Untriaged) so you can explore the UI without any prerequisites.

## Debug Mode

Run with `--debug` to enable exception logging and a diagnostic endpoint:

```powershell
.\ClaudeAdoCompanion.exe --debug
# or: dotnet run -- --debug
```

In debug mode:
- All unhandled exceptions are logged with request details and stack traces to `%TEMP%\claude-ado-companion-debug.log`
- A diagnostic endpoint is available at **http://localhost:5200/api/debug/log** to view the log from your browser
- The log file path is printed to the console on startup

This is useful for troubleshooting ADO connectivity issues, auth errors, or any unexpected behavior.

## Triage Skills

The companion ships with Claude Code skills for triaging ADO bugs. These are auto-detected by Claude when you discuss bug triage, or can be invoked manually with `/triage-bug` and `/triage-bugs`. They use `az boards` CLI commands — no MCP server required. The skills read your ADO connection details from `appsettings.local.json` automatically.

| Skill | Description |
|---|---|
| `/triage-bug <id>` | Triage a single bug — scores actionability, ROI, and Copilot agent readiness, then applies tags |
| `/triage-bugs` | Batch triage all active bugs in your configured area path |

Run from the Claude Code CLI inside the repo:

```powershell
claude
# then type: /triage-bug 12345
```

Or trigger triage directly from the dashboard UI via the "Run Claude Analysis" button.

## Development

### Build from source

```powershell
cd src
dotnet build
dotnet run
```

### Publish

```powershell
dotnet publish src/ClaudeAdoCompanion.csproj -c Release
```

The self-contained single-file exe will be in `src\bin\Release\net8.0\win-x64\publish\`.

### Publishing a New Release

```powershell
# Build
dotnet publish src/ClaudeAdoCompanion.csproj -c Release

# Create release (bump the version tag)
gh release create v1.0.0 --repo gdodd1977/claude-ado-companion --title "v1.0.0" --notes "Description of changes" `
  src/bin/Release/net8.0/win-x64/publish/ClaudeAdoCompanion.exe `
  src/bin/Release/net8.0/win-x64/publish/appsettings.json `
  install.ps1
```

Users can then update by re-downloading the latest release.
