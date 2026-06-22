param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "ExportAzureWiki.sln"
$appProject = Join-Path $root "ExportAzureWiki\ExportAzureWiki.csproj"
$setupProjectName = "AExportWikiSetup"
$vsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

Write-Host "1) Building app output ($Configuration)..."
dotnet build $appProject -c $Configuration -nologo /p:UseAppHost=true | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "App build failed (exit code $LASTEXITCODE)."
}

if (-not (Test-Path $vsWhere)) {
    throw "vswhere.exe not found. Install Visual Studio 2022 with 'Microsoft Visual Studio Installer Projects' extension."
}

$vsPath = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if ([string]::IsNullOrWhiteSpace($vsPath)) {
    throw "Visual Studio installation not found."
}

$devenv = Join-Path $vsPath "Common7\IDE\devenv.com"
if (-not (Test-Path $devenv)) {
    throw "devenv.com not found at '$devenv'. Install full Visual Studio (not only Build Tools/SSMS)."
}

Write-Host "2) Building setup project ($setupProjectName / $Configuration)..."
$buildStartedAt = Get-Date
& $devenv $solution /Build "$Configuration|Any CPU" /Project $setupProjectName | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Setup build failed (exit code $LASTEXITCODE)."
}

$outputDir = Join-Path $PSScriptRoot $Configuration
$msi = Join-Path $outputDir "AExportWikiSetup.msi"
$bootstrap = Join-Path $outputDir "setup.exe"

if (-not (Test-Path $msi)) {
    throw "Installer build finished but MSI not found at '$msi'."
}
if ((Get-Item $msi).LastWriteTime -lt $buildStartedAt.AddSeconds(-2)) {
    throw "MSI file was not updated in this build attempt. Check setup project load/build errors."
}

Write-Host ""
Write-Host "Installer ready:"
Write-Host "MSI: $msi"
if (Test-Path $bootstrap) {
    Write-Host "Bootstrapper: $bootstrap"
}
