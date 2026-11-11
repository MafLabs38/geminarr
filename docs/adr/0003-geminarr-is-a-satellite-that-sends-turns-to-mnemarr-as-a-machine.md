# 0003 — Geminarr is a satellite that sends turns to Mnemarr as a machine

- **Status**: accepted
- **Date**: 2026-11-11
- **Spec**: D-02, D-03, D-04, D-05, D-06, D-07, D-11

## Context

The maintainer talks to Gemini every day (phone, mostly by voice; web on the PC) and wants those conversations findable by meaning in Mnemarr. Gemini has no API for its history. Mnemarr already collects conversations from machines by position, masks them twice, writes one note per conversation, indexes every note for search by meaning, and can consolidate conversations into memory notes. It must not learn how a given source is fetched.

## Decision

1. **A separate container**, a satellite: its own backend, database and page. It talks to Mnemarr only through Mnemarr's public API, outbound; Mnemarr never calls it.
2. **It is a machine for Mnemarr**: enrolled or paired like a collector, with an ingest-only key bound to the machine, sending a heartbeat; machine `gemini`, application `gemini`, one session per Gemini conversation. Mnemarr's machine page, states, positions, masking and search apply unchanged.
3. **A neutral format `chat-turns`** (one JSON line per turn, SPEC §5.2), built in Mnemarr (M-1), because the conversations come from a web product and not from a CLI's file. Not Gemini-specific.
4. **No consolidation** of these conversations: they are an idea repository, found by meaning; Mnemarr gets a per-machine switch (M-2). The cost on the subscription stays nil for them.
5. **No second copy**: Geminarr keeps metadata, positions and the outbox of unacknowledged batches; text is deleted when Mnemarr acknowledges it.
6. **A session that stops working is a state**, reported in the heartbeat as an `attention` (M-3); **Mnemarr sends the mail** that asks the maintainer to sign in again. Geminarr holds no mail credential.

## Alternatives considered

- A plugin inside Mnemarr's container: rejected, the fragile and secret-holding part must stay isolated and replaceable.
- Sending mail from Geminarr: rejected, a mail credential in the most sensitive container, and every satellite would need its own.
- Consolidating like Claude Code conversations: rejected, nothing is being built, the value is retrieval.

## Consequences

- Mnemarr needs four small changes (SPEC §5, [mnemarr-changes.md](../mnemarr-changes.md)), made in its own repository with its own chain.
- A turn rewritten after it was sent is not sent again (the known limit of formats that are documents).
- Renames of a conversation are not propagated (Q-07).
