param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$vendorRoot = Join-Path $root "vendor"
$mermaidDir = Join-Path $vendorRoot "mermaid"
$mathjaxDir = Join-Path $vendorRoot "mathjax"

New-Item -ItemType Directory -Path $mermaidDir -Force | Out-Null
New-Item -ItemType Directory -Path $mathjaxDir -Force | Out-Null

$targets = @(
    @{
        Url = "https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js"
        OutFile = Join-Path $mermaidDir "mermaid.min.js"
    },
    @{
        Url = "https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-svg.js"
        OutFile = Join-Path $mathjaxDir "tex-svg.js"
    }
)

foreach ($t in $targets) {
    if ((-not $Force) -and (Test-Path $t.OutFile)) {
        Write-Host "Skip (already exists): $($t.OutFile)"
        continue
    }

    Write-Host "Downloading: $($t.Url)"
    Invoke-WebRequest -Uri $t.Url -OutFile $t.OutFile -UseBasicParsing
    Write-Host "Saved: $($t.OutFile)"
}

Write-Host ""
Write-Host "Done. Vendor assets are ready in: $vendorRoot"
