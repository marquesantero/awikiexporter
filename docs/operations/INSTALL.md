# Installation

Goal: a workstation that can sign in, browse a wiki, and export to
Word / PDF, against the database engine the organization uses.

## Prerequisites

| Component                | Minimum                             | Notes                                                   |
| ------------------------ | ----------------------------------- | ------------------------------------------------------- |
| OS                       | Windows 10 22H2 / Windows 11        | Required by DPAPI and WebView2                          |
| .NET runtime             | .NET Desktop Runtime 8.x            | Self-contained build does not need this                 |
| WebView2 runtime         | Latest Evergreen                    | Pre-installed on Windows 11; Windows 10 may need manual install from Microsoft |
| Visual C++ runtime       | 2015-2022 redistributable           | Pulled in by SQL Server client                          |
| Database engine          | One of: SQL Server 2019+, PostgreSQL 13+, MySQL 8+, SQLite 3 | TLS strongly recommended                                |
| Disk                     | 1 GB free under `%LocalAppData%`    | Cache + token store + logs                              |

## Quick start (development / single user)

This path uses a SQLite file and the local Windows user account. No
server-side dependencies. Suitable for evaluation.

```powershell
git clone https://github.com/marquesantero/awikiexporter.git
cd awikiexporter
dotnet restore .\ExportAzureWiki.sln
dotnet build .\ExportAzureWiki.sln -c Release --no-restore
dotnet run --project .\ExportAzureWiki.Wpf\ExportAzureWiki.Wpf.csproj
```

On first launch the setup wizard creates:

- `%LocalAppData%\ExportAzureWiki\key.dat` — the AES-GCM master key
  protected with DPAPI for the current Windows user.
- The SQLite database at the path the wizard chooses (default
  `D:\sqlitewiki\sqlitewiki.db` if available, otherwise an
  interactive prompt).
- The initial admin account.

## Production install (Windows desktop)

### 1. Database setup

Pick an engine and create the database. Schemas are applied
automatically on first run; no DBA scripts to deliver.

The application user needs:

| Engine     | Required privileges                                        |
| ---------- | ---------------------------------------------------------- |
| SQL Server | `db_owner` on the target DB (creates tables, indexes)      |
| PostgreSQL | Owner of the schema or `CREATE TABLE` on `public`          |
| MySQL      | `CREATE`, `ALTER`, `INDEX`, plus `SELECT`/`INSERT`/`UPDATE`/`DELETE` |
| SQLite     | Filesystem write to the database file                      |

### 2. TLS configuration

The defaults reject self-signed certs. Make sure the certificate
the database server presents is rooted in a trusted CA on every
client machine. If it is **not**, two choices:

- Distribute the issuing CA via Group Policy (preferred).
- Set `DatabaseConfiguration.TrustServerCertificate = true` per
  workstation (visible warning checkbox in the setup wizard).

See [`CONFIGURATION.md`](CONFIGURATION.md) for the resulting
connection-string shape.

### 3. Network egress

The application reaches:

| Destination                                    | Purpose                          | Port  |
| ---------------------------------------------- | -------------------------------- | ----- |
| Database server                                | All persistence                  | 1433 / 5432 / 3306 |
| `dev.azure.com` / `vssps.dev.azure.com`        | Azure DevOps Wiki                | 443   |
| `login.microsoftonline.com`                    | MSAL Azure AD authentication     | 443   |
| `graph.microsoft.com`                          | Azure AD user / group lookup     | 443   |
| `cdn.jsdelivr.net`                             | Mermaid and MathJax (optional)   | 443   |

A network proxy that intercepts TLS (corporate MitM) must have its
root CA installed on the workstation.

### 4. WebView2 runtime

WebView2 powers the rendering surface and the export pipeline. The
**Evergreen** distribution receives security updates from Microsoft.
For locked-down environments the **fixed-version** runtime can be
shipped alongside the app; document the version in your change log
and update it on every Chromium security advisory.

### 5. First-run setup

Launch `ExportAzureWiki.Wpf.exe`. The setup wizard collects:

- Database engine, connection details, optional
  `TrustServerCertificate`.
- Initial admin username, email, password. The password is validated
  against the active `PasswordPolicy`; the wizard prints the failing
  rule key (`password.policy.too_short`,
  `password.policy.missing_symbol`, ...) which the UI localizes.
- Optional Azure DevOps wiki connection (organization URL, PAT,
  project, wiki id). The PAT is encrypted with AES-GCM and stored
  with the `enc:` prefix.

The wizard finalizes the schema and writes the bootstrap config to
the database. After that, the same admin account is used to register
additional users, configure OAuth providers, and assign permissions.

### 6. Backup / restore

| Item                                                          | What to back up                              |
| ------------------------------------------------------------- | -------------------------------------------- |
| Database                                                      | Standard DB backup; contains all wiki configs, users, audit log, policies |
| `%LocalAppData%\ExportAzureWiki\key.dat` (per Windows user)   | If lost, encrypted blobs in the DB cannot be decrypted on that machine |
| `%LocalAppData%\ExportAzureWiki\MsalCache\*` (per Windows user) | Optional; users re-authenticate on restore   |
| `%LocalAppData%\ExportAzureWiki\Logs\*`                       | Operational; rolling daily                   |

Critical: the AES-GCM master key is DPAPI-bound to the local Windows
user. **A workstation reimage without preserving the user profile
will make every encrypted blob unreadable.** Operators have two
mitigation strategies:

1. Treat encrypted blobs as session-only and re-enter PATs after a
   reimage.
2. Back up DPAPI master keys via the Windows backup credentials
   provider (`pwdmgr` / DPAPI master-key backup).

## Uninstall and full reset

The app is full-trust, so it stores its data in the **real** user profile, not
the MSIX package container. MSIX also has no uninstall hook. Consequently,
**uninstalling the package leaves per-user data behind**, and a reinstall will
NOT return to the onboarding wizard (the setup state lives in the registry).

Per-user data locations:

| Location                              | Contents                                             |
| ------------------------------------- | ---------------------------------------------------- |
| `HKCU\Software\ExportAzureWiki`       | Onboarding gate (`SetupComplete`), connection string, database type |
| `%LocalAppData%\ExportAzureWiki`      | Local SQLite DB, `key.dat`, MSAL token cache, render/image cache, logs |
| `%TEMP%\ExportAzureWiki`              | Wiki clones and source/markdown caches               |

To fully reset a workstation (e.g. to re-run onboarding, or before handing the
machine to another user), close the app and run:

```powershell
pwsh ./tools/maintenance/Reset-AppData.ps1          # prompts for confirmation
pwsh ./tools/maintenance/Reset-AppData.ps1 -Force   # no prompt
```

This removes the three locations above. It does **not** touch external
databases (SQL Server / PostgreSQL / MySQL) — those are server-side and must be
dropped separately if a clean slate is required. The AES-GCM master key
(`key.dat`) is deleted too, so any secrets still held in an external DB become
undecryptable on this machine afterwards — intended for a true reset.

## CLI install

The CLI ships in the same solution; same prerequisites except WebView2
and the WPF runtime are not required.

```powershell
dotnet run --project .\ExportAzureWiki.CLI\ExportAzureWiki.CLI.csproj -- --help
```

See `README.md` for invocation patterns.

## Verifying the install

After first run:

```powershell
# Verify the audit log table was created and the admin login was recorded.
sqlite3 D:\sqlitewiki\sqlitewiki.db "SELECT event_type, username, occurred_at FROM security_audit_log ORDER BY id DESC LIMIT 3;"

# Confirm the master key file exists and is DPAPI-protected.
Get-Item "$env:LocalAppData\ExportAzureWiki\key.dat" | Format-List FullName, Length

# Confirm the Serilog file sink is writing.
Get-ChildItem "$env:LocalAppData\ExportAzureWiki\Logs"
```

If you can browse a wiki page and export to PDF without any
`***` warnings in the log, the install is functional.
