# Claude ADO Companion

A generic Azure DevOps bug triage companion powered by Claude Code. Connect it to any ADO project to:

- **Bug Triage Dashboard** — View, sort, filter, and triage ADO bugs with AI-generated scores
- **Claude Code Triage Skills** — `/triage-bug` and `/triage-bugs` skills that assess actionability, ROI, and Copilot agent readiness
- **Session Viewer** — Browse and live-stream Claude Code sessions in real time

## Prerequisites

| Prerequisite | Required for | Install |
|---|---|---|
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

## Demo Mode

Run with `--demo` to see the dashboard with mock data — no ADO connection or `az login` required:

```powershell
.\ClaudeAdoCompanion.exe --demo
# or: dotnet run -- --demo
```

This loads 10 sample bugs covering all triage states (Copilot Ready, Copilot Possible, Human Required, and Untriaged) so you can explore the UI without any prerequisites.

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
