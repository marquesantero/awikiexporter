# Contributing

ExportAzureWiki is a Windows desktop application built on .NET 8 with a WPF shell, a shared Core contract layer, and a Platform implementation layer.

## Local Setup

1. Install Windows 10/11, Visual Studio 2022 or newer, .NET SDK 9.x, and WebView2 Runtime.
2. Restore packages:

```powershell
dotnet restore .\ExportAzureWiki.sln
```

3. Build the solution:

```powershell
dotnet build .\ExportAzureWiki.sln -c Release
```

4. Run the WPF application:

```powershell
dotnet run --project .\ExportAzureWiki.Wpf\ExportAzureWiki.Wpf.csproj
```

## Project Layout

- `ExportAzureWiki.Core/`: shared models and application service contracts.
- `ExportAzureWiki.Platform/`: data access, provider adapters, export engines, authentication, rendering, and infrastructure.
- `ExportAzureWiki.Wpf/`: desktop UI, view models, dialogs, help assets, and bundled export-rendering assets.
- `ExportAzureWiki.CLI/`: command-line facade over the same platform services used by the desktop app.
- `docs/`: guides, reports, upgrade logs, and project analysis.

## Contribution Rules

- Do not add hardcoded UI text. Add localization keys in `LocalizationManager` for Portuguese and English.
- Keep WPF UI behavior in view models where practical; avoid putting workflow logic in code-behind.
- Keep provider-specific code behind Core contracts or Platform adapters.
- Do not commit generated build output, installer binaries, local cache, logs, secrets, or machine-specific files.
- Validate with `dotnet build .\ExportAzureWiki.sln -c Release` before opening a pull request.

## Pull Request Checklist

- Build passes locally.
- New UI strings are localized in PT and EN.
- README, guides, or reports are updated when behavior changes.
- Security-sensitive data such as PATs, OAuth secrets, and API keys are not logged or committed.
