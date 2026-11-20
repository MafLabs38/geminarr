# 0027 — Any account may be signed in, and there is no import

- **Status**: accepted
- **Date**: 2026-11-20
- **Spec**: D-34; D-09 and D-10 withdrawn

## Context

[ADR 0004](0004-a-dedicated-google-account-never-the-main-one.md) made a rule of a dedicated Google account, never the main one, and put an import of the past (an archive of the user's own export) beside it as the way to bring the history of the main account. The maintainer decided otherwise, and said that he never asked for an import: it was an invention of the specification. He is the only person who signs in; the exposure of a session that is signed in for the time of a reading is small against what he wants (his conversations in his memory); and a rule that the product enforces by a sentence on a page and nothing else was never a protection.

## Decision

1. **No Google account is forbidden, the main one included, and the one that is signed in is the one that is read.** There is no check, no sentence on a page and no rule that names a "dedicated" or a "main" account. A dedicated account stays the careful choice, as advice in the documentation.
2. **There is no import of the past**: no archive, no Takeout. The past of an account is what its page shows when it is signed in, read slowly ([ADR 0024](0024-the-history-is-walked-slowly-and-its-end-is-told-by-the-list.md)).
3. **What stays, because it is about the session and not about which account**: the profile is the crown jewel of the browser container (its own volume, never logged, never returned, excluded from clear backups, *Disconnect* deletes the cookies), an account that is signed out is not read, and reading is read-only on the assistant's own pages.

## Alternatives considered

- **Keep the rule and add an exception for the main account.** An exception that the maintainer takes at once is a rule that nobody follows; it only makes the product say something false.
- **Keep the import for the history of the main account.** A second path to the same notes, with its own format, its own parser and its own tests, for a history that the page already gives.

## Consequences

- The security model says plainly that the session of whichever account is signed in is the whole account, and protects the profile accordingly (SPEC §11).
- Two accounts in one Geminarr need a way to keep their notes apart: [ADR 0026](0026-one-account-signed-in-is-one-machine-of-notes.md).
- ADR 0004 keeps its text (points 3 and 4 still hold); its first and fifth points are superseded by this one.
