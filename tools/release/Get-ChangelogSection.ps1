<#
.SYNOPSIS
    Extracts the changelog section for a release version (for GitHub Release notes).

.DESCRIPTION
    Returns the body of the "## v<Version>" section from CHANGELOG.md. When that
    heading does not exist yet (e.g. notes still live under "Unreleased"), falls
    back to the "## Unreleased" section. Output goes to stdout.

.PARAMETER Version
    Release version without the leading 'v' (e.g. 1.2.3).

.PARAMETER ChangelogPath
    Path to the changelog file. Defaults to CHANGELOG.md in the repo root.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$ChangelogPath = (Join-Path $PSScriptRoot '..\..\CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ChangelogPath)) {
    return ''
}

$lines = Get-Content -LiteralPath $ChangelogPath
$escaped = [regex]::Escape($Version)

$startIdx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^##\s+v?$escaped(\s|$|-)") { $startIdx = $i; break }
}

if ($startIdx -lt 0) {
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^##\s+Unreleased\s*$') { $startIdx = $i; break }
    }
}

if ($startIdx -lt 0) {
    return ''
}

$body = New-Object System.Collections.Generic.List[string]
for ($i = $startIdx + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+') { break }
    $body.Add($lines[$i])
}

($body -join "`n").Trim()
