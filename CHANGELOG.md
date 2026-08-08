# Changelog

This changelog describes application behavior and user-visible capabilities.
Internal repository maintenance and release-process decisions are intentionally
kept out of the product notes.

## Unreleased

## v1.1.1 - 2026-08-08

### Fixed

- PDF export now uses the WebView2 print layout exclusively, matching the
  on-screen rendered document more closely and avoiding the broken code-block
  formatting produced by the previous rendered-PDF path.
- Word export no longer exposes the legacy fine-tuning option; the default
  export path now keeps the generated document closer to the rendered Markdown.
- Release MSIX packages now include the public signing certificate so a new
  workstation can trust and install the package without a certificate mismatch.

## v1.1.0 - 2026-06-21

### Added

- Open a local **folder** of Markdown (recursive) or a **`.zip`** archive in the
  workspace, listed in a tree; pages render on demand for instant opening of
  large folders.
- Separate **Online** (saved wikis) and **Local** (files/folders) tabs in the
  workspace, each keeping its own loaded set for AI, options and export.
- **Offline export** option: Word/PDF use only cached images and make no network
  calls.
- Filter box to search pages in both workspace trees.
- **GitHub** repository Markdown export: browse and render `.md` /
  `.markdown` files from the repository root or a configured docs folder, plus
  GitHub Wiki export (clone `owner/repo.wiki.git`, list and render Markdown
  pages) through the same Word/PDF/HTML pipeline.
- **GitLab** connector with the same Repo and Wiki source modes as GitHub,
  including default-branch auto-detection.
- **AI assistant over the loaded pages**: generate a summary, an index or a
  quiz, and **ask grounded questions** that are answered only from the loaded
  pages with citations to the source page(s). Works per tab and over the current
  page or all loaded pages.
- **Pluggable AI providers**: ~16 editable presets (OpenAI, Azure OpenAI,
  Anthropic, Gemini, Mistral, Groq, OpenRouter, DeepSeek, Together, Fireworks,
  xAI, Perplexity, Cohere, local Ollama/LM Studio, and a Custom
  OpenAI-compatible slot), dynamic model discovery, and a no-cost
  "Test connection". Local servers need no API key.
- CLI `diagnose` command that creates a support bundle with recent logs,
  runtime information, and audit context while excluding secrets.
- Operational help for installation, configuration, security, packaging,
  and support runbooks.

### Improved

- Markdown rendering now handles YAML front matter, GitHub alert callouts
  (note/tip/warning/important/caution), emoji shortcodes, task-list checkboxes
  and `<details>` in Word, and rasterizes SVG/WebP images for Word export.
- **Mermaid diagrams render locally** for Word/PDF export — nothing is sent to
  an external service, and export works offline.
- The workspace remembers your last wiki, code theme, dark mode, active tab and
  offline-export choice between sessions.
- Clear startup warning when the WebView2 runtime is missing; unexpected errors
  are logged and shown instead of crashing silently.
- GitHub rendering now uses the source-agnostic Markdown pipeline, so
  non-Azure pages can flow into the same export path as Azure DevOps wiki
  pages.
- Saved credentials, sessions, PATs, and OAuth client secrets are
  encrypted at rest with authenticated encryption.
- **The on-disk content cache is encrypted at rest** — source Markdown, rendered
  HTML, and downloaded images — under a single app-owned cache root (never the
  shared system TEMP folder). A cache copied off the machine, or read by another
  Windows user, is unusable. WebView2 runs under that managed root, and deleting
  a wiki purges its cached pages, images, and source so content does not outlive
  the access it came from.
- Database connections prefer strict TLS by default, with documented
  opt-in behavior for lab environments.
- Wiki HTML is sanitized before preview/export and served under a strict
  Content-Security-Policy.
- Login sessions rotate on sign-in, support idle timeout, and enforce
  configurable lockout and password rules.
- Azure AD token cache is protected with DPAPI so users can restart the
  app without exposing refresh tokens to other Windows users.
- Permission decisions are covered by a tested access-policy matrix for
  wiki visibility, export permissions, and letterhead access.

### Validation

- 305 automated tests cover wiki-source parsing, export plumbing,
  authentication, permission resolution, security-sensitive storage (including
  at-rest cache encryption), AI provider probing, diagnostics, and migrations.
- `dotnet restore .\ExportAzureWiki.sln`
- `dotnet build .\ExportAzureWiki.sln -c Release --no-restore`
- `dotnet test .\ExportAzureWiki.Tests\ExportAzureWiki.Tests.csproj -c Release --no-build`

## v1.0.0 - 2026-06-03

### Added

- Modern Windows desktop experience built with WPF.
- Guided first-run setup for administrators.
- Wiki connection management with Azure DevOps Wiki as the primary supported provider.
- Extensible provider model for GitHub, GitLab, Bitbucket, Confluence, MediaWiki, DokuWiki, and custom providers.
- Rich wiki rendering for Markdown, tables, code blocks, images, Mermaid diagrams, and math formulas.
- Word export with HTML preprocessing, image handling, code styling, table formatting, heading pagination, table-of-contents support, and optional final document fine-tuning.
- PDF export with dedicated print styling, SVG sanitization, rendered asset handling, and pagination/readability adjustments.
- HTML output support for preview, auditing, and external workflows.
- Local content and image cache for faster repeated rendering and offline-friendly workflows.
- Authentication support for local accounts, Windows/AD, Azure AD, and configurable OAuth providers.
- Administration screens for users, groups, OAuth providers, AI providers, wiki connections, and access policies.
- Permission controls for wiki visibility, start points, comments, Word export, PDF export, and letterhead usage.
- Export history tracking for operational auditability.
- Built-in help content in English and Brazilian Portuguese.
- CLI commands for saving wiki configuration and exporting wiki content from scripts.

### Improved

- Separated the application into Core contracts, Platform services, WPF UI, and CLI layers.
- Improved export pipeline resilience for complex wiki pages with diagrams, math, tables, images, and code.
- Improved workspace behavior around page selection, export options, cache refresh, and user access boundaries.
- Improved localization coverage by centralizing UI text through `LocalizationManager`.
- Improved diagnostics through structured logging and project reports.

### Requirements

- Windows 10/11.
- .NET SDK 9.x for development.
- Application projects target .NET 8.
- Microsoft WebView2 Runtime.
- SQLite, SQL Server, PostgreSQL, or MySQL depending on deployment configuration.

### Validation

- `dotnet restore .\ExportAzureWiki.sln`
- `dotnet build .\ExportAzureWiki.sln -c Release --no-restore`
