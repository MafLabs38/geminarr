# 0024 — The history is walked slowly, and its end is told by the list

- **Status**: accepted
- **Date**: 2026-11-19
- **Spec**: D-32 (SPEC §6.3, F-RDR-04, F-VIEW-02, F-VIEW-03)

## Context

The first pass finds an account that may hold years of conversations. Reading them all at once would be a burst of visits to the assistant from a browser that holds a real session (rule 3 of `CLAUDE.md`), and the spec asks for the opposite: the past is walked **slowly, on purpose** (§6.3). The spec also planned a stored flag, `backfill_done`.

## Decision

1. **Backfill is a mode of the same pass, not a second mechanism.** Besides the conversations that a pass looks at (`MaxListed`, 40) and opens when they changed (`MaxOpenPerPass`, 5), Geminarr asks the extension for up to `BackfillPerPass` (`GEMINARR__READER__BACKFILLPERPASS`, default 10, 0 switches it off, 20 at most) conversations **below** them that it has never seen, from the newest to the oldest. The pass opens the same ones, in addition to its recent ones, and they reach Mnemarr like any other conversation (positions, outbox, masking).
2. **No flag says that the history is done.** What is asked for is "listed, below the recent ones, never seen": once there is none, nothing more is asked. The progress (`listed`, `known`) is told from the last list and from what Geminarr has seen, so it cannot disagree with the list, a lost database starts the walk again by itself, and a conversation that the assistant adds at the bottom (an import on its side) is found.
3. **The list says whether it ends there.** The extension scrolls the list until a scroll brings nothing more (30 scrolls at most, 500 conversations at most, which is what Geminarr takes) and sends `complete: true` only then. A list that was cut is **not** the whole history: the page says so, and the rest of the past is for the import of an archive (D-10).
4. **The extension opens the pages of one answer one after the other**, with a pause of one to four seconds between two (§6.4), at most 25 for an answer (the recent ones and the backfill). It opened them all at once before.
5. **Two pages show it, metadata only**: *Conversations* (id, state, turns sent and read, rewritten turns, when last read, and the progress of the history) and *Runs* (every pass: outcome, counts, how long, why). Geminarr keeps no title and no turn (D-06): the pages do not show one, and an id is all that names a conversation here. Both are kept up to date by the channel of ADR 0023 (the `runs` topic).

## Alternatives considered

- **A stored `backfill_done` flag** (the spec's first idea): a second source of truth that can disagree with the list, that a lost database or a conversation added below would leave wrong.
- **A separate backfill task or a new run kind**: another scheduler and another failure mode for what is the same visit of the same page; the pass already has the timeout, the backoff and the pause.
- **Reading everything at the first pass**: a burst that a person would not make.

## Consequences

- A history of a few hundred conversations takes days at the default pace (ten a pass), as the spec says; the operator can raise or lower it, and *Run now* adds a pass.
- The window of the list that one round reads is 500 conversations: past that, the Takeout import is the way (vertical 6).
- The scroll of the list is the one place where the extension acts on the page (it scrolls, it clicks nothing); the end of the list is now also what it tells.
