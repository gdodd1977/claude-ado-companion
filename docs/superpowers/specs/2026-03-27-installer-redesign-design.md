# Installer Redesign — Design Spec

## Problem

The current install experience has too much friction for new users:

1. `install.ps1` is misleadingly named — it only checks prerequisites and creates shortcuts
2. Downloading the release requires `gh` CLI (authenticated), which most people don't have
3. The README assumes everyone will use `gh release download` — no browser-download path
4. New users must manually clone the repo, download the exe, and figure out prerequisites separately

## Solution

Rewrite `install.ps1` into a real installer that handles everything: clone, download, prerequisite checking, and shortcut creation. Update the README to lead with a one-liner install command.

## Installer Behavior

### Context Detection

On launch, the script checks whether it's running inside an existing clone of the repo (`.git` directory exists and remote matches `gdodd1977/claude-ado-companion`).

- **Fresh install**: Prompts for install directory, clones repo, downloads release assets
- **Update mode**: Runs `git pull`, re-downloads the latest release exe, checks prerequisites

### Fresh Install Flow

1. **Check git** — If `git` is not installed, print install instructions and exit. Git is the only hard dependency for installation.
2. **Prompt for install location** — Default: `$HOME\claude-ado-companion`. User can override.
3. **Clone repo** — `git clone https://github.com/gdodd1977/claude-ado-companion.git <path>`
4. **Download release assets** — Use `Invoke-WebRequest` to download from GitHub's direct release URLs:
   - `https://github.com/gdodd1977/claude-ado-companion/releases/latest/download/ClaudeAdoCompanion.exe`
   - `https://github.com/gdodd1977/claude-ado-companion/releases/latest/download/appsettings.json`
5. **Check prerequisites** — Azure CLI installed, `az login` active, Claude Code CLI installed. Report status for each; do not block install on missing prerequisites.
6. **Offer shortcuts** — Desktop and/or Start Menu shortcuts (same interactive prompt as today)
7. **Print next steps** — How to launch, how to configure on first run

### Update Flow

1. **Stop running instance** — `Get-Process ClaudeAdoCompanion -ErrorAction SilentlyContinue | Stop-Process -Force`
2. **Git pull** — Pull latest repo changes (skills, docs)
3. **Re-download exe** — Same `Invoke-WebRequest` approach as fresh install, overwrites existing exe
4. **Check prerequisites** — Same checks as fresh install
5. **Print summary** — What was updated

### No `gh` CLI Dependency

The `gh` CLI is removed from the prerequisites table entirely. Release assets are downloaded via direct GitHub URLs using `Invoke-WebRequest`, which is built into PowerShell.

## README Changes

### Quick Start (new)

The Quick Start section is rewritten to lead with the one-liner:

```powershell
irm https://raw.githubusercontent.com/gdodd1977/claude-ado-companion/main/install.ps1 | iex
```

A secondary option mentions downloading `install.ps1` from the Releases page and running it manually.

"Build from source" remains as a separate option for developers.

### Prerequisites Table (updated)

Remove `gh` CLI row. The table becomes:

| Prerequisite | Required for | Install |
|---|---|---|
| Azure CLI | Dashboard + triage | `winget install --id Microsoft.AzureCLI -e` |
| Azure login | Dashboard + triage | `az login` |
| Claude Code CLI | Triage skills | `npm install -g @anthropic-ai/claude-code` (requires Node.js 18+) |

Git is mentioned as needed for the installer but not in the prerequisites table (it's an install-time dependency, not a runtime one).

### Updating Section (updated)

Replace the manual update instructions with: "Re-run `install.ps1` from inside your install directory" or the `irm` one-liner.

## Files Changed

| File | Change |
|---|---|
| `install.ps1` | Rewrite: add clone, download, update detection; keep prerequisite checks and shortcut creation |
| `README.md` | Rewrite Quick Start, update prerequisites table, update Updating section |

## Out of Scope

- MSI/setup wizard packaging
- Linux/macOS support (exe is win-x64 only)
- Auto-update mechanism (user re-runs installer manually)
