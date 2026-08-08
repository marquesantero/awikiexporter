# Packaging & Release

ExportAzureWiki ships a classic **Windows setup executable** as the recommended
desktop installer. The release workflow also publishes **MSIX** packages for
internal/sideload deployments that want MSIX identity and optional
`.appinstaller` update feeds.

> Why self-signed: Azure Trusted Signing's identity validation is not yet
> available to organizations in Brazil, and public CA certificates now
> require hardware/cloud HSM storage. For internal-only distribution a
> self-signed certificate trusted via Group Policy is the zero-cost,
> region-independent path. If you later distribute externally, swap the
> certificate for a public-CA / cloud-HSM one; only the signing inputs
> change, not the packaging.

## One-time: create the signing certificate

```powershell
pwsh ./tools/sign/New-SigningCert.ps1 -Subject "CN=Ti com Café, O=Ti com Café, C=BR"
```

This writes (under `artifacts/signing/`, gitignored):

- `signing.pfx` — **secret**, used to sign. Never commit it.
- `signing.cer` — **public**, deployed so machines trust the package.

It prints the exact **Publisher** subject and the base64 command for CI.
Keep the Subject stable across releases; changing it forces every machine
to re-trust the new certificate.

## Build a setup executable locally

Requires **Inno Setup 6**, the **Windows SDK** (`signtool.exe`) and the .NET SDK
from `global.json`.

```powershell
pwsh ./tools/package/Build-Installer.ps1 `
    -PfxPath ./artifacts/signing/signing.pfx `
    -PfxPassword (Read-Host -AsSecureString -Prompt 'PFX password')
```

Output:

- `artifacts/installer/AWikiExportSetup_<version>.exe`

Omit `-PfxPath` only for a local smoke build. Release installers must be signed.

## Build an MSIX locally

```powershell
pwsh ./tools/package/Build-Msix.ps1 `
    -Publisher "CN=Ti com Café, O=Ti com Café, C=BR" `
    -PublisherDisplayName "Ti com Café" `
    -PfxPath ./artifacts/signing/signing.pfx `
    -PfxPassword (Read-Host -AsSecureString -Prompt 'PFX password')
```

Output:

- `artifacts/msix/ExportAzureWiki_<version>_<flavor>.msix`
- `artifacts/msix/ExportAzureWiki-signing.cer` when `-PfxPath` is used

- `-Publisher` **must equal the certificate Subject exactly**, or signing
  (and install) fails.
- Omit `-PfxPath` only for a quick local smoke build. Release packages
  must be signed.
- Version defaults to `<Version>` in `Directory.Build.props`; bump it
  there for a release.

Requires the **Windows SDK** (`MakeAppx.exe`, `signtool.exe`) and the
.NET SDK from `global.json`.

### Two flavors: self-contained vs framework-dependent

`-SelfContained` controls whether the .NET runtime is bundled. Both
packages share the same Identity, so a machine installs **one or the
other**, not both.

| `-SelfContained` | File suffix      | Size    | Target machine needs                          |
| ---------------- | ---------------- | ------- | --------------------------------------------- |
| `$true` (default)| `_selfcontained` | ~150 MB | nothing — the runtime is inside the package   |
| `$false`         | `_fxdependent`   | ~10 MB  | .NET 8 Desktop Runtime installed (Intune/GPO) |

```powershell
# Small package for a managed park that already has the .NET 8 runtime:
pwsh ./tools/package/Build-Msix.ps1 -SelfContained $false -Publisher "CN=..." -PfxPath ...
```

The release workflow builds **both** on every tag, so you can choose per
deployment which one to push.

## Release via CI (tag-triggered)

Pushing a `vX.Y.Z` tag runs `.github/workflows/release.yml`, which builds,
tests, packages, signs, exports the public `.cer`, and attaches those files
to a GitHub Release. The setup executable is the recommended download; MSIX
files remain available for managed deployments.

Configure once in the repository settings:

| Kind   | Name                        | Value                                                    |
| ------ | --------------------------- | -------------------------------------------------------- |
| Secret | `SIGNING_PFX_BASE64`        | `[Convert]::ToBase64String([IO.File]::ReadAllBytes('artifacts/signing/signing.pfx'))` |
| Secret | `SIGNING_PFX_PASSWORD`      | the PFX password                                         |
| Secret | `SIGNING_PUBLISHER`         | the exact cert Subject, e.g. `CN=Ti com Café, O=Ti com Café, C=BR` |
| Var    | `SIGNING_PUBLISHER_DISPLAY` | friendly name, e.g. `Ti com Café`                        |

If any signing secret is absent, the workflow fails before packaging. A
GitHub Release must never publish unsigned Windows packages. The tag version
must match `Directory.Build.props` `<Version>`.

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## Deploying trust to client machines

For the signed MSIX to install without a prompt, each machine must trust
the certificate. Releases attach the public certificate as
`ExportAzureWiki-signing.cer`; locally it is generated beside the MSIX.
Deploy that `.cer` via GPO:

1. **Computer Configuration → Policies → Windows Settings → Security
   Settings → Public Key Policies → Trusted People** → import
   `ExportAzureWiki-signing.cer`. (MSIX sideloading checks Trusted People
   for the signer.)
2. Ensure sideloading/Developer-unlock is allowed: on managed devices,
   **Allow all trusted apps to install** (enabled by default on Windows 11;
   on older builds set the *Allow Trusted Apps* policy).

Do not import the certificate into the Current User store. App Installer checks
the Local Computer certificate stores when verifying package identity.

Manual install on a test machine, from an elevated PowerShell session:

```powershell
pwsh ./tools/sign/Install-MsixCertificate.ps1 `
    -CertificatePath ./ExportAzureWiki-signing.cer `
    -MsixPath ./ExportAzureWiki_1.0.0.0_selfcontained.msix
```

## Branding (optional)

The package currently reuses an existing icon for every tile. To brand
it, drop properly sized PNGs (Square150x150, Square44x44, Square71x71,
StoreLogo 50x50) under `build/msix/Images/` and point `Build-Msix.ps1`
at them instead of the placeholder source.

## Cutting a release (automated)

Releases are driven by a `vX.Y.Z` tag and the GitHub **Release** workflow:

1. Move the `## Unreleased` notes in `CHANGELOG.md` under a new
   `## vX.Y.Z - YYYY-MM-DD` heading (leave a fresh empty `## Unreleased`).
2. Commit, then tag and push:
   ```bash
   git tag v1.1.0
   git push origin v1.1.0
   ```
3. The workflow derives the version from the tag, runs the test gate, builds
   and signs the setup executable plus both MSIX flavors, and creates a GitHub
   Release whose notes are taken from the matching `CHANGELOG.md` section
   (`tools/release/Get-ChangelogSection.ps1`; falls back to `Unreleased`).

The setup version is the tag; the MSIX revision is the workflow run number.

## Auto-update (`.appinstaller`)

For hands-off updates on managed machines, host the package on an internal
feed and distribute an **`.appinstaller`** instead of the raw `.msix`:

1. Set the repository variable **`APPINSTALLER_BASE_URI`** to the base URL where
   the files will be served (e.g. `https://share.contoso.com/awiki`).
2. The release workflow then emits `ExportAzureWiki_<flavor>.appinstaller`
   alongside each `.msix` (also available locally via
   `Build-Msix.ps1 -AppInstallerUri <url>`).
3. Host both the `.msix` and the matching `.appinstaller` at that base URL and
   have users install the **`.appinstaller`** once.

The installed app then checks the feed **on launch** (default every 24h,
`-UpdateCheckHours`) and updates itself when a newer version is published to the
same URLs — no manual reinstall. The `.appinstaller` `Uri` and the package URLs
must be reachable from the client and match the hosted file names.

## Verification status

The scripts and manifests are authored to known-good Windows packaging
patterns, but the Inno Setup and `MakeAppx`/`signtool` packaging steps only run
on Windows with the required tooling. The first tagged release, or a local run
of `Build-Installer.ps1` / `Build-Msix.ps1`, is the real end-to-end validation.
If MSIX `signtool` reports a publisher mismatch, the `-Publisher` value does not
match the certificate Subject.
