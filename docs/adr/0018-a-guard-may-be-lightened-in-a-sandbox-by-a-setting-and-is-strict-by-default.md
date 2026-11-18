# 0018 — A guard may be lightened in a sandbox by an explicit setting; strict by default

- **Status**: decided by the maintainer in the conversation of 2026-11-17; amended the same day after the architecture review (R-24-109): the minimum is not lightened while a browser is configured
- **Amended by**: [0021](0021-geminarr-has-no-debugging-port-to-the-browser.md): Geminarr has no debugging port to the browser any more, so whatever this record says of `CDPURL`, the debugging address, attaching to the browser or driving it no longer holds; the rest stands.
- **Date**: 2026-11-17
- **Spec**: D-26, `GEMINARR__AUTH__MINPASSWORDLENGTH`

## Context

The operator's password had a fixed minimum of 12 characters. In a sandbox on a private build machine, a strong password only costs the maintainer a login that their password manager does not save and that they type at every new address. The maintainer's rule: a security model is right, but **in development the guards must be lightenable**.

This guard is not harmless: **the operator's password is the key to the browser's session** (ADR 0017: whoever signs in to Geminarr reaches the relayed desktop of a browser that may be signed in to the dedicated Google account). Lowering it therefore weakens the guard of that session too, so **it is lightened only where no browser (hence no session) is behind it**. The maintainer first accepted a lowered minimum with the browser, then, on seeing what the review said, chose the strict one there (a password of twelve characters that is easy to remember).

## Decision

1. **The strict value stays the default and what a real deployment gets**: a guard is lightened by a setting that a person writes on purpose, never by the absence of one.
2. **First guard made adjustable: the minimum length of the operator's password** (`GEMINARR__AUTH__MINPASSWORDLENGTH`, default 12, 1 to 1024), documented as a sandbox-only value in `docs/configuration.md`. **While a browser is configured (`GEMINARR__BROWSER__CDPURL` or `DESKTOPURL`), a minimum below 12 is refused at start**: the container does not run, and the message says why. Nothing else about the sign-in changes (the lock after wrong attempts, the session, the header of a write).
3. **It is not silent**: the start of the container logs a warning while the minimum is below 12 (a sandbox without a browser), and the setting is on the Settings page. It does not turn the health degraded: a sandbox would show "needs attention" for ever for a choice that was made on purpose.
4. **The same stance for the next guards**, one at a time and only when a sandbox needs it: a setting with a strict default, documented as sandbox-only. The guards that keep the browser reachable by Geminarr alone (the shared namespace, the loopback-only desktop, the egress policy, the relay's allow-list) are **not** lightened by this decision: they are not about who signs in, and a sandbox does not need them lighter.

## Consequences

- A sandbox with a browser keeps the strict minimum: a password that is short to type but twelve characters long. A lowered minimum is possible only without a browser (a sandbox of the screens, of the API), and a copy of its environment into a deployment with a browser does not start.
- The review agents raised it at "major" (R-24-109): it is answered *fixed* in `docs/review-decisions/24.md`, so it needs no approval of the maintainer.
