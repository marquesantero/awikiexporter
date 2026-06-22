<#
.SYNOPSIS
    Generates a self-signed code-signing certificate for signing the
    ExportAzureWiki MSIX package, and exports it as .pfx (to sign with)
    and .cer (to distribute so machines trust the package).

.DESCRIPTION
    For INTERNAL distribution: machines that trust the exported .cer
    (deployed via Group Policy to Trusted People + Trusted Root, or
    imported manually) can install the signed MSIX without a SmartScreen
    or "untrusted publisher" prompt.

    The certificate Subject printed at the end MUST be used verbatim as
    the MSIX manifest Identity/@Publisher (Build-Msix.ps1 -Publisher).

.PARAMETER Subject
    The X.500 subject. Keep it stable across releases; changing it forces
    every machine to re-trust the new certificate.

.PARAMETER OutputDirectory
    Where to write signing.pfx and signing.cer. Default: ./artifacts/signing
    (gitignored).

.PARAMETER Password
    Password protecting the exported .pfx. If omitted, you are prompted.

.PARAMETER ValidYears
    Certificate validity in years (default 3).

.EXAMPLE
    pwsh ./tools/sign/New-SigningCert.ps1 -Subject "CN=Ti com Café, O=Ti com Café, C=BR"

.NOTES
    The .pfx is a secret: never commit it. For CI, base64-encode it into
    the SIGNING_PFX_BASE64 GitHub secret (see docs/operations/PACKAGING.md).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Subject,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\signing'),

    [securestring] $Password,

    [int] $ValidYears = 3
)

$ErrorActionPreference = 'Stop'

if (-not $Password) {
    $Password = Read-Host -AsSecureString -Prompt 'Password to protect the exported .pfx'
}

$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

Write-Host "Creating self-signed code-signing certificate..." -ForegroundColor Cyan
Write-Host "  Subject: $Subject"

# Code Signing EKU = 1.3.6.1.5.5.7.3.3
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -FriendlyName 'ExportAzureWiki MSIX Signing' `
    -NotAfter (Get-Date).AddYears($ValidYears) `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')

$pfxPath = Join-Path $OutputDirectory 'signing.pfx'
$cerPath = Join-Path $OutputDirectory 'signing.cer'

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $Password | Out-Null
Export-Certificate    -Cert $cert -FilePath $cerPath | Out-Null

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  PFX (secret, do NOT commit): $pfxPath"
Write-Host "  CER (public, deploy to trust): $cerPath"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host ""
Write-Host "Use this EXACT value as the MSIX Publisher:" -ForegroundColor Yellow
Write-Host "  $($cert.Subject)"
Write-Host ""
Write-Host "For CI, base64-encode the PFX into the SIGNING_PFX_BASE64 secret:" -ForegroundColor Cyan
Write-Host "  [Convert]::ToBase64String([IO.File]::ReadAllBytes('$pfxPath')) | Set-Clipboard"
