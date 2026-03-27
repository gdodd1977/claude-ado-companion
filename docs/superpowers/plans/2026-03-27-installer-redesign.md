# Installer Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the prerequisite-checker-only `install.ps1` with a real installer that clones the repo, downloads the release exe, checks prerequisites, and creates shortcuts — then update the README to match.

**Architecture:** Single PowerShell script with two modes (fresh install vs update) detected automatically. README rewritten to lead with a one-liner install command. No new files created — two existing files modified.

**Tech Stack:** PowerShell 5.1+, `Invoke-WebRequest`, Git, GitHub-flavored Markdown

---

### Task 1: Rewrite install.ps1 as a real installer

**Files:**
- Modify: `install.ps1` (full rewrite)

- [ ] **Step 1: Write the new install.ps1**

Replace the entire contents of `install.ps1` with the script below. Key design notes:
- When run via `irm | iex`, `$PSScriptRoot` is empty — the script detects this and uses `$PWD` as the context for update detection.
- Fresh install prompts for a directory (default `$HOME\claude-ado-companion`), clones the repo, downloads release assets.
- Update mode is triggered when the script detects it's inside an existing clone (`.git` exists with the correct remote).
- Prerequisite checks are identical to today's logic but renumbered.
- Shortcut creation is preserved from the current script.
- `$ErrorActionPreference` is set to `Stop` for clone/download operations but individual prerequisite checks use `-ErrorAction SilentlyContinue` so one failure doesn't abort the report.

```powershell
#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or updates the Claude ADO Companion.

.DESCRIPTION
    Fresh install: clones the repo, downloads the latest release exe, checks
    prerequisites, and optionally creates shortcuts.

    Update: pulls latest repo changes, re-downloads the exe, checks prerequisites.

    Run from anywhere:
        irm https://raw.githubusercontent.com/gdodd1977/claude-ado-companion/main/install.ps1 | iex

    Or run locally inside an existing install to update:
        powershell -ExecutionPolicy Bypass -File install.ps1
#>

$repoUrl   = "https://github.com/gdodd1977/claude-ado-companion.git"
$repoOwner = "gdodd1977/claude-ado-companion"
$assetBase = "https://github.com/$repoOwner/releases/latest/download"
$assets    = @("ClaudeAdoCompanion.exe", "appsettings.json")

# ── Helpers ────────────────────────────────────────────────────────────────

function Write-Banner {
    param([string]$Text)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Test-InsideRepo {
    # Returns $true if the current directory is inside a clone of our repo.
    if (-not (Test-Path ".git")) { return $false }
    $remote = git remote get-url origin 2>$null
    return ($remote -and $remote -match 'claude-ado-companion')
}

function Install-ReleaseAssets {
    param([string]$TargetDir)
    foreach ($file in $assets) {
        $url  = "$assetBase/$file"
        $dest = Join-Path $TargetDir $file
        Write-Host "  Downloading $file ... " -NoNewline
        try {
            Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
            Write-Host "OK" -ForegroundColor Green
        }
        catch {
            Write-Host "FAILED" -ForegroundColor Red
            Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

function Test-Prerequisites {
    Write-Banner "Checking Prerequisites"

    $issues = @()

    # -- 1. Azure CLI --
    Write-Host "  [1/3] Azure CLI ... " -NoNewline
    $azVersion = az --version 2>&1 | Select-Object -First 1
    if ($LASTEXITCODE -eq 0 -and $azVersion -match 'azure-cli') {
        Write-Host "OK  ($azVersion)" -ForegroundColor Green
    }
    else {
        Write-Host "NOT FOUND" -ForegroundColor Red
        $issues += @{
            Name = "Azure CLI"
            Fix  = @(
                "winget install --id Microsoft.AzureCLI -e"
                "  -- or download from https://aka.ms/installazurecliwindows"
            )
        }
    }

    # -- 2. Azure login --
    Write-Host "  [2/3] Azure login ... " -NoNewline
    $azAccount = az account show -o json 2>&1 | ConvertFrom-Json
    if ($LASTEXITCODE -eq 0 -and $azAccount.user) {
        $upn = $azAccount.user.name
        Write-Host "OK  (logged in as $upn)" -ForegroundColor Green
    }
    else {
        Write-Host "NOT LOGGED IN" -ForegroundColor Yellow
        $issues += @{
            Name = "Azure login"
            Fix  = @("az login")
        }
    }

    # -- 3. Claude CLI --
    Write-Host "  [3/3] Claude CLI ... " -NoNewline
    $claudeVersion = claude --version 2>&1
    if ($LASTEXITCODE -eq 0 -and $claudeVersion) {
        Write-Host "OK  ($claudeVersion)" -ForegroundColor Green
    }
    else {
        Write-Host "NOT FOUND" -ForegroundColor Red
        $issues += @{
            Name = "Claude CLI"
            Fix  = @(
                "npm install -g @anthropic-ai/claude-code"
                "  -- requires Node.js 18+. Install Node: https://nodejs.org"
            )
        }
    }

    Write-Host ""
    if ($issues.Count -eq 0) {
        Write-Host "  All prerequisites met!" -ForegroundColor Green
    }
    else {
        Write-Host "  $($issues.Count) item(s) need attention:" -ForegroundColor Yellow
        Write-Host ""
        foreach ($issue in $issues) {
            Write-Host "    $($issue.Name)" -ForegroundColor Yellow
            foreach ($step in $issue.Fix) {
                Write-Host "      $step"
            }
            Write-Host ""
        }
    }
}

function New-AppShortcut {
    param([string]$ShortcutPath, [string]$ExePath, [string]$WorkDir)
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($ShortcutPath)
    $sc.TargetPath = "cmd.exe"
    $sc.Arguments = "/k `"$ExePath`""
    $sc.WorkingDirectory = $WorkDir
    $sc.Description = "Claude ADO Companion - Bug Triage Dashboard"
    $sc.Save()
    Write-Host "  Created: $ShortcutPath" -ForegroundColor Green
}

function Invoke-ShortcutPrompt {
    param([string]$InstallDir)
    $exePath = Join-Path $InstallDir "ClaudeAdoCompanion.exe"
    if (-not (Test-Path $exePath)) { return }

    Write-Banner "Create Shortcuts"
    Write-Host "  [1] Desktop shortcut"
    Write-Host "  [2] Start Menu shortcut"
    Write-Host "  [3] Both"
    Write-Host "  [4] Skip"
    Write-Host ""
    $choice = Read-Host "  Choose an option (1-4)"

    $desktopPath   = [Environment]::GetFolderPath("Desktop")
    $startMenuPath = Join-Path ([Environment]::GetFolderPath("Programs")) "Claude ADO Companion.lnk"

    switch ($choice) {
        "1" { New-AppShortcut (Join-Path $desktopPath "Claude ADO Companion.lnk") $exePath $InstallDir }
        "2" { New-AppShortcut $startMenuPath $exePath $InstallDir }
        "3" {
            New-AppShortcut (Join-Path $desktopPath "Claude ADO Companion.lnk") $exePath $InstallDir
            New-AppShortcut $startMenuPath $exePath $InstallDir
        }
        default { Write-Host "  Skipped." -ForegroundColor DarkGray }
    }
    Write-Host ""
}

function Write-NextSteps {
    param([string]$InstallDir)
    Write-Banner "You're All Set"
    Write-Host "  To launch the dashboard:"
    Write-Host "    cd `"$InstallDir`"" -ForegroundColor White
    Write-Host "    .\ClaudeAdoCompanion.exe" -ForegroundColor White
    Write-Host ""
    Write-Host "  Then open http://localhost:5200 in your browser."
    Write-Host ""
    Write-Host "  On first launch you'll be prompted for your ADO connection details."
    Write-Host "  To update later, re-run this installer from inside the install directory."
    Write-Host ""
}

# ── Main ───────────────────────────────────────────────────────────────────

# Determine context: are we inside an existing clone?
# When run via irm | iex, $PSScriptRoot is empty — fall back to $PWD.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
Push-Location $scriptDir

if (Test-InsideRepo) {
    # ── UPDATE MODE ────────────────────────────────────────────────────
    Write-Banner "Claude ADO Companion - Update"

    # Stop running instance so the exe can be overwritten
    $proc = Get-Process ClaudeAdoCompanion -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "  Stopping running instance ... " -NoNewline
        $proc | Stop-Process -Force
        Write-Host "OK" -ForegroundColor Green
    }

    # Pull latest repo (skills, docs, etc.)
    Write-Host "  Pulling latest changes ... " -NoNewline
    $pullOutput = git pull 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK" -ForegroundColor Green
    }
    else {
        Write-Host "WARNING" -ForegroundColor Yellow
        Write-Host "    $pullOutput" -ForegroundColor Yellow
    }

    # Re-download release assets
    Write-Host ""
    Write-Host "  Downloading latest release:" -ForegroundColor Cyan
    Install-ReleaseAssets -TargetDir $scriptDir

    Test-Prerequisites
    Write-NextSteps -InstallDir $scriptDir
}
else {
    # ── FRESH INSTALL ──────────────────────────────────────────────────
    Write-Banner "Claude ADO Companion - Install"

    # Check git is available
    $gitVersion = git --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Git is required but was not found." -ForegroundColor Red
        Write-Host ""
        Write-Host "  Install git:" -ForegroundColor Yellow
        Write-Host "    winget install --id Git.Git -e"
        Write-Host "    -- or download from https://git-scm.com/download/win"
        Write-Host ""
        Pop-Location
        return
    }
    Write-Host "  Git ... OK  ($gitVersion)" -ForegroundColor Green

    # Prompt for install location
    $defaultDir = Join-Path $HOME "claude-ado-companion"
    Write-Host ""
    $installDir = Read-Host "  Install location (default: $defaultDir)"
    if ([string]::IsNullOrWhiteSpace($installDir)) {
        $installDir = $defaultDir
    }

    # Clone
    if (Test-Path (Join-Path $installDir ".git")) {
        Write-Host ""
        Write-Host "  Directory already contains a git repo. Switching to update mode." -ForegroundColor Yellow
        Set-Location $installDir
        # Re-run as update (recursive call with correct location)
        Pop-Location
        Push-Location $installDir
        # Pull + download inline (same as update mode)
        $proc = Get-Process ClaudeAdoCompanion -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "  Stopping running instance ... " -NoNewline
            $proc | Stop-Process -Force
            Write-Host "OK" -ForegroundColor Green
        }
        Write-Host "  Pulling latest changes ... " -NoNewline
        $pullOutput = git pull 2>&1
        if ($LASTEXITCODE -eq 0) { Write-Host "OK" -ForegroundColor Green }
        else {
            Write-Host "WARNING" -ForegroundColor Yellow
            Write-Host "    $pullOutput" -ForegroundColor Yellow
        }
        Write-Host ""
        Write-Host "  Downloading latest release:" -ForegroundColor Cyan
        Install-ReleaseAssets -TargetDir $installDir
        Test-Prerequisites
        Invoke-ShortcutPrompt -InstallDir $installDir
        Write-NextSteps -InstallDir $installDir
        Pop-Location
        return
    }

    Write-Host ""
    Write-Host "  Cloning repository ... " -NoNewline
    $cloneOutput = git clone $repoUrl $installDir 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK" -ForegroundColor Green
    }
    else {
        Write-Host "FAILED" -ForegroundColor Red
        Write-Host "    $cloneOutput" -ForegroundColor Red
        Pop-Location
        return
    }

    # Download release assets into the clone
    Write-Host ""
    Write-Host "  Downloading latest release:" -ForegroundColor Cyan
    Install-ReleaseAssets -TargetDir $installDir

    # Check prerequisites
    Push-Location $installDir
    Test-Prerequisites

    # Shortcuts
    Invoke-ShortcutPrompt -InstallDir $installDir

    # Next steps
    Write-NextSteps -InstallDir $installDir
    Pop-Location
}

Pop-Location
```

- [ ] **Step 2: Verify the script parses without syntax errors**

Run:
```powershell
powershell -NoProfile -Command "& { $null = [System.Management.Automation.PSParser]::Tokenize((Get-Content 'install.ps1' -Raw), [ref]$null); Write-Host 'OK - no syntax errors' }"
```

Expected: `OK - no syntax errors`

- [ ] **Step 3: Commit**

```bash
git add install.ps1
git commit -m "Rewrite install.ps1 as a real installer with clone, download, and update support"
```

---

### Task 2: Update README.md

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update Prerequisites section**

Remove the GitHub CLI row from the prerequisites table. Replace the prerequisite checker instructions to reference the installer instead. The new table:

```markdown
## Prerequisites

| Prerequisite | Required for | Install |
|---|---|---|
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli-windows) | Dashboard + triage | `winget install --id Microsoft.AzureCLI -e` |
| Azure login | Dashboard + triage | `az login` (needs access to your ADO organization) |
| [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code/overview) | Triage skills | `npm install -g @anthropic-ai/claude-code` (requires Node.js 18+) |

The installer checks these automatically and tells you what's missing.
```

- [ ] **Step 2: Rewrite Quick Start section**

Replace everything from `## Quick Start` through `### Open the dashboard` with the new installer-first flow:

```markdown
## Quick Start

### Install

Run the one-liner in PowerShell:

\```powershell
irm https://raw.githubusercontent.com/gdodd1977/claude-ado-companion/main/install.ps1 | iex
\```

This clones the repo (needed for Claude Code triage skills), downloads the latest release, checks prerequisites, and optionally creates shortcuts.

> **Alternative:** Download `install.ps1` from the [latest release](https://github.com/gdodd1977/claude-ado-companion/releases/latest) and run it:
> \```powershell
> powershell -ExecutionPolicy Bypass -File install.ps1
> \```

### Build from source

Requires [.NET 9 SDK](https://dot.net/download).

\```powershell
git clone https://github.com/gdodd1977/claude-ado-companion.git
cd claude-ado-companion/src
dotnet run
\```

### First-run setup
```

Everything from `### First-run setup` through the rest of that section stays unchanged.

- [ ] **Step 3: Update Table of Contents**

Replace the Quick Start subsections in the ToC to match the new structure:

```markdown
- [Quick Start](#quick-start)
  - [Install](#install)
  - [Build from source](#build-from-source)
  - [First-run setup](#first-run-setup)
```

- [ ] **Step 4: Rewrite Updating section**

Replace the entire `## Updating` section:

```markdown
## Updating

Re-run the installer from inside your install directory:

\```powershell
cd claude-ado-companion
powershell -ExecutionPolicy Bypass -File install.ps1
\```

Or run the one-liner from anywhere — it detects the existing install and updates in place:

\```powershell
irm https://raw.githubusercontent.com/gdodd1977/claude-ado-companion/main/install.ps1 | iex
\```

This pulls the latest repo changes (skills, docs), re-downloads the exe, and checks prerequisites. Your settings (`appsettings.local.json`) are preserved.
```

- [ ] **Step 5: Update Publishing a New Release section**

In the Development section, update the `gh release create` command to include `install.ps1` (name unchanged) since the release asset name stays the same:

No change needed here — the existing command already includes `install.ps1`.

- [ ] **Step 6: Commit**

```bash
git add README.md
git commit -m "Update README with one-liner install, remove gh CLI prerequisite"
```

---

### Task 3: Verify end-to-end

- [ ] **Step 1: Verify install.ps1 syntax**

```powershell
powershell -NoProfile -Command "& { $null = [System.Management.Automation.PSParser]::Tokenize((Get-Content 'install.ps1' -Raw), [ref]$null); Write-Host 'OK' }"
```

Expected: `OK`

- [ ] **Step 2: Verify README links and structure**

Read through the README and verify:
- All ToC anchors match section headings
- No references to `gh release download` remain in the install/update paths
- `gh` CLI only appears in the Development > Publishing section (where the maintainer uses it)
- The `irm` one-liner URL is correct

- [ ] **Step 3: Final commit if any fixups needed**

```bash
git add -A
git commit -m "Fix any issues found during verification"
```
