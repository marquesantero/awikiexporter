# Operations Runbook

Symptom-driven troubleshooting for production support. Each section
ends with the audit / log query that confirms the diagnosis.

## "I can't log in"

### 1. Account is locked

**Symptom**: the login screen reports "account locked".

**Confirm**:

```sql
SELECT username, failed_login_count, locked_until
FROM users
WHERE username = 'the.user';
```

If `locked_until` is in the future, lockout is active.

**Resolve**: either wait `LockoutDurationMinutes` (default 15), or
clear via the admin UI / a `UPDATE users SET failed_login_count = 0,
locked_until = NULL WHERE id = ?` issued by an admin. Record the
manual unlock in the audit log (see "Audit a manual change").

**Confirm root cause**:

```sql
SELECT occurred_at, event_type, detail
FROM security_audit_log
WHERE username = 'the.user'
  AND event_type = 'login.failure'
ORDER BY occurred_at DESC
LIMIT 20;
```

The `detail` column shows whether attempts were from a known IP
(legitimate user retyping) or from many different sources (active
brute force, raise `MaxFailedAttempts` is the wrong answer; lower
the threshold and investigate the source).

### 2. Password policy rejects the new password

**Symptom**: setup wizard or password-reset flow rejects an
otherwise reasonable password.

**Confirm**: the error message is keyed
(`password.policy.too_short`, `password.policy.missing_symbol`,
...). Check the active policy:

```
auth.runtime.config -> PasswordMinLength, PasswordRequire*
```

**Resolve**: either pick a password that satisfies the active
policy, or adjust the policy via the admin UI.

### 3. Local auth not allowed for this account

**Symptom**: `auth.error.external_user_provider_not_allowed` or
`auth.local.not_enabled`.

**Confirm**: the user's row has `AuthenticationMethod` set to
`AzureAD` or `OAuth`, or `auth.runtime.config` has
`AllowLocalAuth = false`.

**Resolve**: the user must sign in via the configured external
provider. Resetting `AuthenticationMethod` to `Local` from the
admin UI re-enables password login but should only be done by an
admin and is itself recorded.

## "Azure AD sign-in fails"

### 1. Stuck on consent / prompt every launch

**Symptom**: every process start re-prompts even though the user
just authenticated.

**Confirm**: check whether the persistent cache attached on this
machine.

```
%LocalAppData%\ExportAzureWiki\MsalCache\ExportAzureWiki.msal.cache
```

If the file is missing, MSAL fell back to in-memory cache.

**Confirm in log**:

```
Get-Content "$env:LocalAppData\ExportAzureWiki\Logs\app-*.log" |
    Select-String "persistent token cache could not be attached"
```

**Resolve**: usually a permissions issue on `%LocalAppData%`. Make
sure the user can write that folder. Re-launch.

### 2. Group sync is empty after login

**Symptom**: the user has no group membership in the app even though
the Azure AD config has `SyncAzureADGroups = true`.

**Confirm in log**:

```
Get-Content "$env:LocalAppData\ExportAzureWiki\Logs\app-*.log" |
    Select-String "Graph /me/memberOf"
```

A `Warning` line means the Graph call failed; the most common cause
is the App Registration is missing the `GroupMember.Read.All`
permission and admin consent.

**Resolve**: grant the permission in the Azure portal, request admin
consent, re-launch.

### 3. PAT-style behaviour ("works once, fails after a day")

**Symptom**: silent token acquisition stops working after a few
days; the user is re-prompted.

**Confirm**: refresh tokens have a lifetime defined by the Azure AD
tenant. The cache helper rotates them on every silent acquisition,
but only if the user actually opens the app inside the refresh
token's lifetime.

**Resolve**: not a bug; tune the tenant policy if the desired
session length is longer than the configured refresh-token lifetime.

## "Database connection fails"

### 1. "Certificate chain was not trusted"

**Symptom**: the connection fails with a TLS chain error.

**Confirm**:

```
Get-Content "$env:LocalAppData\ExportAzureWiki\Logs\app-*.log" |
    Select-String "Certificate"
```

**Resolve**: distribute the issuing CA via Group Policy, or set
`TrustServerCertificate = true` for the workstation (lab only).
**Do not** disable TLS entirely.

### 2. Schema is wrong / missing column

**Symptom**: a service throws "column does not exist".

**Confirm**: the schema upgrade runs idempotently on startup via
`SchemaManager.EnsureRequiredTablesAsync`, which drives each
structural step through a journaled migration runner. Check which
migrations have actually been recorded:

```sql
SELECT id, description, applied_at
FROM schema_migrations
ORDER BY applied_at;
```

A missing id (e.g. `0008_security_audit_log`) means that step never
completed. Check the log for the failure:

```
Get-Content "$env:LocalAppData\ExportAzureWiki\Logs\app-*.log" |
    Select-String "Schema migration run failed"
```

**Resolve**: address the underlying exception. Each step is
idempotent (`ColumnExistsAsync` / `TableExistsAsync` before
`ALTER` / `CREATE`) and is only journaled after it succeeds, so a
failed step re-runs cleanly on the next boot. Deleting a row from
`schema_migrations` forces that step to run again on next launch.

## "Export to PDF / Word produced corrupt output"

### 1. Mermaid diagram missing

**Symptom**: a diagram block in the wiki rendered to plaintext in
the export.

**Confirm**: the live render shows the diagram; export does not.

**Resolve**: the export pipeline drives WebView2 with a fresh
context. Check that the WebView2 runtime version on the
workstation is current; an old runtime cannot evaluate the
mermaid bundle the host page loads.

### 2. Image broken in export

**Symptom**: `<img>` is rendered to a broken-image icon.

**Confirm**: the source URL is one of:

- `https://local.images/...`: the file should be at
  `%LocalAppData%\ExportAzureWiki\Cache\WikiImages\<scope>\<file>`.
  Check the cache scope matches the active wiki.
- `https://dev.azure.com/...`: the embedded asset on Azure DevOps
  required authentication. The downloader uses the PAT stored on
  the wiki config; if the PAT was rotated, re-save it.

## Audit log queries

The `security_audit_log` table is the canonical record. The most
useful queries:

### Logins by user this week

```sql
SELECT occurred_at, event_type, detail
FROM security_audit_log
WHERE username = 'the.user'
  AND occurred_at >= datetime('now', '-7 days')  -- SQLite; use INTERVAL for Postgres
ORDER BY occurred_at DESC;
```

### Brute-force candidates

```sql
SELECT username, COUNT(*) AS failures, MAX(occurred_at) AS last_attempt
FROM security_audit_log
WHERE event_type = 'login.failure'
  AND occurred_at >= datetime('now', '-1 day')
GROUP BY username
HAVING COUNT(*) >= 10
ORDER BY failures DESC;
```

### Newly locked accounts

```sql
SELECT occurred_at, username, detail
FROM security_audit_log
WHERE event_type = 'account.locked'
ORDER BY occurred_at DESC
LIMIT 50;
```

### Admin-policy changes

```sql
SELECT occurred_at, username, detail
FROM security_audit_log
WHERE event_type IN ('policy.admin.changed',
                     'permission.granted',
                     'permission.revoked')
ORDER BY occurred_at DESC;
```

## Diagnostic export

If a user reports a problem you cannot reproduce, ask for the
diagnostic bundle.

### Via the CLI (preferred)

```powershell
dotnet run --project .\ExportAzureWiki.CLI\ExportAzureWiki.CLI.csproj `
    -- diagnose --output "$env:USERPROFILE\Desktop\ExportAzureWiki-diag.zip"
```

The bundle includes:

- `manifest.json` — application version, .NET runtime, OS, culture.
- `system.json` — boolean flags for the presence of the master key
  file, the MSAL cache, and the logs folder. **No contents.**
- `logs/*.log` — last 14 daily log files. Every entry has already
  passed through `SensitivePropertyEnricher` so credentials are
  masked at write time.
- `audit-summary.txt` — counts per event type from the most recent
  100 audit entries plus the latest 20 (timestamp + event type +
  username).

The bundle explicitly **excludes**:

- The DPAPI-protected master key file (`key.dat`).
- The MSAL token cache.
- The wiki content cache (`WikiPages`, `WikiImages`).
- DB connection strings.

### Manual fallback

If the CLI is unavailable on the affected workstation, the support
engineer can ship the log directory directly:

```powershell
$dst = "$env:USERPROFILE\Desktop\ExportAzureWiki-diag.zip"
$src = "$env:LocalAppData\ExportAzureWiki\Logs"
Compress-Archive -Path $src -DestinationPath $dst
```

This still respects the secret-masking guarantee but loses the
manifest and audit summary.

The user should also report:

- App version (visible in the About dialog).
- WebView2 runtime version (from
  `HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients` registry).
- Database engine and version.
- A short reproduction of the failing flow.

## Common log messages reference

| Log line                                                              | Meaning                                                                | Action                                |
| --------------------------------------------------------------------- | ---------------------------------------------------------------------- | ------------------------------------- |
| `Stored session is unusable, discarding`                              | The encrypted session blob failed to decrypt or deserialize.           | Expected after key rotation or schema change; no action.       |
| `Admin lookup failed for user ... defaulting to non-admin`            | DB query for admin role failed; user temporarily lost admin UI.        | Check DB connectivity.                |
| `Persistent token cache could not be attached`                        | MSAL fell back to in-memory cache; re-prompt expected on next launch.  | Check `%LocalAppData%` write rights.  |
| `Local login locked account ... for N min after ... failed attempts`  | Lockout activated for this account.                                    | Expected; investigate source of attempts. |
| `Group lookup failed for user ...; evaluating direct policies only`   | Identity-group DB read failed; user evaluated against direct policies only. | Check DB connectivity / schema.    |
| `audit login.failure ... reason="unknown_user"`                       | Login against a username that does not exist.                          | Likely an enumeration probe.          |
