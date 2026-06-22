# ExportAzureWiki

[![CI](https://github.com/marquesantero/awikiexporter/actions/workflows/ci.yml/badge.svg)](https://github.com/marquesantero/awikiexporter/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/marquesantero/awikiexporter?label=release)](https://github.com/marquesantero/awikiexporter/releases)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4)](https://learn.microsoft.com/windows/)
[![Status](https://img.shields.io/badge/status-released-1b7f4c)](CHANGELOG.md)
[![Tests](https://img.shields.io/badge/tests-309%20passing-1b7f4c)](ExportAzureWiki.Tests)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

A Windows desktop application built with .NET for reading, browsing, rendering, and exporting wiki content to Word, PDF, and HTML with corporate authentication and permission controls.

## Highlights

- Modern WPF desktop shell with a Core, Platform, and UI architecture.
- High-fidelity export to Word, PDF, and HTML, with an **offline export** mode
  that makes no network calls.
- Rich rendering for Markdown, tables, code blocks, images, math formulas, and
  **locally-rendered Mermaid diagrams** (nothing leaves the machine).
- Wiki sources: **Azure DevOps**, **GitHub**, and **GitLab** (repository docs
  folders or wiki repositories), plus local **folders** and **`.zip`** archives.
- **AI assistant** over the loaded pages — summary, index, quiz, and grounded
  Q&A with citations — across pluggable providers (OpenAI, Azure, Anthropic,
  Gemini, local Ollama/LM Studio, and more).
- **Content cache encrypted at rest** (source Markdown, rendered HTML, images)
  under a single app-owned root, on top of secret encryption and a strict CSP.
- Administrative workflows for first-run setup, users, groups, OAuth providers,
  wiki connections, and access policies.
- Centralized PT/EN localization through `LocalizationManager`.
- CLI built on the same service contracts used by the desktop app.

## Table Of Contents

- [Quick Start](#quick-start)
- [Requirements](#requirements)
- [Architecture](#architecture)
- [Features](#features)
- [Export Pipeline](#export-pipeline)
- [Authentication And Permissions](#authentication-and-permissions)
- [CLI](#cli)
- [Development](#development)
- [Contributing](#contributing)
- [Reports](#reports)
- [License](#license)

## Quick Start

```powershell
git clone https://github.com/marquesantero/awikiexporter.git
cd awikiexporter
dotnet restore .\ExportAzureWiki.sln
dotnet build .\ExportAzureWiki.sln -c Release
dotnet run --project .\ExportAzureWiki.Wpf\ExportAzureWiki.Wpf.csproj
```

Validate the CLI:

```powershell
dotnet run --project .\ExportAzureWiki.CLI\ExportAzureWiki.CLI.csproj -- --help
```

## Requirements

| Requirement | Version / Notes |
| --- | --- |
| Operating system | Windows 10/11 |
| SDK | .NET SDK 9.x, with application projects targeting .NET 8 |
| Desktop runtime | Windows Desktop Runtime / WPF |
| WebView | Microsoft WebView2 Runtime |
| Database | SQLite, SQL Server, PostgreSQL, or MySQL |

## Architecture

```text
ExportAzureWiki.Core
  Models and service contracts

ExportAzureWiki.Platform
  Data access, wiki providers, auth, rendering, export engines

ExportAzureWiki.Wpf
  WPF shell, views, view models, dialogs, help, bundled render assets

ExportAzureWiki.CLI
  Command-line facade over PlatformHost and Core contracts
```

| Project | Responsibility |
| --- | --- |
| `ExportAzureWiki.Core/` | Shared models, application service contracts, and UI-independent rules. |
| `ExportAzureWiki.Platform/` | Infrastructure, wiki providers, persistence, authentication, authorization, rendering, and export engines. |
| `ExportAzureWiki.Wpf/` | Primary desktop app, views, view models, dialogs, help files, icons, and rendering assets. |
| `ExportAzureWiki.CLI/` | Command-line interface for configuration and export workflows. |
| `AExportWikiSetup/`, `WikiExporterInstall/` | Installer project sources. Generated installer outputs should not be committed. |

## Features

| Area | Capabilities |
| --- | --- |
| Wikis | Connection management, browsing, encrypted local cache, and start-point selection. |
| Sources | Azure DevOps Wiki; GitHub and GitLab in Repository or Wiki mode; local Markdown folders and `.zip` archives. |
| GitHub / GitLab | Repository Markdown via Git Trees/raw content, and wiki repositories via `*.wiki.git` clone with default-branch detection. |
| Export | Word, PDF, and HTML output with preprocessing, asset handling, and an offline-only mode. |
| AI | Summary, index, quiz, and grounded Q&A with citations over the loaded pages, across pluggable OpenAI-compatible providers (local or cloud). |
| Administration | Users, groups, permissions, OAuth providers, AI providers, and wiki connection management. |
| Authentication | Local auth, Windows/AD, Azure AD, and configurable OAuth providers. |
| Localization | Centralized keys with Portuguese and English support. |
| Diagnostics | Operational logs, crash bundles, and project reports for troubleshooting. |

## Export Pipeline

### Word

- HTML preprocessing for OpenXML compatibility.
- Local image and `data-uri` handling.
- Code rendering with the active theme.
- Table handling for corporate document layouts.
- Automatic table of contents support and pagination rules.
- Optional final document fine-tuning.

### PDF

- Dedicated export CSS pipeline.
- SVG and rendered asset sanitization.
- Pagination and readability adjustments.

### HTML

- Reusable intermediate output for auditing, preview, and external pipelines.

## Authentication And Permissions

ExportAzureWiki is designed for corporate usage:

- Local username/password authentication with PBKDF2-SHA256 hashing,
  a configurable password policy, and login lockout.
- Windows/AD and Azure AD integrations when enabled. Azure AD uses MSAL
  with PKCE and a DPAPI-protected token cache.
- OAuth providers such as Microsoft, Google, and GitHub when configured in the database.
- Access policies by user, group, role, and wiki, resolved by a
  unit-tested permission matrix.
- Export permissions for Word, PDF, and letterhead usage.
- A security audit log of authentication and permission events.

## Security

Secrets **and the on-disk content cache** (source Markdown, rendered HTML, and
downloaded images) are encrypted at rest with AES-GCM (DPAPI-protected master
key), database connections require TLS by default, wiki HTML is sanitized and
served under a strict Content-Security-Policy, and credentials are masked
in logs. The full threat model, cryptographic primitives, and known
limitations are documented in
[`docs/operations/SECURITY.md`](docs/operations/SECURITY.md). Report
vulnerabilities per [`SECURITY.md`](SECURITY.md).

## CLI

Export example:

```powershell
dotnet run --project .\ExportAzureWiki.CLI\ExportAzureWiki.CLI.csproj -- export `
  --organization "https://dev.azure.com/myorg" `
  --token "<PAT>" `
  --project "MyProject" `
  --wiki "MyWiki" `
  --format docx `
  --output ".\out\wiki.docx"
```

Save configuration:

```powershell
dotnet run --project .\ExportAzureWiki.CLI\ExportAzureWiki.CLI.csproj -- config `
  --organization "https://dev.azure.com/myorg" `
  --token "<PAT>" `
  --project "MyProject" `
  --wiki "MyWiki"
```

## Development

Full build:

```powershell
dotnet restore .\ExportAzureWiki.sln
dotnet build .\ExportAzureWiki.sln -c Release --no-restore
```

Run the desktop app:

```powershell
dotnet run --project .\ExportAzureWiki.Wpf\ExportAzureWiki.Wpf.csproj
```

### Localization Rule

- Do not add hardcoded UI text for screens, menus, labels, buttons, tooltips, or messages.
- Every UI string must use a `LocalizationManager` key.
- Every new key must support at least Portuguese and English.
- UI work is not complete until both languages are validated.

### Repository Hygiene

Do not commit:

- `bin/`, `obj/`, `out/`, `artifacts/`
- `.vs/`, `.user` files, local logs, and local caches
- secrets, PATs, OAuth client secrets, or `.env` files
- generated installer executables
- `node_modules/`

## Contributing

Recommended flow:

1. Create a feature branch from `main`.
2. Keep changes small, reviewable, and aligned with the architecture.
3. Validate `dotnet build .\ExportAzureWiki.sln -c Release`.
4. Update documentation when behavior, setup, or operations change.
5. Open a pull request with technical context and test steps.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full contribution rules.

## Operations

- [Installation guide](docs/operations/INSTALL.md)
- [Configuration reference](docs/operations/CONFIGURATION.md)
- [Security model](docs/operations/SECURITY.md)
- [Operations runbook](docs/operations/RUNBOOK.md)
- [Packaging & release (MSIX)](docs/operations/PACKAGING.md)

## Reports

- [Project analysis 2026-06-03](docs/reports/project-analysis-2026-06-03.html)
- [Changelog](CHANGELOG.md)
- [Authentication setup](docs/guides/AUTHENTICATION_SETUP.md)
- [Quick start guide](docs/guides/QUICK_START_GUIDE.md)

## Roadmap

Completed in the current hardening track (see [CHANGELOG](CHANGELOG.md)):

- Security baseline: AES-GCM at rest, strict TLS, HTML sanitization +
  CSP, session rotation, password policy, lockout, audit log, MSAL DPAPI
  cache.
- Security-focused automated test suite and CI gates (build, tests,
  vulnerable-package scan, SBOM).
- Dependency injection, journaled schema migrations, operational docs.

Planned:

- Database integration tests with Testcontainers.
- Migrate the export pipeline off `WebView2.WinForms` to remove the last
  WinForms dependency in Platform.
- Signed MSIX packaging and release provenance (SLSA).
- Opt-in telemetry.
- Continue improving Word/PDF visual fidelity.

## License

Licensed under the [Apache License, Version 2.0](LICENSE). Contributions are accepted under the same terms.
