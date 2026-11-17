# 0018 — A guard may be lightened in a sandbox by an explicit setting; strict by default

- **Status**: accepted (by the maintainer, 2026-11-17)
- **Date**: 2026-11-17
- **Spec**: D-26, `GEMINARR__AUTH__MINPASSWORDLENGTH`

## Context

The operator's password had a fixed minimum of 12 characters. In a sandbox on a private build machine, nobody else reaches the page, and a strong password only costs the maintainer a login that their password manager does not save and that they have to type at every new address. The maintainer's rule: a security model is right, but **in development the guards must be lightenable**; a strong password in a local container protects nothing.

## Decision

1. **The strict value stays the default and what a real deployment gets**: a guard is lightened by a setting that a person writes on purpose, never by the absence of one.
2. **First guard made adjustable: the minimum length of the operator's password** (`GEMINARR__AUTH__MINPASSWORDLENGTH`, default 12, 1 to 1024), documented as a sandbox-only value in `docs/configuration.md`. Nothing else about the sign-in changes (the lock after wrong attempts, the session, the header of a write).
3. **The same stance for the next guards**, one at a time and only when a sandbox needs it: a setting with a strict default, documented as sandbox-only. The guards that protect the session of the Google account (the browser reachable by Geminarr alone, the loopback-only desktop, the egress policy) are **not** lightened by this decision: they protect something that is not a sandbox's own.

## Consequences

- A deployment that sets the minimum below 12 does so knowingly; the settings page shows it (it is not a secret).
- The sandbox runs `admin` / `admin` with `GEMINARR__AUTH__MINPASSWORDLENGTH=1`.
