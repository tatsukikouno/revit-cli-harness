<#
.SYNOPSIS
  Start, stop and inspect the RevitHarness DialogWatcher.
.EXAMPLE
  powershell.exe -File scripts/dialog-watcher.ps1 start -WatcherArgs '--dry-run'
  powershell.exe -File scripts/dialog-watcher.ps1 status
  powershell.exe -File scripts/dialog-watcher.ps1 tail-log -Lines 50 -Follow
  powershell.exe -File scripts/dialog-watcher.ps1 stop
#>
param(
    [Parameter(Mandatory = $true, Position = 0)][ValidateSet('start', 'stop', 'status', 'tail-log')][string]$Command,
    [string]$Exe,
    [string[]]$WatcherArgs = @(),
    [int]$Lines = 20,
    [switch]$Follow
)
$ErrorActionPreference = 'Stop'
$dialogs = Join-Path $env:LOCALAPPDATA 'RevitHarness\dialogs'
$pidFile = Join-Path $dialogs 'watcher.pid'
$stopFile = Join-Path $dialogs 'watcher.stop'

function Get-WatcherProcess {
    if (-not (Test-Path $pidFile)) { return $null }
    $id = [int](Get-Content $pidFile -Raw)
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p -and $p.ProcessName -eq 'RevitHarness.DialogWatcher') { return $p }
    return $null
}

function Find-Exe {
    if ($Exe) { return $Exe }
    $repo = Split-Path $PSScriptRoot -Parent
    $candidates = @(
        Join-Path $repo 'src\RevitHarness.DialogWatcher\bin\x64\Release\net8.0-windows\RevitHarness.DialogWatcher.exe'
        Join-Path $repo 'src\RevitHarness.DialogWatcher\bin\x64\Debug\net8.0-windows\RevitHarness.DialogWatcher.exe'
    ) | Where-Object { Test-Path $_ } | Sort-Object { (Get-Item $_).LastWriteTime } -Descending
    if (-not $candidates) { throw 'DialogWatcher exe not found. Build it or pass -Exe.' }
    return @($candidates)[0]
}

switch ($Command) {
    'start' {
        if (Get-WatcherProcess) { Write-Output 'already running'; exit 0 }
        $exePath = Find-Exe
        $argList = @()
        if ($WatcherArgs) { $argList += $WatcherArgs }
        $startArgs = @{ FilePath = $exePath; WindowStyle = 'Hidden'; PassThru = $true }
        if ($argList.Count -gt 0) { $startArgs.ArgumentList = $argList }
        $p = Start-Process @startArgs
        Start-Sleep -Milliseconds 800
        if ($p.HasExited) { Write-Output "exited immediately (code $($p.ExitCode)); another watcher may be running"; exit 1 }
        Write-Output "started pid $($p.Id)"
    }
    'stop' {
        $p = Get-WatcherProcess
        if (-not $p) { Write-Output 'not running'; exit 0 }
        New-Item -ItemType Directory -Force -Path $dialogs | Out-Null
        Set-Content -Path $stopFile -Value 'stop'
        if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force; Write-Output 'killed after timeout' }
        else { Write-Output 'stopped' }
        Remove-Item $stopFile -ErrorAction SilentlyContinue
    }
    'status' {
        $p = Get-WatcherProcess
        if ($p) { Write-Output "running pid $($p.Id) since $($p.StartTime.ToString('s'))" } else { Write-Output 'not running'; exit 1 }
        $latest = Get-ChildItem $dialogs -Filter 'dialogs_*.jsonl' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
        if ($latest) { Write-Output "latest log: $($latest.FullName)" }
    }
    'tail-log' {
        $latest = Get-ChildItem $dialogs -Filter 'dialogs_*.jsonl' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
        if (-not $latest) { Write-Output 'no log yet'; exit 0 }
        if ($Follow) { Get-Content $latest.FullName -Tail $Lines -Wait } else { Get-Content $latest.FullName -Tail $Lines }
    }
}
