# Claude ADO Companion

A companion tool for the Copilot Flows team featuring:

- **Bug Triage Dashboard** — View, sort, filter, and triage ADO bugs with AI-generated scores
- **Claude Code Triage Skills** — `/triage-bug` and `/triage-bugs` skills that assess actionability, ROI, and Copilot agent readiness
- **Session Viewer** — Browse and live-stream Claude Code sessions in real time

## Prerequisites

| Prerequisite | Required for | Install |
|---|---|---|
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli-windows) | Dashboard + triage | `winget install --id Microsoft.AzureCLI -e` |
| Azure login | Dashboard + triage | `az login` (needs access to `msazure.visualstudio.com`) |
| [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code/overview) | Triage skills | `npm install -g @anthropic-ai/claude-code` (requires Node.js 18+) |

Run the prerequisite checker to verify your setup:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

It checks each prerequisite and prints what's ready vs what needs attention. It does **not** install anything automatically.

## Quick Start

### Option A: Download a release (recommended)

```powershell
# Download the latest release exe + config
gh release download --repo gdodd1977/copilot-flows-claude-companion --dir .
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

The companion ships with Claude Code slash commands for triaging ADO bugs. These use `az boards` CLI commands — no MCP server required.

| Skill | Description |
|---|---|
| `/triage-bug <id>` | Triage a single bug — scores actionability, ROI, and Copilot agent readiness, then applies tags |
| `/triage-bugs` | Batch triage all active bugs on the AI Experiences board |

Run from the Claude Code CLI inside the repo:

```powershell
claude
# then type: /triage-bug 36500000
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
gh release create v1.1.0 --repo gdodd1977/copilot-flows-claude-companion --title "v1.1.0" --notes "Description of changes" `
  src/bin/Release/net8.0/win-x64/publish/ClaudeAdoCompanion.exe `
  src/bin/Release/net8.0/win-x64/publish/appsettings.json `
  install.ps1
```

Users can then update by re-downloading the latest release.
