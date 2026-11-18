# 0010 — Review findings are answered in a register that the agents read, and a serious finding that is not fixed is the maintainer's to accept

- **Status**: accepted
- **Date**: 2026-11-11
- **Spec**: D-18; refines rule 14 of `CLAUDE.md`

## Context

Four review agents read every pull request (and CodeQL and SonarQube read the code). They are made to find defects, so they always find something, they remember nothing from one round to the next, and they sometimes contradict each other. The maintainer decided the protocol on 2026-11-05 and asked for it in every project; it lives in the organisation's `github-template` (`templates/quality/review-decisions/`, the `ai_review` option) and is adopted here as in Mnemarr's family ([Orchestrarr's ADR 0022](https://github.com/MafLabs38/orchestrarr/blob/develop/docs/adr/0022-review-findings-are-answered-in-a-register-the-agents-read.md) is the model).

## Decision

1. **Three answers, one table per pull request**: *fixed* (the author), *declined or deferred* at medium or below with the reason in one line (the author), *declined or deferred* at blocker, critical, high or major, or a CodeQL security alert (**the maintainer**, in a comment on the pull request, before it is final). Only the last kind interrupts the maintainer.
2. **A register in the repository**: `docs/review-decisions/<pr number>.md`, one entry per finding in a fixed shape ([README](../review-decisions/README.md)). The agents read it (the section "Decisions already taken" of their instructions) and do not raise again a finding that an entry answers, unless the code changed so that the reason no longer holds. A serious entry without a valid approval link counts as open.
3. **A check, not only a promise**: the `review-decisions` job (`.github/workflows/review-decisions.yml`, checker `tools/review-decisions/check.py`, approvers `.github/review-approvers`) fails a pull request that adds or changes a serious entry that is not fixed and does not cite a comment of an approver. It runs the checker and the approvers of the **base branch**: a pull request cannot approve itself. The checker's own tests run in the `lint` job of the CI.
4. **The rules that judge are in the maintainer's tier** (ADR 0008): the README of the folder, the checker, the approvers file, the job, the agents' instructions and the skills under `.claude/`. The entries themselves are the author's, and the job checks them.
5. **The agents advise, they do not command**: each finding is checked in the code; the cosmetic is declined in one line; detail is never added only to satisfy an agent (the lesson of Mnemarr's PR #23, forty rounds).

## Consequences

- The agents' noise falls, the author keeps the last word on everything that is not serious, and a serious finding cannot be set aside silently.
- An entry answers the same finding at the same place and nothing wider: it is not a standing waiver for a component.
- The job cannot know the level an agent gave: it is the author's word in the heading, so the maintainer reads the table. A false entry is a breach of the protocol, not a gap in the check.
- Making `review-decisions` (and the other checks) a *required* check is a setting of `repos/geminarr.yaml` in the template repository, done once these jobs pass.
- The agents are models and still judge differently from one run to the next: the register reduces the noise, it does not remove it.
