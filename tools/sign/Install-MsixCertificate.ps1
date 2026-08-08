<#
.SYNOPSIS
    Trusts the public certificate used to sign the ExportAzureWiki MSIX.

.DESCRIPTION
    Import the public .cer into the Local Machine Trusted People store so
    Windows App Installer can validate a self-signed MSIX package on this
    machine. This script is for internal/test distribution. It never imports or
    handles the private .pfx.

.PARAMETER CertificatePath
    Path to ExportAzureWiki-signing.cer or signing.cer.

.PARAMETER MsixPath
    Optional MSIX path to install after trusting the certificate.

.EXAMPLE
    pwsh ./tools/sign/Install-MsixCertificate.ps1 `
        -CertificatePath ./ExportAzureWiki-signing.cer `
        -MsixPath ./ExportAzureWiki_1.1.2.1_selfcontained.msix
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CertificatePath,

    [string] $MsixPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CertificatePath)) {
    throw "Certificate not found: $CertificatePath"
}

$resolvedCertificate = (Resolve-Path -LiteralPath $CertificatePath).Path
$trustedPeople = 'Cert:\LocalMachine\TrustedPeople'
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($resolvedCertificate)

Write-Host "Trusting MSIX signing certificate..." -ForegroundColor Cyan
Write-Host "  Certificate: $resolvedCertificate"
Write-Host "  Thumbprint:  $($certificate.Thumbprint)"
Write-Host "  Store:       $trustedPeople"

Import-Certificate -FilePath $resolvedCertificate -CertStoreLocation $trustedPeople | Out-Null

$installed = Get-ChildItem -Path $trustedPeople |
    Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
    Select-Object -First 1

if (-not $installed) {
    throw "Certificate import did not complete: $($certificate.Thumbprint)"
}

Write-Host "Certificate imported into Local Machine Trusted People." -ForegroundColor Green

if ($MsixPath) {
    if (-not (Test-Path -LiteralPath $MsixPath)) {
        throw "MSIX not found: $MsixPath"
    }

    $resolvedMsix = (Resolve-Path -LiteralPath $MsixPath).Path
    $signature = Get-AuthenticodeSignature -FilePath $resolvedMsix
    if ($signature.SignerCertificate?.Thumbprint -ne $certificate.Thumbprint) {
        throw "The MSIX signer does not match the imported certificate. MSIX signer: $($signature.SignerCertificate?.Thumbprint); imported certificate: $($certificate.Thumbprint)"
    }

    Write-Host "Installing MSIX: $resolvedMsix" -ForegroundColor Cyan
    Add-AppxPackage -Path $resolvedMsix
    Write-Host "MSIX installed." -ForegroundColor Green
}
