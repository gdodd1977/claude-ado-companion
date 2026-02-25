#Requires -Version 5.1
<#
.SYNOPSIS
    Checks prerequisites for the Claude ADO Companion.

.DESCRIPTION
    Validates that Azure CLI, Azure login, and Claude CLI are available.
    Reports what's ready and gives clear next steps for anything missing.
    Does not install anything automatically.
#>

$ErrorActionPreference = 'SilentlyContinue'

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Claude ADO Companion - Prerequisite Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$issues = @()

# -- 1. Azure CLI --

Write-Host "[1/3] Azure CLI ... " -NoNewline

$azVersion = az --version 2>&1 | Select-Object -First 1
if ($LASTEXITCODE -eq 0 -and $azVersion -match 'azure-cli') {
    Write-Host "OK  ($azVersion)" -ForegroundColor Green
}
else {
    Write-Host "NOT FOUND" -ForegroundColor Red
    $issues += @{
        Name  = "Azure CLI"
        Fix   = @(
            "winget install --id Microsoft.AzureCLI -e"
            "  -- or download from https://aka.ms/installazurecliwindows"
        )
    }
}

# -- 2. Azure login --

Write-Host "[2/3] Azure login ... " -NoNewline

$azAccount = az account show -o json 2>&1 | ConvertFrom-Json
if ($LASTEXITCODE -eq 0 -and $azAccount.user) {
    $upn = $azAccount.user.name
    Write-Host "OK  (logged in as $upn)" -ForegroundColor Green
}
else {
    Write-Host "NOT LOGGED IN" -ForegroundColor Yellow
    $issues += @{
        Name  = "Azure login"
        Fix   = @(
            "az login"
        )
    }
}

# -- 3. Claude CLI --

Write-Host "[3/3] Claude CLI ... " -NoNewline

$claudeVersion = claude --version 2>&1
if ($LASTEXITCODE -eq 0 -and $claudeVersion) {
    Write-Host "OK  ($claudeVersion)" -ForegroundColor Green
}
else {
    Write-Host "NOT FOUND" -ForegroundColor Red
    $issues += @{
        Name  = "Claude CLI"
        Fix   = @(
            "npm install -g @anthropic-ai/claude-code"
            "  -- requires Node.js 18+. Install Node: https://nodejs.org"
        )
    }
}

# -- Summary --

Write-Host ""
if ($issues.Count -eq 0) {
    Write-Host "All prerequisites are met. You're ready to go!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Quick start:" -ForegroundColor Cyan
    Write-Host "  cd $PSScriptRoot"
    Write-Host "  claude"
    Write-Host ""
}
else {
    Write-Host "$($issues.Count) item(s) need attention:" -ForegroundColor Yellow
    Write-Host ""
    foreach ($issue in $issues) {
        Write-Host "  $($issue.Name)" -ForegroundColor Yellow
        foreach ($step in $issue.Fix) {
            Write-Host "    $step"
        }
        Write-Host ""
    }
}

# -- Shortcut Creation --

$exePath = Join-Path $PSScriptRoot "ClaudeAdoCompanion.exe"
$exeExists = Test-Path $exePath

if ($exeExists) {
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    Write-Host "  Create Shortcuts" -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  [1] Desktop shortcut"
    Write-Host "  [2] Start Menu shortcut"
    Write-Host "  [3] Both"
    Write-Host "  [4] Skip"
    Write-Host ""
    $choice = Read-Host "Choose an option (1-4)"

    function New-AppShortcut {
        param([string]$ShortcutPath)
        $ws = New-Object -ComObject WScript.Shell
        $sc = $ws.CreateShortcut($ShortcutPath)
        $sc.TargetPath = $exePath
        $sc.WorkingDirectory = $PSScriptRoot
        $sc.Description = "Claude ADO Companion - Bug Triage Dashboard"
        $sc.Save()
        Write-Host "  Created: $ShortcutPath" -ForegroundColor Green
    }

    $desktopPath = [Environment]::GetFolderPath("Desktop")
    $startMenuPath = Join-Path ([Environment]::GetFolderPath("Programs")) "Claude ADO Companion.lnk"

    switch ($choice) {
        "1" {
            New-AppShortcut (Join-Path $desktopPath "Claude ADO Companion.lnk")
        }
        "2" {
            New-AppShortcut $startMenuPath
        }
        "3" {
            New-AppShortcut (Join-Path $desktopPath "Claude ADO Companion.lnk")
            New-AppShortcut $startMenuPath
        }
        default {
            Write-Host "  Skipped shortcut creation." -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}
else {
    Write-Host "Tip: Download the release exe into this directory, then re-run this script to create shortcuts." -ForegroundColor DarkGray
    Write-Host ""
}

Write-Host "Note for developers: building from source requires .NET 9 SDK (https://dot.net/download)." -ForegroundColor DarkGray
Write-Host ""
