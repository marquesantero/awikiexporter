<#
.SYNOPSIS
    Trusts the public certificate used to sign the ExportAzureWiki MSIX.

.DESCRIPTION
    Import the public .cer into Trusted People and Trusted Root so Windows can
    validate a self-signed MSIX package on this machine. This script is for
    internal/test distribution. It never imports or handles the private .pfx.

.PARAMETER CertificatePath
    Path to ExportAzureWiki-signing.cer or signing.cer.

.PARAMETER Scope
    CurrentUser does not require elevation and is best for single-user testing.
    LocalMachine requires an elevated PowerShell session and is appropriate for
    managed workstations or admin-driven installs.

.PARAMETER MsixPath
    Optional MSIX path to install after trusting the certificate.

.EXAMPLE
    pwsh ./tools/sign/Install-MsixCertificate.ps1 `
        -CertificatePath ./ExportAzureWiki-signing.cer `
        -MsixPath ./ExportAzureWiki_1.1.0.2_selfcontained.msix

.EXAMPLE
    pwsh ./tools/sign/Install-MsixCertificate.ps1 `
        -CertificatePath ./ExportAzureWiki-signing.cer `
        -Scope LocalMachine
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CertificatePath,

    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string] $Scope = 'CurrentUser',

    [string] $MsixPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CertificatePath)) {
    throw "Certificate not found: $CertificatePath"
}

$resolvedCertificate = (Resolve-Path -LiteralPath $CertificatePath).Path
$trustedPeople = "Cert:\$Scope\TrustedPeople"
$trustedRoot = "Cert:\$Scope\Root"

Write-Host "Trusting MSIX signing certificate..." -ForegroundColor Cyan
Write-Host "  Certificate: $resolvedCertificate"
Write-Host "  Scope:       $Scope"

Import-Certificate -FilePath $resolvedCertificate -CertStoreLocation $trustedPeople | Out-Null
Import-Certificate -FilePath $resolvedCertificate -CertStoreLocation $trustedRoot | Out-Null

Write-Host "Certificate imported into Trusted People and Trusted Root." -ForegroundColor Green

if ($MsixPath) {
    if (-not (Test-Path -LiteralPath $MsixPath)) {
        throw "MSIX not found: $MsixPath"
    }

    $resolvedMsix = (Resolve-Path -LiteralPath $MsixPath).Path
    Write-Host "Installing MSIX: $resolvedMsix" -ForegroundColor Cyan
    Add-AppxPackage -Path $resolvedMsix
    Write-Host "MSIX installed." -ForegroundColor Green
}
