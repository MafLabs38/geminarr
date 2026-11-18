# 0021 — Geminarr has no debugging port to the browser

- **Status**: accepted by the maintainer in the conversation of 2026-11-18
- **Date**: 2026-11-18
- **Supersedes**: the attachment of Geminarr to the browser's debugging address in [0016](0016-the-browser-is-a-container-of-its-own.md) (`GEMINARR__BROWSER__CDPURL`, Playwright)
- **Spec**: D-29; D-08 (read-only) still holds

## Context

Geminarr attached to the browser container over its debugging port to look at the session. Whoever holds that port holds the browser: every cookie of the signed-in account, every page, any script. The maintainer rejected it ("a scraper must not go through a debugging port"): it is the one finding (R-24-34) that no code of this pull request could close while the attach existed. The conversations are to be read by an extension of the browser that pushes them to Geminarr (the next pull request); that extension also says what the session is.

## Decision

1. **Geminarr has no debugging port to the browser**: the browser container is not started with `--remote-debugging-port`, the project that drove it (`Geminarr.Browser`, Playwright) is removed, and `GEMINARR__BROWSER__CDPURL` no longer exists.
2. **What stays is the person's own sign-in**: the browser container, its web desktop relayed on an origin of its own behind a ticket of the operator's session (ADR 0017, 0019), the Connection page with its *Sign in* button and *Pause*, the egress guard (ADR 0020) and the exposure check.
3. **What Geminarr cannot know until the extension says it**: whether somebody is signed in, and as whom. Until then the connection reads "not connected", the pass only checks that the desktop answers, and *Disconnect* is refused with a sentence of ours (clearing the session without a debugging port needs the extension).

## Consequences

- R-24-34 has no object. The shared network namespace remains for the desktop only; its reason is the loopback address of the desktop, not a key to a session.
- The sign-in state ("Signed in as ...") and *Disconnect* come back with the extension reader, in the pull request that puts it back.
