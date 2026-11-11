# What Mnemarr must gain for Geminarr

> Change requests addressed to the **Mnemarr** repository, written here as the single source so that both repositories are built against the same text. Each one is made in Mnemarr, with its own chain (an ADR and a D-row there, tests, the four review agents). The contract is [SPEC.md §5](SPEC.md#5-the-contract-with-mnemarr).
>
> **Not started.** To be done by a session opened in the Mnemarr workspace, from a file of instructions (see the end of this document), so that the conversation history belongs to that repository.

| Id | Change | Why | Needed by |
|---|---|---|---|
| **M-1** | The conversation format **`chat-turns`**: a reader/parser for the lines of SPEC §5.2 (a `meta` line 0 with the title, then one JSON line per turn with `role`, `at`, `text`), registered next to `claude-code` in `ConversationFormats`, with the rule for `session` and `from`; documented in `docs/conversation-formats.md` | Geminarr's turns are not a CLI's file; any web assistant can use it | Vertical 1 |
| **M-2** | A **per-machine switch "consolidate: yes/no"** (default yes) on the machine (Manage button of the Machines page; API; configuration as code). `ConsolidationStore.Pending` and the job skip conversations of a machine where it is off. Machines of format `chat-turns` default to **no** | The consolidation has one global switch and no filter by machine or format; Gemini conversations must not be consolidated | Vertical 1 |
| **M-3** | An optional **`attention`** object (`{code, url}`) in the heartbeat (SPEC §5.4): `needs-login`, `reader-broken`. A heartbeat with `attention` makes the machine `failing` with the link on the machines page, and triggers **a mail** to a configured address — once per episode, then at most once a day. The mail holds the machine name, the sentence and the link; no token, no conversation. The sender is a new, small, generic **notifications** feature of Mnemarr (a mail adapter behind an interface; the service is chosen in Mnemarr's own spec, nothing yet exists) | The session ends sometimes; one place sends mail | Vertical 1 (the signal), vertical 5 (the mail in production) |
| **M-4** | **Check** that the start-of-session thread and the context pack (ADR 0037/0038) and the consolidation never pull `chat-turns` / consolidate-off machines into a Claude session by mistake, and that search can include or exclude them with `topic` | Q-12 | Vertical 1 |
| M-5 | *Optional.* Let a sender set or change the **title** of a conversation (Q-07), instead of only the first meta line | Renames in Gemini | Later |

## Acceptance (what Geminarr relies on)

- A batch `{format: "chat-turns", …}` is accepted for a machine that was enrolled for it; the note appears under `gemini/gemini/<day>-<session>.md`, titled from the `meta` line, with each turn under its heading; positions work as for `claude-code` (overlap skipped, no hole filled, `GET /api/v1/ingest/positions` lists the session).
- A machine with consolidate off is never sent to the runner; the Sources page's consolidation card does not count its conversations as waiting.
- A heartbeat with `attention` shows the link on the machines page, and (with the notifications configured) one mail is sent per episode; a second heartbeat does not send another; a heartbeat without `attention` ends the episode.
- Everything above has tests in Mnemarr; none of them stores a real conversation.

## The file of instructions for the Mnemarr session

When the maintainer is ready, the agent writes in the **Mnemarr workspace** a file (for example `docs/handoff/geminarr.md`, not committed until the session decides) that restates M-1 to M-4 with the acceptance above, links to this document and to SPEC §5, and says: *read `CLAUDE.md` and the protocol; one vertical, one pull request; ADR + D-row; do not start before reading Geminarr's SPEC §5*. The maintainer then opens a session from that workspace.
