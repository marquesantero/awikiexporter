<#
.SYNOPSIS
    Builds (and optionally signs) an MSIX package for the ExportAzureWiki
    WPF app: dotnet publish -> stage -> MakeAppx -> signtool.

.DESCRIPTION
    Windows-only (uses the Windows SDK MakeAppx/signtool and a desktop-
    bridge full-trust manifest). Produces a self-contained win-x64 package
    so target machines do not need the .NET Desktop Runtime installed.

    Signing is optional: omit -PfxPath to produce an UNSIGNED .msix (useful
    for a quick local smoke build). An unsigned package cannot be installed
    until it is signed by a certificate the machine trusts.

.PARAMETER Version
    SemVer (X.Y.Z). Defaults to the <Version> in Directory.Build.props.
    Mapped onto the 4-part MSIX version as X.Y.Z.<Revision>.

.PARAMETER Revision
    The 4th version component (default 0). CI can pass the run number.

.PARAMETER Publisher
    MUST match the signing certificate Subject EXACTLY, e.g.
    "CN=Ti com Café, O=Ti com Café, C=BR". New-SigningCert.ps1 prints it.

.PARAMETER PublisherDisplayName
    Friendly publisher name shown to users.

.PARAMETER PfxPath / -PfxPassword
    Code-signing certificate. When omitted, the package is left unsigned.
    When present, the public .cer is exported beside the .msix so target
    machines can trust the MSIX signer.

.PARAMETER OutputDirectory
    Where the .msix is written. Default: ./artifacts/msix (gitignored).

.EXAMPLE
    pwsh ./tools/package/Build-Msix.ps1 `
        -Publisher "CN=Ti com Café, O=Ti com Café, C=BR" `
        -PublisherDisplayName "Ti com Café" `
        -PfxPath ./artifacts/signing/signing.pfx -PfxPassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [string] $Version,
    [int] $Revision = 0,
    [string] $Publisher = 'CN=ExportAzureWiki Dev',
    [string] $PublisherDisplayName = 'ExportAzureWiki',
    [string] $PfxPath,
    [securestring] $PfxPassword,

    # $true  = self-contained (bundles the .NET runtime; ~150 MB; target
    #          machines need nothing pre-installed).
    # $false = framework-dependent (~small; target machines must have the
    #          .NET 8 Desktop Runtime, e.g. deployed via Intune/GPO).
    [bool] $SelfContained = $true,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\msix'),

    # Base URL where the .msix and .appinstaller will be hosted (internal feed,
    # e.g. https://share.contoso.com/awiki). When set, a .appinstaller manifest
    # is emitted that enables auto-update on launch. Omit to skip it.
    [string] $AppInstallerUri,

    # How often (hours) the installed app checks the feed for updates.
    [int] $UpdateCheckHours = 24
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$wpfProject = Join-Path $repoRoot 'ExportAzureWiki.Wpf\ExportAzureWiki.Wpf.csproj'
$manifestTemplate = Join-Path $repoRoot 'build\msix\Package.appxmanifest'

# --- Resolve version ----------------------------------------------------
if (-not $Version) {
    Write-Host "Reading <Version> from Directory.Build.props..." -ForegroundColor Cyan
    $Version = (dotnet msbuild $wpfProject -getProperty:Version -nologo).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$Version' must be SemVer X.Y.Z."
}
$msixVersion = "$Version.$Revision"
Write-Host "Package version: $msixVersion" -ForegroundColor Cyan

# --- Locate Windows SDK tools -------------------------------------------
function Find-SdkTool([string] $name) {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path $kitsRoot)) { throw "Windows SDK not found at $kitsRoot." }
    $tool = Get-ChildItem -Path $kitsRoot -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $tool) { throw "$name not found under $kitsRoot. Install the Windows SDK." }
    return $tool.FullName
}

$makeAppx = Find-SdkTool 'makeappx.exe'
$signTool = Find-SdkTool 'signtool.exe'
Write-Host "MakeAppx: $makeAppx"
Write-Host "SignTool: $signTool"

# --- Publish ------------------------------------------------------------
$stageRoot = Join-Path $OutputDirectory 'stage'
$publishDir = Join-Path $stageRoot 'app'
if (Test-Path $stageRoot) { Remove-Item -Recurse -Force $stageRoot }
$null = New-Item -ItemType Directory -Force -Path $publishDir

$flavor = if ($SelfContained) { 'selfcontained' } else { 'fxdependent' }
Write-Host "Publishing WPF app (win-x64, $flavor)..." -ForegroundColor Cyan
dotnet publish $wpfProject `
    -c Release -r win-x64 --self-contained $($SelfContained.ToString().ToLowerInvariant()) `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# --- Verify native dependencies -----------------------------------------
# LibGit2Sharp loads a native git2-*.dll at runtime; without it the GitHub
# Wiki/clone mode crashes on the INSTALLED app (it builds fine in the IDE).
# Fail the package build rather than ship a binary that crashes on a feature.
$nativeGit = Get-ChildItem -Path $publishDir -Recurse -Filter 'git2-*.dll' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $nativeGit) {
    throw "Native libgit2 (git2-*.dll) is missing from the publish output ($publishDir). " +
          "The GitHub Wiki/clone feature would crash at runtime. Check the LibGit2Sharp package and the win-x64 RID."
}
Write-Host "Native libgit2 present: $($nativeGit.Name)" -ForegroundColor Green

# --- Logos --------------------------------------------------------------
# Branded, correctly-sized tile PNGs live under build/msix/Images (square,
# transparent, generated from the app logo). Copy them into the package.
$brandImages = Join-Path $repoRoot 'build\msix\Images'
$imagesDir = Join-Path $publishDir 'Images'
$null = New-Item -ItemType Directory -Force -Path $imagesDir
foreach ($logo in @('StoreLogo.png','Square150x150Logo.png','Square44x44Logo.png','Square71x71Logo.png')) {
    $tile = Join-Path $brandImages $logo
    if (-not (Test-Path $tile)) { throw "Missing tile image: $tile" }
    Copy-Item $tile (Join-Path $imagesDir $logo) -Force
}

# --- Manifest -----------------------------------------------------------
Write-Host "Filling manifest (Publisher: $Publisher)..." -ForegroundColor Cyan
$manifest = Get-Content $manifestTemplate -Raw
$manifest = $manifest.Replace('{VERSION}', $msixVersion)
$manifest = $manifest.Replace('{PUBLISHER}', $Publisher)
$manifest = $manifest.Replace('{PUBLISHER_DISPLAY}', $PublisherDisplayName)
Set-Content -Path (Join-Path $publishDir 'AppxManifest.xml') -Value $manifest -Encoding UTF8

# --- Pack ---------------------------------------------------------------
$msixName = "ExportAzureWiki_${msixVersion}_$flavor.msix"
$msixPath = Join-Path $OutputDirectory $msixName
Write-Host "Packing $msixName..." -ForegroundColor Cyan
& $makeAppx pack /d $publishDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed." }

# --- Sign ---------------------------------------------------------------
if ($PfxPath) {
    if (-not (Test-Path $PfxPath)) { throw "PFX not found: $PfxPath" }
    if (-not $PfxPassword) { $PfxPassword = Read-Host -AsSecureString -Prompt 'PFX password' }
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword)
    try {
        $plainPwd = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)

        Write-Host "Signing $msixName..." -ForegroundColor Cyan
        & $signTool sign /fd SHA256 /a /f $PfxPath /p $plainPwd $msixPath
        if ($LASTEXITCODE -ne 0) { throw "signtool failed (does Publisher match the cert Subject?)." }
        Write-Host "Signed." -ForegroundColor Green

        $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($PfxPath, $plainPwd)
        $cerPath = Join-Path $OutputDirectory 'ExportAzureWiki-signing.cer'
        [IO.File]::WriteAllBytes(
            $cerPath,
            $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Write-Host "Public signing certificate ready: $cerPath" -ForegroundColor Green
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}
else {
    Write-Warning "No -PfxPath given: package is UNSIGNED and cannot be installed until signed."
}

Write-Host ""
Write-Host "MSIX ready: $msixPath" -ForegroundColor Green

# --- .appinstaller (auto-update) ----------------------------------------
# Emits a sideload auto-update manifest. Users install the .appinstaller once;
# the app then checks the feed on launch and updates itself when a newer
# version is published to the same URLs.
if ($AppInstallerUri) {
    $base = $AppInstallerUri.TrimEnd('/')
    if ($manifest -notmatch 'Name="([^"]+)"') { throw "Could not read Identity Name from the manifest." }
    $identityName = $Matches[1]
    $appInstallerName = "ExportAzureWiki_$flavor.appinstaller"
    $appInstallerPath = Join-Path $OutputDirectory $appInstallerName

    $appInstallerXml = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2018"
    Version="$msixVersion"
    Uri="$base/$appInstallerName">
  <MainPackage
      Name="$identityName"
      Publisher="$Publisher"
      Version="$msixVersion"
      ProcessorArchitecture="x64"
      Uri="$base/$msixName" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="$UpdateCheckHours" />
  </UpdateSettings>
</AppInstaller>
"@

    Set-Content -Path $appInstallerPath -Value $appInstallerXml -Encoding UTF8
    Write-Host "AppInstaller ready: $appInstallerPath" -ForegroundColor Green
    Write-Host "  Host $msixName and $appInstallerName at $base/ ; users install the .appinstaller." -ForegroundColor DarkGray
}
