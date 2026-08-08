<#
.SYNOPSIS
    Builds a classic Windows setup executable for AWikiExport.

.DESCRIPTION
    Publishes the WPF app as a win-x64 self-contained folder and packages it
    into a single Inno Setup .exe installer. Signing is optional for local smoke
    builds; CI passes the same PFX used by the MSIX release.

.PARAMETER Version
    SemVer (X.Y.Z). Defaults to the <Version> in Directory.Build.props.

.PARAMETER OutputDirectory
    Where the installer is written. Default: ./artifacts/installer.

.PARAMETER PfxPath / PfxPassword
    Optional code-signing certificate. When provided, signs the app executable
    before packaging and signs the final setup executable after compilation.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\installer'),
    [string] $PfxPath,
    [securestring] $PfxPassword,
    [bool] $SelfContained = $true
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$wpfProject = Join-Path $repoRoot 'ExportAzureWiki.Wpf\ExportAzureWiki.Wpf.csproj'
$installerScript = Join-Path $repoRoot 'build\installer\AWikiExport.iss'
$publishDir = Join-Path $OutputDirectory 'publish'
$setupBaseName = if ($Version) { "AWikiExportSetup_$Version" } else { 'AWikiExportSetup' }

function Find-Tool([string] $name, [string[]] $fallbackPaths) {
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($path in $fallbackPaths) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    throw "$name was not found. Install Inno Setup 6 or the Windows SDK tooling required by this script."
}

function Find-SdkTool([string] $name) {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kitsRoot)) {
        throw "Windows SDK not found at $kitsRoot."
    }

    $tool = Get-ChildItem -Path $kitsRoot -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $tool) {
        throw "$name not found under $kitsRoot. Install the Windows SDK."
    }

    return $tool.FullName
}

function Invoke-SignTool([string] $FilePath) {
    if (-not $PfxPath) {
        return
    }

    if (-not (Test-Path -LiteralPath $PfxPath)) {
        throw "PFX not found: $PfxPath"
    }

    $password = $PfxPassword
    if (-not $password) {
        $password = Read-Host -AsSecureString -Prompt 'PFX password'
    }

    $signTool = Find-SdkTool 'signtool.exe'
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
    try {
        $plainPwd = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        Write-Host "Signing $FilePath..." -ForegroundColor Cyan
        & $signTool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a /f $PfxPath /p $plainPwd $FilePath
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Timestamped signing failed for $FilePath. Retrying without timestamp."
            & $signTool sign /fd SHA256 /a /f $PfxPath /p $plainPwd $FilePath
            if ($LASTEXITCODE -ne 0) {
                throw "signtool failed for $FilePath."
            }
        }
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

if (-not $Version) {
    Write-Host "Reading <Version> from Directory.Build.props..." -ForegroundColor Cyan
    $Version = (dotnet msbuild $wpfProject -getProperty:Version -nologo).Trim()
    $setupBaseName = "AWikiExportSetup_$Version"
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$Version' must be SemVer X.Y.Z."
}

$iscc = Find-Tool 'ISCC.exe' @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)

Write-Host "Inno Setup compiler: $iscc" -ForegroundColor Cyan
Write-Host "Publishing WPF app (win-x64, self-contained=$SelfContained)..." -ForegroundColor Cyan

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -Recurse -Force -LiteralPath $OutputDirectory
}

$null = New-Item -ItemType Directory -Force -Path $publishDir

dotnet publish $wpfProject `
    -c Release -r win-x64 --self-contained $($SelfContained.ToString().ToLowerInvariant()) `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$appExe = Join-Path $publishDir 'ExportAzureWiki.Wpf.exe'
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Published app executable not found: $appExe"
}

$nativeGit = Get-ChildItem -Path $publishDir -Recurse -Filter 'git2-*.dll' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $nativeGit) {
    throw "Native libgit2 (git2-*.dll) is missing from the publish output ($publishDir)."
}

$cleanupPatterns = @('*.pdb', '*.ps1', '*.sh', '*.ts', '*.md', '*.gitkeep', '*.yml')
foreach ($pattern in $cleanupPatterns) {
    Get-ChildItem -Path $publishDir -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

Invoke-SignTool $appExe

$absoluteOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$absolutePublish = (Resolve-Path -LiteralPath $publishDir).Path

Write-Host "Compiling installer..." -ForegroundColor Cyan
& $iscc `
    "/DAppVersion=$Version" `
    "/DSourceDir=$absolutePublish" `
    "/DOutputDir=$absoluteOutput" `
    "/DOutputBaseFilename=$setupBaseName" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed."
}

$setupPath = Join-Path $OutputDirectory "$setupBaseName.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Installer was not produced: $setupPath"
}

Invoke-SignTool $setupPath

Write-Host ""
Write-Host "Installer ready: $setupPath" -ForegroundColor Green
