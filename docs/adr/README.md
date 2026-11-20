# Architecture Decision Records

One file per structural decision, numbered in order: `NNNN-title.md`. Each ADR matches a `D-xx` row in the [spec](../SPEC.md#2-decisions).
An accepted ADR is never edited to change its decision: a new ADR supersedes it.

| ADR                                                                                         | Decision                                                                                                          | Spec                              |
| ------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- | --------------------------------- |
| [0001](0001-record-architecture-decisions.md)                                               | Record architecture decisions                                                                                     | —                                 |
| [0002](0002-the-family-stack-and-structure.md)                                              | The family stack and structure                                                                                    | D-01                              |
| [0003](0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md)            | Geminarr is a satellite that sends turns to Mnemarr as a machine                                                  | D-02 to D-07, D-11                |
| [0004](0004-a-dedicated-google-account-never-the-main-one.md)                               | A dedicated Google account, never the main one                                                                    | D-08, D-09, D-10                  |
| [0005](0005-the-same-quality-chain-and-process-as-mnemarr.md)                               | The same quality chain and process as Mnemarr                                                                     | D-12                              |
| [0006](0006-releases-with-semantic-release-consumed-by-pulling.md)                          | Releases are decided by semantic-release from main, and consumed by pulling                                       | D-13                              |
| [0007](0007-geminarr-is-described-as-code-at-three-levels.md)                               | Geminarr is described as code, at three levels                                                                    | D-14                              |
| [0008](0008-the-agent-merges-technical-pull-requests.md)                                    | The agent merges technical pull requests; the maintainer decides verticals, releases and the rules                | rule 12                           |
| [0009](0009-default-port.md)                                                                | Default port 8282                                                                                                 | D-17                              |
| [0010](0010-review-findings-are-answered-in-a-register-the-agents-read.md)                  | Review findings are answered in a register the agents read; a serious one not fixed is the maintainer's to accept | D-18                              |
| [0011](0011-one-operator-a-session-in-memory-and-everything-closed-but-the-probes.md)       | One operator, a session in memory, and everything closed but the probes                                           | D-19                              |
| [0012](0012-the-attention-mail-points-to-mnemarr-not-to-the-satellite.md)                   | The attention mail points to Mnemarr, not to the satellite                                                        | D-20                              |
| [0013](0013-the-menu-says-which-environment-it-is-with-a-name-a-logo-and-the-version.md)    | The menu says which environment it is: a name, a logo and the version                                             | D-21, F-SYS-04                    |
| [0014](0014-the-pass-the-outbox-and-mnemarrs-positions.md)                                  | The pass, the outbox and Mnemarr's positions                                                                      | D-22, F-LNK-01 to F-LNK-04        |
| [0015](0015-the-browser-the-live-view-and-the-profile.md)                                   | The browser, the live view and the profile _(superseded by 0016)_                                                 | D-23 (withdrawn)                  |
| [0016](0016-the-browser-is-a-container-of-its-own.md)                                       | The browser is a container of its own, and a person signs in to it by hand                                        | D-24, F-CON-01 to F-CON-04, F-LGN |
| [0017](0017-the-browser-is-for-geminarr-alone-and-geminarr-relays-its-screen.md)            | The browser is for Geminarr alone, and Geminarr relays its screen behind its own sign-in                          | D-25, F-CON-01, F-LGN             |
| [0018](0018-a-guard-may-be-lightened-in-a-sandbox-by-a-setting-and-is-strict-by-default.md) | A guard may be lightened in a sandbox by an explicit setting; strict by default                                   | D-26                              |
| [0019](0019-the-desktop-has-an-origin-of-its-own-opened-by-a-ticket.md)                     | The desktop has an origin of its own, opened by a ticket of the operator's session                                | D-27                              |
| [0020](0020-geminarrs-own-egress-is-limited-by-its-user-in-the-shared-namespace.md)         | Geminarr's own egress is limited by its user, in the namespace that it shares with the browser                    | D-28                              |
| [0021](0021-geminarr-has-no-debugging-port-to-the-browser.md)                               | Geminarr has no debugging port to the browser                                                                     | D-29                              |
| [0022](0022-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md)             | The reader is an extension in the browser that a person signed in to                                              | D-30                              |
| [0023](0023-what-changes-is-pushed-to-the-screens-as-it-changes.md)                          | What changes is pushed to the screens as it changes                                                  | D-31                              |
| [0024](0024-the-history-is-walked-slowly-and-its-end-is-told-by-the-list.md)                 | The history is walked slowly, and its end is told by the list                                        | D-32                              |
| [0025](0025-a-conversation-is-read-from-its-end-as-far-as-geminarr-needs.md)                 | A conversation is read from its end, as far back as Geminarr needs                                   | D-33                              |
| [0026](0026-one-account-signed-in-is-one-machine-of-notes.md)                                | One account signed in is one machine of notes                                                        | D-35                              |

## Template

```markdown
# NNNN — Title

- **Status**: proposed | accepted | superseded by NNNN
- **Date**: YYYY-MM-DD
- **Spec**: D-xx

## Context

## Decision

## Alternatives considered

## Consequences
```
