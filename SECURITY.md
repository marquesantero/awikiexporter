# Security policy

See [`docs/operations/SECURITY.md`](docs/operations/SECURITY.md) for the
full security model: threat model, cryptographic primitives, known
limitations, and update cadence.

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

## Supported versions

| Version | Status            | Security fixes |
| ------- | ----------------- | -------------- |
| `main`  | Active            | Yes            |
| `>= v1` | Latest minor only | Yes            |
| Older   | Unsupported       | No             |

## Update cadence

- Critical / High CVE in a direct or transitive dependency: patched
  and a pre-release published within five business days.
- Medium / Low: rolled into the next scheduled release.
- The `vulnerable-packages` CI job fails the build on any
  High / Critical reported by `dotnet list package --vulnerable
  --include-transitive`, so a known-bad dependency cannot land on
  `main`.
