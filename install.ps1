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
