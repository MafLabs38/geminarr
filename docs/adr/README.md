# Architecture Decision Records

One file per structural decision, numbered in order: `NNNN-title.md`. Each ADR matches a `D-xx` row in the [spec](../SPEC.md#2-decisions).
An accepted ADR is never edited to change its decision: a new ADR supersedes it.

| ADR | Decision | Spec |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | — |
| [0002](0002-the-family-stack-and-structure.md) | The family stack and structure | D-01 |
| [0003](0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) | Geminarr is a satellite that sends turns to Mnemarr as a machine | D-02 to D-07, D-11 |
| [0004](0004-a-dedicated-google-account-never-the-main-one.md) | A dedicated Google account, never the main one | D-08, D-09, D-10 |
| [0005](0005-the-same-quality-chain-and-process-as-mnemarr.md) | The same quality chain and process as Mnemarr | D-12 |
| [0006](0006-releases-with-semantic-release-consumed-by-pulling.md) | Releases are decided by semantic-release from main, and consumed by pulling | D-13 |
| [0007](0007-geminarr-is-described-as-code-at-three-levels.md) | Geminarr is described as code, at three levels | D-14 |
| [0008](0008-the-agent-merges-technical-pull-requests.md) | The agent merges technical pull requests; the maintainer decides verticals, releases and the rules | rule 12 |
| [0009](0009-default-port.md) | Default port 8282 | D-17 |
| [0010](0010-review-findings-are-answered-in-a-register-the-agents-read.md) | Review findings are answered in a register the agents read; a serious one not fixed is the maintainer's to accept | D-18 |

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
