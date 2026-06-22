# Configuration reference

Every tunable knob in one place, with the defaults the security model
in [`SECURITY.md`](SECURITY.md) assumes.

## Authentication

Stored as JSON under `auth.runtime.config` in `ApplicationSettings`.
Edited through the admin UI; the values below are the **defaults**
when no override is persisted.

### Session

| Field                     | Default | Effect                                                              |
| ------------------------- | ------- | ------------------------------------------------------------------- |
| `SessionTimeoutMinutes`   | `1440`  | Absolute upper bound. Session is rejected after this many minutes from login regardless of activity. |
| `IdleTimeoutMinutes`      | `60`    | Sliding window. Session is rejected if more than this many minutes pass between successful validations. Set to `0` to disable the idle check. |
| `EnableRememberMe`        | `true`  | Whether the UI offers a "remember me" toggle.                       |
| `AllowMultipleProviders`  | `true`  | Whether more than one authentication provider can be enabled at once. |
| `RequireAuthentication`   | `true`  | When `false`, anonymous read flows are allowed (legacy; not recommended). |

### Password policy

| Field                       | Default | Effect                                                                                          |
| --------------------------- | ------- | ----------------------------------------------------------------------------------------------- |
| `PasswordMinLength`         | `8`     | Minimum length. Increase to `12+` for higher-trust deployments.                                 |
| `PasswordRequireUppercase`  | `true`  | At least one A-Z character.                                                                    |
| `PasswordRequireLowercase`  | `true`  | At least one a-z character.                                                                    |
| `PasswordRequireDigit`      | `true`  | At least one 0-9 character.                                                                    |
| `PasswordRequireSymbol`     | `true`  | At least one character that is not a letter or digit.                                          |

For a passphrase-style policy, set `MinLength = 20` and disable the
four character-class flags. The UI surfaces the failing rule key
(`password.policy.too_short`, `password.policy.missing_symbol`, ...)
so the user sees what's wrong instead of a generic message.

### Lockout (brute-force protection)

| Field                       | Default | Effect                                                                |
| --------------------------- | ------- | --------------------------------------------------------------------- |
| `MaxFailedAttempts`         | `5`     | Consecutive failed logins before the account is locked. Set to `0` to disable. |
| `LockoutDurationMinutes`    | `15`    | How long the lockout lasts. Minimum effective value is `1`.           |

Failed logins are recorded in the `security_audit_log` table with the
`login.failure` event and a `reason` field (`unknown_user`,
`inactive`, `locked`, `wrong_password`). The transition into the
locked state emits `account.locked`.

## Database

Captured at setup time and stored in the bootstrap config the OS
ships with the binary (`appsettings.bootstrap.json`). After that, the
fields are read by `ConnectionStringBuilder`.

### Shared fields

| Field                       | Type     | Required | Notes                                          |
| --------------------------- | -------- | -------- | ---------------------------------------------- |
| `DatabaseType`              | enum     | yes      | `SqlServer`, `PostgreSQL`, `MySQL`, `SQLite`.  |
| `Server`                    | string   | yes (except SQLite) | Host or `tcp:host`.                     |
| `Port`                      | int      | optional | `0` uses the engine default.                   |
| `Database`                  | string   | yes (except SQLite) | Must pass `SqlIdentifier.Validate` (letters, digits, underscore). |
| `Username`                  | string   | optional | Required unless `UseWindowsAuth = true` (SQL Server only). |
| `Password`                  | string   | optional | Required if `Username` is set.                 |
| `UseWindowsAuth`            | bool     | SQL Server only | Integrated security via the current Windows session. |
| `FilePath`                  | string   | SQLite only | Filesystem path to the DB file.            |
| `TrustServerCertificate`    | bool     | optional | **Defaults to `false`**. See "TLS" below.      |

### TLS defaults

| Engine     | `TrustServerCertificate = false` (default) | `TrustServerCertificate = true` (opt-in) |
| ---------- | ------------------------------------------ | ---------------------------------------- |
| SQL Server | `Encrypt=True; Trust Server Certificate=False` | `Encrypt=True; Trust Server Certificate=True` (still encrypted) |
| PostgreSQL | `SSL Mode=VerifyFull`                      | `SSL Mode=Require`                       |
| MySQL      | `SSL Mode=VerifyCA`                        | `SSL Mode=Required`                      |
| SQLite     | N/A                                        | N/A                                      |

The setup wizard surfaces `TrustServerCertificate` as a labelled
warning checkbox: do not enable it outside a lab environment.

### Application setting keys

Keys persisted in the `ApplicationSettings` table:

| Key                         | Content                                                  | Encryption     |
| --------------------------- | -------------------------------------------------------- | -------------- |
| `auth.runtime.session`      | Current `UserSession` JSON                               | AES-GCM        |
| `auth.runtime.config`       | `AuthenticationConfig` JSON                              | None           |
| `language.preferred`        | `PersistedLanguage` JSON (current UI language)           | None           |

### Schema migration journal

The `schema_migrations` table records every structural migration that
has been applied, one row per stable migration id
(`0001_authentication_configuration` ... `0008_security_audit_log`),
with the UTC timestamp. It is the auditable upgrade history for a
given database. Query it to confirm which upgrades a database has
received; see the RUNBOOK "Schema is wrong / missing column" section
for the recovery procedure.

## Wiki connection

Stored in `WikiConfigurations`. One row per wiki.

| Column                  | Type           | Notes                                                                |
| ----------------------- | -------------- | -------------------------------------------------------------------- |
| `PersonalAccessToken`   | text           | **AES-GCM with `enc:` prefix** since Fase 1.6. Legacy plaintext rows are rewritten the next time they are saved. |
| `Organization`          | text           | `https://dev.azure.com/<org>`                                        |
| `Project`               | text           | Azure DevOps project name                                            |
| `WikiIdentifier`        | text           | Wiki id or wiki name as it appears in the API                        |
| `IsDefault`             | bool           | Exactly one row per visibility scope is `IsDefault = true` after save |
| `VisibilityScope`       | enum-as-string | `Personal` or `Global`                                               |
| `OwnerUserId`           | text           | User id when `VisibilityScope = Personal`                            |

## OAuth provider config

Stored in `OAuthProviders`. One row per provider (Microsoft Account,
Google, GitHub, Azure AD).

| Column                  | Type   | Notes                                                                       |
| ----------------------- | ------ | --------------------------------------------------------------------------- |
| `ClientId`              | text   | Plaintext (not a secret).                                                   |
| `ClientSecret`          | text   | **AES-GCM with `enc:` prefix** since Fase 1.6.                              |
| `TenantId`              | text   | Azure AD only. `organizations` if omitted.                                  |
| `RedirectUri`           | text   | `http://localhost` lets MSAL pick a loopback port; matches the default App Registration template. |
| `Scopes`                | text   | Space- or comma-separated. Defaults to `openid profile email User.Read GroupMember.Read.All` for Azure AD. |

## File system paths

All under the running Windows user's profile. None of them require
elevation.

| Path                                                                            | Owner                  | Purpose                                |
| ------------------------------------------------------------------------------- | ---------------------- | -------------------------------------- |
| `%LocalAppData%\ExportAzureWiki\key.dat`                                        | DPAPI per Windows user | AES-GCM master key (32 bytes random)   |
| `%LocalAppData%\ExportAzureWiki\Logs\app-yyyyMMdd.log`                          | App                    | Serilog file sink, daily rolling, 25 MB cap, 14 day retention |
| `%LocalAppData%\ExportAzureWiki\MsalCache\ExportAzureWiki.msal.cache`           | DPAPI per Windows user | MSAL token cache                       |
| `%LocalAppData%\ExportAzureWiki\Cache\WikiPages\<scope>\*`                      | App                    | Rendered HTML cache                    |
| `%LocalAppData%\ExportAzureWiki\Cache\WikiImages\<scope>\*`                     | App                    | Downloaded wiki images                 |

## Logging

| Sink     | Level         | Notes                                                                   |
| -------- | ------------- | ----------------------------------------------------------------------- |
| File     | `Information` | Rolling daily under `Logs\`. Format includes timestamp, level, message, structured `{Property}` map. |
| Trace    | `Warning`     | Forwards to `System.Diagnostics.Trace` so DebugView, the VS Output window, and CI test capture see warnings/errors without parsing the file. |

Every event passes through `SensitivePropertyEnricher`. Any property
named `Password`, `Pat`, `PersonalAccessToken`, `Token`,
`AccessToken`, `RefreshToken`, `IdToken`, `ClientSecret`, `Secret`,
`ApiKey`, `ApiToken`, `Authorization`, or `EncryptedSession` is
replaced with `***` before the sinks write.

## Cache management

The render cache under `%LocalAppData%\ExportAzureWiki\Cache` is
**not** automatically pruned. Operators should schedule a task that
deletes content older than the freshness window the team accepts
(commonly 7-30 days). The audit log table and the operational log
files keep their own retention policies (audit: forever, ops: 14 day
rolling).
