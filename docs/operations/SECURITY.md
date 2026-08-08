# Security Model

This document describes the security guarantees ExportAzureWiki provides,
the choices that back them, and the limitations operators should be aware
of when deploying in a corporate environment.

It is the canonical reference for security reviewers, auditors, and
incident-responders.

## Supported versions

| Version | Status            | Security fixes |
| ------- | ----------------- | -------------- |
| `main`  | Active            | Yes            |
| `>= v1` | Latest minor only | Yes            |
| Older   | Unsupported       | No             |

## Reporting a vulnerability

Please **do not open a public issue** for a security report. Email the
maintainer listed in `CODEOWNERS` (or contact the repository owner via
the GitHub profile) with:

- A clear description of the issue and the affected component
- Reproduction steps, including version / commit
- Impact assessment (what an attacker gains)
- Any proposed remediation

You should receive an acknowledgement within five business days. Public
disclosure will be coordinated after a fix is available.

## Threat model

ExportAzureWiki is a Windows desktop application that connects to an
Azure DevOps wiki, renders content locally, and exports documents. The
threat model that informs the design:

| Asset                              | Threats considered                                     |
| ---------------------------------- | ------------------------------------------------------ |
| Personal Access Token (PAT)        | Theft from DB / config; theft from logs                |
| OAuth client secrets               | Theft from DB                                          |
| User session                       | Session fixation; tamper of stored session             |
| Local user passwords               | Offline brute force; online brute force; weak policy   |
| Wiki content rendered in WebView2  | XSS injected via wiki authors                          |
| Database connection in transit     | MitM on corporate network                              |
| Database connection at rest        | DBA / backup read of plaintext secrets                 |
| Exported document                  | Authoring of arbitrary HTML by a wiki author           |
| Audit trail                        | Tampering; deletion                                    |

Out of scope: kernel-level malware on the workstation, physical access
with admin rights, an attacker who already controls the DB account the
application uses to write.

## What we protect

### At-rest encryption

| Item                            | Mechanism                       | Where the key lives                            |
| ------------------------------- | ------------------------------- | ---------------------------------------------- |
| PAT in `WikiConfigurations`     | AES-GCM (`enc:` prefix)         | DPAPI per Windows user (`%LocalAppData%/ExportAzureWiki/key.dat`) |
| OAuth `ClientSecret`            | AES-GCM (`enc:` prefix)         | Same DPAPI master key                          |
| AI provider `ApiKey`            | AES-GCM (`enc:` prefix)         | Same DPAPI master key                          |
| DB connection string (registry) | AES-GCM (`enc:` prefix)         | Same DPAPI master key                          |
| Stored session                  | AES-GCM                         | Same DPAPI master key                          |
| MSAL Azure AD token cache       | DPAPI native                    | DPAPI per Windows user (`%LocalAppData%/ExportAzureWiki/MsalCache/`) |
| Local user password             | PBKDF2-SHA256, 100k iterations  | Per-user 32-byte salt                          |

The database connection string (which may contain the DB password) is
stored under `HKCU\Software\ExportAzureWiki`; the value is AES-GCM
encrypted, so a registry export or profile backup does not leak DB
credentials. Legacy plaintext values from earlier builds are re-encrypted
on the next connection save.

Old AES-CBC blobs from earlier releases are still readable for one
release cycle; values are rewritten in AES-GCM the next time they pass
through `Encrypt` ([Fase 1.1](../reports/) migration window).

### TLS to the database

Default settings reject self-signed certificates. Operators can opt out
explicitly with `DatabaseConfiguration.TrustServerCertificate = true`
for lab environments; the wizard surfaces this as a warning checkbox.

| Engine     | Default                              | Trust-override (opt-in)        |
| ---------- | ------------------------------------ | ------------------------------ |
| SQL Server | `Encrypt=true; TrustServerCertificate=false` | `Encrypt=true; TrustServerCertificate=true` |
| PostgreSQL | `SslMode=VerifyFull`                 | `SslMode=Require`              |
| MySQL      | `SslMode=VerifyCA`                   | `SslMode=Required`             |
| SQLite     | Local file; not network              | N/A                            |

### Authentication

- Local accounts: PBKDF2-SHA256 + per-user salt + constant-time
  comparison.
- Local lockout: configurable threshold (`MaxFailedAttempts`, default 5)
  and cooldown (`LockoutDurationMinutes`, default 15). Failed
  attempts against an unknown username still consume a fixed CPU budget
  in the verifier to reduce timing-based enumeration.
- Password policy: configurable (`PasswordMinLength`,
  `PasswordRequireUppercase/Lowercase/Digit/Symbol`).
- Sessions: random `SessionId` per login (rotation defeats session
  fixation), absolute expiry (`SessionTimeoutMinutes`), sliding idle
  expiry (`IdleTimeoutMinutes`).
- Azure AD: MSAL.NET Public Client with PKCE on every interactive
  flow (PKCE cannot be disabled at this seam). Token cache persisted
  with DPAPI in `CurrentUser` scope so refresh tokens survive process
  restart without exposing them to other Windows users.

### Authorization

- Effective admin is resolved against `AccessPolicies`. DB failure
  fails closed (default to non-admin) and is logged at Error.
- Group lookup failures during admin evaluation fall back to
  direct-policy evaluation and are logged at Warning. They never
  silently elevate.

### SQL injection

- Every parameter is bound via Dapper.
- Identifiers that come from configuration (database name, table
  name) pass through `SqlIdentifier` which validates against
  `^[A-Za-z_][A-Za-z0-9_]{0,62}$` and quotes per dialect. Hostile
  inputs (`xx"; DROP TABLE x--`) are rejected before reaching the
  driver.

### XSS in rendered wiki content

- Wiki HTML passes through `HtmlSanitizer` (HtmlAgilityPack-based)
  before reaching the WebView2 host page: `<script>`, `<iframe>`,
  `<object>`, `<embed>`, `<form>`, `<input>`, `<link>`, `<meta>`,
  `<base>` are stripped; every `on*` attribute is removed; URLs
  beginning with `javascript:`, `vbscript:`, `file:`,
  `data:text/html` are nulled.
- A strict `Content-Security-Policy` `<meta>` is injected into the
  host template: `default-src 'none'; object-src 'none'; base-uri
  'self'; frame-ancestors 'none'; form-action 'none'`. The
  template-emitted inline scripts (highlight.js init, mermaid init,
  MathJax config) are constructed from compile-time string literals;
  `'unsafe-inline'` is allowed for them with the explicit intent to
  replace with nonces in a follow-up.
- Mermaid runs with `securityLevel: 'strict'`.

### Audit log

`SecurityAuditLog` records authentication-relevant events with the
timestamp, event type, user id / username, IP and user agent (when
available), plus a free-form JSON detail blob. Events:

- `login.success`
- `login.failure` (with `reason` = `unknown_user`, `inactive`,
  `locked`, `wrong_password`)
- `logout`
- `account.locked`
- `account.unlocked`
- `password.changed`
- `password.reset.requested`
- `policy.admin.changed`
- `permission.granted` / `permission.revoked`

Events also flow through the Serilog file sink under
`%LocalAppData%/ExportAzureWiki/Logs/`, so a DB outage does not lose
the audit trail.

### Secrets in logs

Serilog runs every event through `SensitivePropertyEnricher`. Any
property whose name matches a known secret label is replaced with
`***` before the sinks see the event. Recognized labels (case-
insensitive):

```
Password, Pat, PersonalAccessToken, Token, AccessToken,
RefreshToken, IdToken, ClientSecret, Secret, ApiKey, ApiToken,
Authorization, EncryptedSession
```

Adding a new credential-bearing label requires updating the enricher
itself; the change is covered by `SensitivePropertyEnricherTests`.

## Known limitations

The remediation plan tracks these and they are NOT shipped as
"done"; they are listed here for honesty during a review.

- **WinForms in `Platform`**: `SafeExportWrapper` still depends on
  `Microsoft.Web.WebView2.WinForms`. This blocks shipping a fully
  WinForms-free Platform (Fase 3.1b).
- **Inline scripts in render template**: the CSP allows
  `'unsafe-inline'` for scripts the template itself emits. The
  template-emitted strings are compile-time constants, not wiki
  content, but switching to nonces would tighten the policy further.
- **`Result<T>` not standardized**: some service methods still
  surface exceptions instead of returning a `Result<T>` from
  `ExportAzureWiki.Core`. Inconsistency makes "every error is logged
  with structure" harder to enforce at the type level.
- **No automated integration tests against real DBs in CI**:
  Testcontainers wiring is planned (Fase 2.2). Unit-level coverage of
  the security-critical code paths is 107 tests today; DB-level
  coverage is tested locally only.
- **External distribution trust**: release MSIX packages are signed, but a
  self-signed certificate still requires the public `.cer` to be trusted on
  target machines. Public internet distribution should move to a CA-backed
  or managed code-signing certificate.

## Cryptographic primitives reference

| Use case              | Algorithm                              | Key size  | Source                          |
| --------------------- | -------------------------------------- | --------- | ------------------------------- |
| Secret at-rest        | AES-GCM (12-byte nonce, 16-byte tag)   | 256 bits  | `System.Security.Cryptography`  |
| Master-key wrapping   | Windows DPAPI                          | OS-managed| `ProtectedData.Protect`         |
| Password hashing      | PBKDF2-HMAC-SHA256, 100,000 iterations | 256 bits  | `Rfc2898DeriveBytes`            |
| Random nonce / salt   | CSRNG                                  | -         | `RandomNumberGenerator`         |
| Constant-time compare | -                                      | -         | `CryptographicOperations.FixedTimeEquals` |

## Update / disclosure cadence

- Critical / High CVE in a direct or transitive dependency:
  patched and a pre-release published within five business days.
- Medium / Low: rolled into the next scheduled release.
- The `vulnerable-packages` job in CI fails the build on any
  High / Critical reported by `dotnet list package --vulnerable
  --include-transitive`, so a known-bad dependency cannot land on
  `main`.
