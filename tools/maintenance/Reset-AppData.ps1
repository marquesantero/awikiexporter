<#
.SYNOPSIS
    Removes all local ExportAzureWiki (AWikiExport) per-user data so the next
    launch starts fresh at the onboarding/setup wizard.

.DESCRIPTION
    MSIX has no uninstall hook, and the app (full-trust) stores its data in the
    REAL user profile, not the package container -- so uninstalling the package
    leaves data behind and onboarding never runs again. This script wipes:

      * HKCU\Software\ExportAzureWiki        (SetupComplete, connection string,
                                              database type -- the onboarding gate)
      * %LocalAppData%\ExportAzureWiki       (local SQLite DB, Cache, Logs,
                                              MSAL token cache)
      * %TEMP%\ExportAzureWiki               (wiki clones, source/markdown and
                                              Word image caches)

    It does NOT touch external databases (SQL Server / PostgreSQL / MySQL): those
    live on a server and are out of scope for a local reset.

    Run it with the app closed. It will stop a running instance first (with
    confirmation, unless -Force).

.PARAMETER Force
    Skip the confirmation prompt and stop a running app without asking.

.PARAMETER KeepLogs
    Preserve %LocalAppData%\ExportAzureWiki\Logs (useful when resetting to
    reproduce an issue but keeping the diagnostic trail).

.EXAMPLE
    pwsh ./tools/maintenance/Reset-AppData.ps1

.EXAMPLE
    pwsh ./tools/maintenance/Reset-AppData.ps1 -Force -KeepLogs
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch] $Force,
    [switch] $KeepLogs
)

$ErrorActionPreference = 'Stop'

$registryPath = 'HKCU:\Software\ExportAzureWiki'
$localData    = Join-Path $env:LOCALAPPDATA 'ExportAzureWiki'
$tempData     = Join-Path $env:TEMP 'ExportAzureWiki'
$logsDir      = Join-Path $localData 'Logs'

Write-Host "This will permanently delete local AWikiExport data:" -ForegroundColor Yellow
Write-Host "  - Registry: $registryPath"
Write-Host "  - Folder:   $localData$(if ($KeepLogs) { '  (keeping Logs)' })"
Write-Host "  - Folder:   $tempData"
Write-Host "External databases (SQL Server/PostgreSQL/MySQL) are NOT affected." -ForegroundColor DarkGray
Write-Host ""

if (-not $Force -and -not $PSCmdlet.ShouldProcess('local AWikiExport data', 'DELETE')) {
    Write-Host "Aborted." -ForegroundColor Cyan
    return
}

# --- Stop a running instance (files would be locked otherwise) -----------
$procs = Get-Process -Name 'ExportAzureWiki.Wpf' -ErrorAction SilentlyContinue
if ($procs) {
    if ($Force -or $PSCmdlet.ShouldProcess('running AWikiExport process', 'STOP')) {
        Write-Host "Stopping running app..." -ForegroundColor Cyan
        $procs | Stop-Process -Force
        Start-Sleep -Milliseconds 600
    }
    else {
        Write-Warning "App is running; close it and re-run. Aborted."
        return
    }
}

function Remove-Tree([string] $path) {
    if (Test-Path $path) {
        try {
            # SQLite/.git leave read-only files; clear attributes before delete.
            Get-ChildItem -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue |
                ForEach-Object { try { $_.Attributes = 'Normal' } catch {} }
            Remove-Item -LiteralPath $path -Recurse -Force
            Write-Host "Removed: $path" -ForegroundColor Green
        }
        catch {
            Write-Warning "Could not fully remove $path : $($_.Exception.Message)"
        }
    }
    else {
        Write-Host "Not present: $path" -ForegroundColor DarkGray
    }
}

# --- Registry (the onboarding gate) -------------------------------------
if (Test-Path $registryPath) {
    Remove-Item -Path $registryPath -Recurse -Force
    Write-Host "Removed: $registryPath" -ForegroundColor Green
}
else {
    Write-Host "Not present: $registryPath" -ForegroundColor DarkGray
}

# --- LocalAppData -------------------------------------------------------
if ($KeepLogs -and (Test-Path $logsDir)) {
    # Remove everything under LocalData except Logs.
    Get-ChildItem -LiteralPath $localData -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'Logs' } |
        ForEach-Object { Remove-Tree $_.FullName }
}
else {
    Remove-Tree $localData
}

# --- Temp ---------------------------------------------------------------
Remove-Tree $tempData

Write-Host ""
Write-Host "Done. The next launch will start at onboarding." -ForegroundColor Green
