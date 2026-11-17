# 0018 — A guard may be lightened in a sandbox by an explicit setting; strict by default

- **Status**: accepted (by the maintainer, 2026-11-17)
- **Date**: 2026-11-17
- **Spec**: D-26, `GEMINARR__AUTH__MINPASSWORDLENGTH`

## Context

The operator's password had a fixed minimum of 12 characters. In a sandbox on a private build machine, a strong password only costs the maintainer a login that their password manager does not save and that they type at every new address. The maintainer's rule: a security model is right, but **in development the guards must be lightenable**.

This guard is not harmless: **the operator's password is the key to the browser's session** (ADR 0017: whoever signs in to Geminarr reaches the relayed desktop of a browser that may be signed in to the dedicated Google account). Lowering it therefore weakens the guard of that session too. The maintainer decides this knowingly, for a sandbox on a private network whose Google account is the **dedicated** one (ADR 0004), never his main one.

## Decision

1. **The strict value stays the default and what a real deployment gets**: a guard is lightened by a setting that a person writes on purpose, never by the absence of one.
2. **First guard made adjustable: the minimum length of the operator's password** (`GEMINARR__AUTH__MINPASSWORDLENGTH`, default 12, 1 to 1024), documented as a sandbox-only value in `docs/configuration.md`. Nothing else about the sign-in changes (the lock after wrong attempts, the session, the header of a write).
3. **It is not silent**: the start of the container logs a warning while the minimum is below 12, and the setting is on the Settings page. It does not turn the health degraded: a sandbox would show "needs attention" for ever for a choice that was made on purpose.
4. **The same stance for the next guards**, one at a time and only when a sandbox needs it: a setting with a strict default, documented as sandbox-only. The guards that keep the browser reachable by Geminarr alone (the shared namespace, the loopback-only desktop, the egress policy, the relay's allow-list) are **not** lightened by this decision: they are not about who signs in, and a sandbox does not need them lighter.

## Consequences

- A deployment that sets the minimum below 12 does so knowingly; a copy of a sandbox's environment into a real deployment shows in the log at every start.
- The sandbox runs `admin` / `admin` with `GEMINARR__AUTH__MINPASSWORDLENGTH=1`, with the dedicated account only. Whoever reaches that sandbox on the private network reaches the dedicated account's session: that is the price, accepted by the maintainer.
- A decision at "major" that the review agents raised and that is the maintainer's: it is registered as declined-by-design with his approval comment (docs/review-decisions/24.md).
