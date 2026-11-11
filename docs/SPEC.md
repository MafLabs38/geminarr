# Geminarr — Functional and Technical Specification

> **Status**: draft v0.3 (2026-11-11). The architecture and the security model are decided (D-01 to D-14, D-17); the foundation is built; **how Gemini is read is not known yet** and is the object of the first spike (§11, Q-01 to Q-06): the reader's design (§6) is written to survive either answer and will be frozen after it.
> **Related documents**: [VISION.md](VISION.md) (why) · [ROADMAP.md](ROADMAP.md) (the verticals) · [adr/](adr/README.md) (decision records) · [mnemarr-changes.md](mnemarr-changes.md) (what Mnemarr must gain)
> **Audience**: the project owner, contributors.
>
> ⚠️ This document is intended for the **public repo**. It contains **no real infrastructure values and no real conversation or account**. All names (`homelab.example`, `server-1`, `a-dedicated-account@example.com`…) are examples.

## Legend

| Marker | Meaning |
|---|---|
| ✅ | **Frozen**: decided, does not change without a new ADR |
| 🟡 | **Proposed**: written by the analyst, pending validation or the spike |
| ❓ | **Open**: unanswered question, see §11 |
| 🧪 | **Spike**: to be measured on the real target before it is designed |

Identifiers: `D-xx` decision · `F-XXX-nn` feature · `Q-xx` question · `M-n` change asked of Mnemarr.

## Table of contents

1. [Vision](#1-vision)
2. [Decisions](#2-decisions)
3. [Architecture](#3-architecture)
4. [Data model](#4-data-model)
5. [The contract with Mnemarr](#5-the-contract-with-mnemarr)
6. [The reader](#6-the-reader)
7. [The session and the live view](#7-the-session-and-the-live-view)
8. [The Takeout import](#8-the-takeout-import)
9. [Feature catalog](#9-feature-catalog)
10. [API, settings and health](#10-api-settings-and-health)
11. [Security and privacy](#11-security-and-privacy)
12. [Non-functional requirements](#12-non-functional-requirements)
13. [Question register](#13-question-register)
14. [Glossary](#14-glossary)
15. [Changelog](#15-changelog)

---

## 1. Vision

Geminarr is a **satellite of [Mnemarr](https://github.com/MafLabs38/mnemarr)**: a self-hosted container that brings the **conversation history of a web assistant** (Gemini, first) into Mnemarr, which finds it again by meaning and by text. It exists because that history has no API: it can only be read the way a person reads it, signed in to the web product.

What it does, in one sentence: *signed in to a dedicated Google account, it reads the Gemini conversations that appeared or grew since its last visit, and sends their new turns to Mnemarr, which stores them as notes and indexes them.*

What it is **not**:

- **Not a memory.** It keeps no copy of the conversations once Mnemarr has them (D-06). Search, notes, titles and everything a person does with a conversation happen in Mnemarr.
- **Not a consolidator.** Gemini conversations are an *idea repository*, not a build log: nothing is summarised or turned into decisions (D-05). They are raw notes found by meaning.
- **Not a Gemini client.** It never writes to Gemini, never starts a conversation and visits no page but the assistant's own (D-08).
- **Not for the main Google account** (D-09, §11).

How it fits the constellation:

| Product | Job | Relation to Geminarr |
|---|---|---|
| **Mnemarr** | Remember | Receives the turns (ingest API), shows Geminarr as a machine, sends the "sign in again" mail |
| **Portarr** | See the fleet | Registers Geminarr as an application and checks its health endpoint |
| **Orchestrarr** | Act | Not involved |

---

## 2. Decisions

| # | Decision | Rationale | ADR |
|---|---|---|---|
| ✅ D-01 | **An *arr-style application**: one container, .NET API, React frontend, API first. Same stack, structure, quality chain and process as Mnemarr. | The family shares conventions; a contributor of one project works in the others. | [0002](adr/0002-the-family-stack-and-structure.md) |
| ✅ D-02 | **A satellite**: Geminarr talks to Mnemarr only through Mnemarr's public API (ingest, positions, heartbeat, pairing), outbound only. Mnemarr never calls Geminarr. | Mnemarr must not know how data is fetched; a fragile scraper must not be able to take Mnemarr down. | [0003](adr/0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) |
| ✅ D-03 | **Geminarr is a machine for Mnemarr**: machine name `gemini` (configurable), application `gemini`, one session per Gemini conversation. | Reuses enrolment, keys, positions, masking, states and the machines page. | [0003](adr/0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) |
| ✅ D-04 | **A new neutral format `chat-turns`** (one JSON line per turn) for the ingest of conversations that no CLI wrote. Defined in §5, built in Mnemarr (M-1). | Not a Gemini-specific format: any web assistant fits. | [0003](adr/0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) |
| ✅ D-05 | **No consolidation of these conversations.** Mnemarr gains a per-machine switch (M-2); the search by meaning already covers them. | The maintainer uses Gemini to think out loud, not to build. | [0003](adr/0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) |
| ✅ D-06 | **No second copy.** Geminarr stores metadata, positions and an outbox of unacknowledged batches; the text of a turn is deleted from the outbox when Mnemarr acknowledges it. | One copy is easier to protect and to delete. | [0003](adr/0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) |
| ✅ D-07 | **Reading is by position and idempotent**: a turn is identified by its index in its conversation; nothing is sent twice, nothing is skipped, a restart loses nothing. | Mnemarr's contract (ADR 0024); same guarantees as a collector. | [0003](adr/0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) |
| ✅ D-08 | **Read-only, on the assistant's own pages.** The browser visits only the assistant's origin (and the sign-in pages of its account provider during the live view); it never submits a prompt. | Limits what a bug or a compromise can do on the account, and the footprint on the provider. | [0004](adr/0004-a-dedicated-google-account-never-the-main-one.md) |
| ✅ D-09 | **A dedicated Google account, never the main one**, sharing the maintainer's Google AI plan through the family group. A one-time import of the past may use the main account only by the ladder of D-10. | A Google session is valid across all of the account's services: a leak of this container must reach nothing but Gemini conversations. | [0004](adr/0004-a-dedicated-google-account-never-the-main-one.md) |
| ✅ D-10 | **The past is imported by the least exposed way that is complete enough**: (1) Google Takeout, no session; (2) extraction from the maintainer's own browser, text only; (3) last resort, an ephemeral sign-in of the container to the main account under the conditions of §11.4. | Same reason. | [0004](adr/0004-a-dedicated-google-account-never-the-main-one.md) |
| ✅ D-11 | **A session that stops working is a state, not a silence**: Geminarr reports `needs-login` to Mnemarr, which mails the maintainer a link to Geminarr's sign-in page. Geminarr holds no mail credential and the mail never contains a token. | One place sends mail (M-3); fewer secrets in the most sensitive container. | [0003](adr/0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) |
| ✅ D-12 | **The same pipeline, thresholds, review agents and process as Mnemarr** (verticals, one PR each, local gates, push once, triaged review). | The maintainer's rule: identical structures, processes and skills. | [0005](adr/0005-the-same-quality-chain-and-process-as-mnemarr.md) |
| ✅ D-13 | **Releases by semantic-release from `main`, consumed by pulling** (image on GHCR; a GitOps tool in the cluster follows the versions). Nothing in the CI can reach a host. | Same as Mnemarr's ADR 0013. | [0006](adr/0006-releases-with-semantic-release-consumed-by-pulling.md) |
| ✅ D-14 | **Everything is described as code**: the repository (Terraform, `github-template`), the image (this repo), the deployment (the private GitOps repository). The container is configurable by environment variables and files only. | The maintainer deploys Geminarr like Mnemarr, on the Kubernetes cluster, by GitOps. | [0007](adr/0007-geminarr-is-described-as-code-at-three-levels.md) |
| 🟡 D-15 | **The reader is an interface with two implementations** (the web reader, the Takeout importer) and a sink (Mnemarr). Selectors and the internal calls of the web product live in one versioned place. | The web product changes without notice; the pipeline must not. | *after the spike* |
| 🟡 D-16 | **One browser, started per pass and closed after it** (resident only during a live view). | A resident Chromium costs memory for nothing between two passes that are 20 minutes apart. | *after the spike* |
| ✅ D-17 | **Default port 8282** (Portarr has 8484, Mnemarr 8383). | No collision on a default install. | [0009](adr/0009-default-port.md) |

---

## 3. Architecture

### 3.1 Overview

```text
                         ┌──────────────────────────── Geminarr (one container) ─────────────────────────────┐
  operator's browser ───▶│  page (React)  ──▶  REST API /api/v1  ──▶  Core                                    │
  (behind the proxy's    │                           │                 ├─ ConnectionStateMachine               │
   single sign-on, and   │                           │                 ├─ PassScheduler  (every N minutes)     │
   Geminarr's own login) │                           │                 ├─ ConversationPipeline ──▶ Outbox      │
                         │                           │                 └─ MnemarrSink (HTTP client)            │
                         │   live view (WebSocket) ◀─┘                        ▲                                │
                         │        │                                          │ reads                          │
                         │        ▼                                          │                                │
                         │   Browser adapter (Playwright, one Chromium, profile on its own volume)            │
                         └───────────┬───────────────────────────────────────────────────────┬────────────────┘
                                     │ HTTPS out (D-08)                                      │ HTTPS out
                                     ▼                                                       ▼
                           gemini.google.com                                      Mnemarr  /api/v1/ingest/*
                           accounts.google.com (live view only)
```

### 3.2 Projects (same layers as Mnemarr)

| Project | Role | Rules |
|---|---|---|
| `Geminarr.Core` | Domain and use cases: connection state machine, scheduler, pipeline, outbox, ports (`IConversationReader`, `IConversationSink`, `IProfileStore`, `IClock`) | No ASP.NET, no browser code. Everything testable with fakes and `FakeTimeProvider`. |
| `Geminarr.Browser` *(vertical 2)* | The Playwright adapter: the web reader, the live view, the profile | Behind the ports of Core; selectors and calls of the web product in one place (D-15). |
| `Geminarr.Api.V1` | Controllers and resources of `/api/v1` | Thin. Authentication and the header of writes arrive with vertical 1. |
| `Geminarr.Host` | Entry point, configuration, health, static files of the frontend | The only project that knows the others. |
| `frontend/` | React page: overview, connection, conversations, runs, settings | Server data only through the data hooks (DESIGN.md). |
| Tests | `Geminarr.Core.Test` (unit), `Geminarr.Integration.Test` (the real host in memory), `tests/e2e` (Playwright against the image) | See [TESTING.md](TESTING.md). |

### 3.3 One pass (the unit of work)

```text
scheduler (every ~20 min ± 20 %, or "run now")
  1. ConnectionStateMachine: may we run?  (paused / needs-login / not-connected  →  skip, heartbeat only)
  2. start the browser on the profile; check the session ("am I signed in?")
        not signed in ×N  →  state needs-login, stop
  3. list the recent conversations (bounded), compare with `conversations`  →  the ones that are new or moved up
  4. for each, at most MaxOpenPerPass:  open it, extract the turns, compare with turns_sent
        new turns  →  outbox batch (from = position of the first new line)
  5. close the browser
  6. flush the outbox to Mnemarr (batches of ≤ 500 lines / 3 MB), delete what is acknowledged
  7. heartbeat to Mnemarr (state, version, pending lines, error/attention)
  8. write the run (counts, outcome, reason)
```

Steps 6 and 7 happen even when 2 failed: the outbox of an earlier pass is still flushed, and Mnemarr must hear that Geminarr is alive and what is wrong.

---

## 4. Data model

SQLite in the data folder (`geminarr.db`), a schema in **numbered steps** as in Mnemarr (ADR 0018), ADO.NET behind interfaces. The browser profile is **not** here: it has its own folder (§11).

| Table | Columns (type) | Notes |
|---|---|---|
| `schema_steps` | `step` (int, PK), `applied_at` | The migration ledger. |
| `connection` | `id` (=1), `state` (text), `since` (ts), `last_ok_at` (ts?), `consecutive_failures` (int), `last_reason` (text?), `paused_until` (ts?), `attention_sent_at` (ts?) | A single row. `state` is one of §7.2. |
| `destination` | `id` (=1), `url` (text), `machine` (text), `key_file` (text), `enrolled_at` (ts?), `last_heartbeat_at` (ts?), `last_heartbeat_error` (text?) | The Mnemarr this Geminarr sends to. The **key is never in the database**: only the path of a file only the container's user can read. |
| `conversations` | `id` (text, PK: the assistant's own id), `title` (text?), `first_seen_at`, `last_seen_at`, `last_opened_at` (ts?), `list_rank` (int?), `turns_known` (int), `turns_sent` (int), `last_turn_hash` (text?), `rewritten_turns` (int), `source` (`web`/`takeout`), `state` (`new`/`synced`/`gone`/`error`), `last_error` (text?) | **No text of a turn.** `turns_sent` is Geminarr's position; Mnemarr's (`GET /api/v1/ingest/positions`) is the truth and wins when they differ (§5.4). |
| `outbox` | `conversation_id`, `from_position` (int), `lines` (blob, compressed JSON lines), `created_at`, `attempts` (int), `last_error` | Batches not yet acknowledged. The only place a turn's text is held, and only until acknowledged. |
| `runs` | `id`, `kind` (`pass`/`backfill`/`import`), `started_at`, `ended_at`, `outcome` (`ok`/`partial`/`failed`/`skipped`), `reason` (text?), `listed`, `opened`, `turns_sent`, `duration_ms` | Kept 90 days (setting). |
| `imports` | `id`, `file_name`, `size`, `state` (`received`/`reading`/`done`/`failed`), `conversations`, `turns`, `error`, `created_at` | A Takeout import (§8). The uploaded archive is deleted when it is read. |

Invariants: `turns_sent ≤ turns_known`; a conversation's lines in the outbox are contiguous from `turns_sent`; deleting a conversation row never deletes what Mnemarr holds.

---

## 5. The contract with Mnemarr

Geminarr is a client of Mnemarr's existing ingest API ([collector.md](https://github.com/MafLabs38/mnemarr/blob/develop/docs/collector.md), ADR 0024/0027/0030). Only the **format** and the **attention signal** are new (M-1, M-3). The contract is written here so that both repositories are built against the same text.

### 5.1 Joining Mnemarr

Two ways, both ending with a key file (`GEMINARR__MNEMARR__KEYFILE`, default `<data>/mnemarr/key`, mode `0600`):

- **By asking (pairing, ADR 0030)**, for a person at the page: Geminarr calls `POST /api/v1/ingest/pair`, shows the approval address and a code; a person approves in Mnemarr, names the machine (default `gemini`); `…/pair/poll` returns the key once.
- **By a code (enrolment, ADR 0027)**, for a deployment as code: `GEMINARR__MNEMARR__ENROLMENTCODEFILE` names a file with a one-time code; Geminarr exchanges it at start (`POST /api/v1/ingest/enrol`) and writes the key. A key already provided as a file (`…KEYFILE` mounted from a sealed secret) is used as it is.

The key is **ingest-only** and **bound to the machine**; a 401/403 puts the destination in `mnemarr-refused` (§10.3) and Geminarr stops sending until it is paired again. The address must be HTTPS (a loopback or an explicit `…ALLOWINSECUREHTTP=true` sandbox excepted), and redirects are not followed.

### 5.2 The format `chat-turns` (M-1)

`POST /api/v1/ingest/conversations`

```json
{
  "format": "chat-turns",
  "machine": "gemini",
  "application": "gemini",
  "session": "gemini-c1a2b3c4d5e6",
  "from": 0,
  "lines": [
    "{\"v\":1,\"meta\":{\"title\":\"A distributed AI?\",\"source\":\"web\",\"id\":\"c1a2b3c4d5e6\"}}",
    "{\"v\":1,\"role\":\"user\",\"at\":\"2026-11-11T08:03:12Z\",\"text\":\"…\"}",
    "{\"v\":1,\"role\":\"assistant\",\"at\":null,\"text\":\"…\"}"
  ]
}
```

| Field | Rule |
|---|---|
| `session` | `gemini-` + the assistant's conversation id, lower-cased, `[a-z0-9-]` only (anything else is replaced; a collision is resolved by suffixing a short hash). Stable for the life of the conversation. |
| `from` | The position of the first line of `lines` in the conversation: line 0 is the `meta` line, turn *n* is line *n+1*. |
| line 0 | Always a `meta` line: `title` (may be empty), `source` (`web`/`takeout`), `id` (the assistant's id). It is written once; a later rename of the conversation is **not** sent (the position model cannot update a line; Q-07). |
| turn line | `role` (`user`/`assistant`), `at` (ISO-8601 UTC, or `null` when the product does not show one), `text` (plain text or Markdown as the assistant wrote it; never HTML). |
| size | A line is at most 1.5 MB (a longer turn is cut and ends with `[…truncated]`), a batch at most 500 lines or 3 MB: Mnemarr's limits. |
| not sent | Attachments, images, audio and tool output: a turn that held only an attachment is sent with the text `[attachment]`, so that positions stay aligned with the product. |

Mnemarr masks secrets again on its side; **Geminarr masks first** (the same rules as the collector's, `tests/masking/samples.json` shared in spirit) so that nothing it sends carries a private key, token or password assignment.

### 5.3 Positions, idempotence, rewrites

- Before sending, Geminarr asks `GET /api/v1/ingest/positions` (at start and about hourly, like the collector) and **goes back to where Mnemarr is** when Mnemarr holds fewer lines than Geminarr believes it sent (a restored backup, a deleted machine).
- The response `{next, accepted}` moves `turns_sent`. Mnemarr skips an overlap and never fills a hole: Geminarr never sends a batch whose `from` is beyond `next`.
- **A turn rewritten after it was sent** (an edit, a regenerated answer) is **not** sent again: it is the known limit of formats that are documents (conversation-formats.md). Geminarr detects it (the hash of a known turn differs), counts it in `rewritten_turns` and shows it in the page; it never reorders or renumbers.

### 5.4 The heartbeat and the attention signal (M-3)

At the end of every pass, even one that sent nothing: `POST /api/v1/ingest/heartbeat` with Mnemarr's existing `{version, pendingLines, error, paused}`, plus one optional field proposed here:

```json
{ "version": "0.3.0.123", "pendingLines": 0, "error": "The session has expired.", "paused": false,
  "attention": { "code": "needs-login", "url": "https://geminarr.homelab.example/connection" } }
```

`attention.code` is one of `needs-login`, `reader-broken`; `url` is an address the maintainer can open (from `GEMINARR__PUBLICURL`). Mnemarr treats a heartbeat with `attention` as `failing`, shows the link on the machines page and, once per episode and then at most once a day (settings of Mnemarr), **sends a mail** to the address the maintainer configured in Mnemarr. The mail contains the machine's name, the sentence and the link, **never a token or a conversation**.

### 5.5 What Mnemarr does with a Geminarr machine

It is `always-on` (state `silent` after 30 minutes without a heartbeat), its conversations are filed `gemini/gemini/<day>-<session>.md` (the first folder of a note is the machine, so a search can include or exclude them with `topic`), they are indexed for search by meaning, and **not consolidated** (M-2). Reading them needs `conversations.read`, as for every conversation.

---

## 6. The reader

### 6.1 Ports

```text
IConversationReader                          IConversationSink
  CheckSession()      → SessionCheck           Positions()                 → map session → next
  ListRecent(limit)   → ConversationHeader[]   Send(session, from, lines)  → { next, accepted }
  Open(id)            → Turn[]                 Heartbeat(state)            → void
```

`ConversationHeader` = `{ id, title?, rank, activityHint? }` (the list is ordered by last activity). `Turn` = `{ role, at?, text }`. Two implementations (D-15): the **web reader** (vertical 3) and the **Takeout importer** (vertical 4); a fake reader (in-memory conversations that grow) serves vertical 1's demo and the tests of the pipeline.

### 6.2 Deciding what to open 🧪

Gemini's list is ordered by last activity, so a conversation whose rank improved or that is new has changed (**to be confirmed by the spike**, Q-02). Per pass:

1. `ListRecent(MaxListed)` (default 40).
2. A conversation is a **candidate** if it is not in `conversations`, or its rank is better than last time, or its title changed.
3. Open at most `MaxOpenPerPass` (default 5) candidates, best rank first; the others wait for the next pass (their rank is not stored as "seen", so they stay candidates).
4. After `Open`, compare: if `turns_known` grew, or the hash of the last known turn differs, there is something to send; positions decide what.

### 6.3 Backfill

The first visit has a whole history to read. **Backfill is a mode of the same pass**: while `backfill_done` is false, each pass also opens up to `BackfillPerPass` (default 10) conversations older than the ones seen, walking the list deeper (`ListRecent` with a larger window, newest first), until the end of the list. It is **slow on purpose** (a few hundred conversations take days) and is the fallback of the Takeout import (§8), which is the preferred way for the past (D-10).

### 6.4 Politeness and failure

- **One browser, one page, sequential**, no parallel conversations; a random pause of 1–4 s between two opened conversations; a pass never lasts more than `PassTimeoutMinutes` (default 5).
- **Every wait is bounded and has a reason** (no fixed sleeps): wait for the list to appear, for the turns to render, for the scroll to stop loading.
- **Backoff**: a pass that fails (page changed, rate limit, network) doubles the interval up to `MaxBackoffMinutes` (default 360); a success resets it.
- **Failure classes**, each a named state with a reason code (§7.2, §10.3): session invalid → `needs-login`; the page does not look like what the reader knows (a selector or a call failed on the first conversation of the pass) → `reader-broken`; a block or captcha → `rate-limited` and the reader stops for `RateLimitPauseHours` (default 6); the product or the network is down → `unreachable`.
- **`reader-broken` is loud**: Geminarr stops opening conversations, keeps the outbox, signals `attention: reader-broken`, and writes in the run **which step** failed (never the page content). The fix is a new version of the reader.

### 6.5 Masking and the text

The text is extracted as the assistant wrote it (Markdown when the product exposes it, otherwise the rendered text), never as HTML. Masking (§5.2) runs before the outbox. Nothing of a conversation is written to the log, in any level (§11).

---

## 7. The session and the live view

### 7.1 The profile

The browser profile (cookies, local storage) of the dedicated account is the **session**. It lives in `GEMINARR__PROFILEFOLDER` (default `/profile`, **a volume of its own**, mode `0700`, owner the container's user), not in the data folder: it is excluded from every clear backup and from every snapshot of the database. It holds no extension, no synced data and no saved password (the browser is started with sync, autofill and password saving off).

### 7.2 The states of the connection

```text
                  start live view                  signed in detected
 not-connected ───────────────────▶ connecting ──────────────────────▶ connected ◀───────────┐
      ▲                                  │ timeout / closed                │  │ │              │ success
      │ disconnect (destroys profile)    ▼                                 │  │ └─ failure ──▶ degraded(unreachable | rate-limited | reader-broken)
      └──────────────────────────────── (back)                             │  │
                                         session invalid ×N (default 3)    │  └─ pause ──▶ paused ── resume ──▶ connected
                                                  ▼                        │
                                            needs-login ◀──────────────────┘
                                                  │ start live view
                                                  └──▶ connecting
```

| State | Meaning | Passes | Signal to Mnemarr |
|---|---|---|---|
| `not-connected` | No profile | none | `error: "Not connected."` |
| `connecting` | A live view is open | none | — |
| `connected` | Signed in and reading | yes | healthy |
| `degraded` | Signed in but the last pass failed: reason `unreachable`, `rate-limited` or `reader-broken` | backoff (§6.4) | `error` (+ `attention` for `reader-broken`) |
| `needs-login` | The session is gone (N consecutive "not signed in") | none | `error` + `attention: needs-login` |
| `paused` | The operator asked for quiet (until a time or until resumed) | none | `paused: true` |

A **network error is never "not signed in"**: only an explicit sign-in redirect or page counts towards `needs-login`.

### 7.3 The live view (F-LGN)

The operator signs in once, **through Geminarr's page**, to a browser running in the container:

1. The operator opens **Connection** and presses *Sign in*; Geminarr starts the browser on the profile, on the assistant's address, and opens a **live-view session** (one at a time; ten minutes at most; it ends when the page is closed).
2. The page shows the browser's screen (a canvas fed by the browser's screencast) and relays keyboard, mouse and paste to it. The operator types the dedicated account's address and password and approves the sign-in on their phone.
3. When the reader sees the signed-in application, the state becomes `connected`, the live view closes, the browser closes, the profile stays.
4. *Disconnect* closes the browser and **deletes the profile folder's content**; the state is `not-connected`.

Transport: a WebSocket on Geminarr's own origin (`/api/v1/connection/live/{id}`), opened with a **single-use token** minted by an authenticated POST; the server checks the `Origin`, the token and the operator's session, and refuses a second viewer. The streamed frames never reach a log or a disk. The Content-Security-Policy stays `default-src 'self'`: the canvas needs no exception (Q-08).

The sign-in pages of the account provider are the **only** pages other than the assistant's that the browser may load, and only during a live view (D-08): the network policy of the browser (a route filter) refuses every other origin.

---

## 8. The Takeout import

The preferred way to bring the past (D-10, step 1): the maintainer asks Google Takeout for the *Gemini Apps* activity of the main account, downloads the archive and gives it to Geminarr. **No session is involved.**

- **Input**: the archive (`.zip`, or an extracted folder under `GEMINARR__IMPORTFOLDER`), uploaded at *Import* (multipart, `GEMINARR__IMPORT__MAXMEGABYTES`, default 512) or dropped in the folder.
- **Safety** (it is untrusted input): entries are read in memory or in a private temporary folder, never extracted by name; a path with `..`, an absolute path or a symlink is refused; the number of entries, the size of an entry and the total uncompressed size are bounded (zip bombs); only JSON/HTML files of the known layout are read.
- **Mapping**: each activity entry becomes a turn pair (the prompt, then the response) in a conversation. The export may carry no conversation id (Q-06): then Geminarr builds one from the day and the order, `gemini-takeout-<yyyymmdd>-<n>`, `source: takeout`. Timestamps come from the entry; titles from the first prompt.
- **Idempotence**: re-importing the same archive sends nothing new (positions); a later archive that overlaps the first is merged by session id and position, never duplicated. If the web reader later sees a conversation the import created under another id, the two stay separate (Q-09 records this limit).
- **Result**: a page with counts (conversations, turns, skipped, errors); the archive is deleted when it has been read.

---

## 9. Feature catalog

Priority: **M** must (the MVP) · **S** should · **C** could. Vertical: see [ROADMAP.md](ROADMAP.md).

| Id | Feature | Pri. | Vertical |
|---|---|---|---|
| **F-SYS** | **System** | | |
| F-SYS-01 | Version, runtime, uptime; liveness and readiness probes | M | 0 |
| F-SYS-02 | Health with reason codes (§10.3), shown on the page | M | 1 |
| F-SYS-03 | Effective settings (read-only, secrets hidden) | S | 1 |
| **F-ACC** | **Operator access** | | |
| F-ACC-01 | A first administrator from a name and a password file (as Mnemarr, ADR 0016); a session cookie; the header of writes | M | 1 |
| F-ACC-02 | Behind the proxy's single sign-on **and** Geminarr's own login (nothing is reachable anonymously except the probes) | M | 1 |
| **F-LNK** | **The link to Mnemarr** | | |
| F-LNK-01 | Pair with a Mnemarr by asking, or enrol with a code, or use a provided key file | M | 1 |
| F-LNK-02 | Heartbeat at every pass; state and `attention` (§5.4) | M | 1 |
| F-LNK-03 | Resume from Mnemarr's positions (§5.3); the outbox survives a restart | M | 1 |
| F-LNK-04 | Show the destination, its state and its last error; unlink | S | 1 |
| **F-CON** | **The connection to the assistant** | | |
| F-CON-01 | The connection state machine (§7.2) and its page | M | 2 |
| F-CON-02 | The live view to sign in (§7.3); disconnect destroys the profile | M | 2 |
| F-CON-03 | Pause and resume | S | 2 |
| F-CON-04 | Detect an expired session and ask to sign in again (`needs-login`, `attention`) | M | 2 |
| **F-RDR** | **The reader** | | |
| F-RDR-01 | `IConversationReader`/`IConversationSink` and the pipeline with a fake reader and a fake sink | M | 1 |
| F-RDR-02 | The web reader: session check, list, open, extract turns (§6) | M | 3 |
| F-RDR-03 | Incremental passes: candidates, positions, outbox, batches | M | 3 |
| F-RDR-04 | Backfill of the whole history at a slow pace (§6.3) | S | 3 |
| F-RDR-05 | `reader-broken` and `rate-limited` handling, backoff (§6.4) | M | 3 |
| F-RDR-06 | Masking of secrets before the outbox | M | 3 |
| F-RDR-07 | "Run now" | S | 3 |
| **F-IMP** | **The Takeout import** | | |
| F-IMP-01 | Upload or drop an archive; safe reading; mapping to conversations (§8) | M | 4 |
| F-IMP-02 | Idempotent re-import; counts and errors on the page | M | 4 |
| **F-VIEW** | **What the operator sees** | | |
| F-VIEW-01 | Overview: connection, destination, last runs | M | 1–3 |
| F-VIEW-02 | Conversations: state, turns known/sent, rewritten turns (metadata only, never text) | S | 3 |
| F-VIEW-03 | Runs: outcome, counts, reason | S | 3 |
| **F-OPS** | **Operating it** | | |
| F-OPS-01 | Deployment as code: Compose and Kubernetes examples, GitOps folder shape | M | 0 |
| F-OPS-02 | Runbook: signed-out, reader broken, Mnemarr refused, recover a lost profile | M | 5 |
| F-OPS-03 | Production deployment on the cluster, by GitOps | M | 5 |

---

## 10. API, settings and health

### 10.1 REST API (`/api/v1`)

Every call but the probes and `system/status` needs the operator's session (vertical 1); every write carries the `X-Geminarr-Request` header (a cross-site form cannot set it). Errors are `application/problem+json`; an unknown path under `/api` is a 404, never the HTML shell.

| Resource | Verbs | Purpose | Vertical |
|---|---|---|---|
| `/system/status` | GET | Version, runtime, uptime | 0 |
| `/health` | GET | The checks with their reason codes | 1 |
| `/auth/login`, `/auth/logout`, `/auth/session` | POST/POST/GET | The operator's session | 1 |
| `/mnemarr` | GET, PUT, DELETE | The destination: address, machine, state, last error | 1 |
| `/mnemarr/pairing` | POST, GET | Start pairing (returns the approval address), poll it | 1 |
| `/connection` | GET | State, since, last ok, reason | 2 |
| `/connection/live-views` | POST, DELETE | Open (returns a single-use token and the path), close | 2 |
| `/connection/pause`, `/connection/resume` | POST | Pause/resume | 2 |
| `/connection/disconnect` | POST | Destroy the profile | 2 |
| `/runs` | GET, POST | List runs; start one now | 3 |
| `/conversations` | GET | List with state and counts (no text) | 3 |
| `/imports` | POST, GET | Upload an archive; list/show imports | 4 |
| `/settings` | GET | Effective settings | 1 |

OpenAPI is served at `/openapi/v1.json`.

### 10.2 Settings

Environment variables prefixed `GEMINARR__` (a double underscore is a level), or files for secrets (`…FILE`). Defaults in bold. Settings that arrive with a vertical are marked.

| Setting | Default | Purpose |
|---|---|---|
| `DATAFOLDER` | **`/config`** | Database, settings, outbox, Mnemarr key |
| `PROFILEFOLDER` *(2)* | **`/profile`** | The browser profile: a volume of its own |
| `PUBLICURL` *(1)* | none | The address of Geminarr's page as the operator reaches it; goes into the `attention` link |
| `AUTH__ADMINUSER`, `AUTH__ADMINPASSWORDFILE`, `AUTH__COOKIESECURITY` *(1)* | none, none, **`Always`** | The first administrator (as Mnemarr) |
| `MNEMARR__URL` *(1)* | none | The Mnemarr to send to (HTTPS) |
| `MNEMARR__MACHINE` *(1)* | **`gemini`** | The machine name |
| `MNEMARR__KEYFILE`, `MNEMARR__ENROLMENTCODEFILE` *(1)* | `<data>/mnemarr/key`, none | See §5.1 |
| `MNEMARR__ALLOWINSECUREHTTP` *(1)* | **`false`** | A sandbox only |
| `READER__POLLMINUTES` *(3)* | **20** | Interval between passes (±20 % jitter) |
| `READER__MAXLISTED`, `READER__MAXOPENPERPASS` *(3)* | **40**, **5** | §6.2 |
| `READER__BACKFILLPERPASS` *(3)* | **10** | §6.3 (0 turns backfill off) |
| `READER__PASSTIMEOUTMINUTES`, `READER__MAXBACKOFFMINUTES`, `READER__RATELIMITPAUSEHOURS` *(3)* | **5**, **360**, **6** | §6.4 |
| `READER__LOGINFAILURES` *(2)* | **3** | Consecutive "not signed in" before `needs-login` |
| `IMPORT__MAXMEGABYTES`, `IMPORTFOLDER` *(4)* | **512**, none | §8 |
| `RETENTION__RUNSDAYS` *(3)* | **90** | How long runs are kept |

Every limit and interval above comes from configuration with that default; none is a literal in the code.

### 10.3 Health and reason codes

`GET /health/live` (the process answers), `GET /health/ready` (it can do its job: the data folder is writable). `GET /api/v1/health` (authenticated) lists the checks; each has a stable `reason` code, and the page maps codes to sentences (it does not parse English).

| Check | Reason codes |
|---|---|
| `data-folder` | `data-folder-missing`, `data-folder-read-only` |
| `database` | `database-unreadable`, `database-step-unknown` |
| `profile` *(2)* | `profile-missing`, `profile-not-private`, `profile-unreadable` |
| `connection` *(2)* | `not-connected`, `needs-login`, `reader-broken`, `rate-limited`, `unreachable`, `paused` |
| `mnemarr` *(1)* | `mnemarr-not-linked`, `mnemarr-refused`, `mnemarr-unreachable`, `mnemarr-insecure-address` |
| `outbox` *(1)* | `outbox-growing` (older than one hour) |

A degraded check never makes `ready` fail (the container is still useful to look at); only the data folder does.

---

## 11. Security and privacy

### 11.1 Assets, by value

1. **The browser profile** = the session of a Google account. Whoever holds it is that account, 2-step verification included.
2. **The Mnemarr key** (ingest-only, bound to the machine).
3. **The conversations** in flight (the outbox) and, in Mnemarr, at rest.
4. The operator's session on Geminarr's page.

### 11.2 Rules that follow (D-08, D-09)

- **The dedicated account** holds only Gemini conversations: no mailbox in use, no saved passwords, no link to the main account (never its recovery address; strong password kept outside Google's password manager; 2-step verification on the phone). A Brand Account does not isolate (it is reached from the main one).
- **The profile** is on its own volume, readable by the container's user only; never in logs, an API response, a health page, a crash dump, a command line or an environment dump; excluded from clear backups; the browser has no extension, sync or password manager. *Disconnect* deletes it.
- **The page and the live view** are behind the proxy's single sign-on and Geminarr's own login; the live view's WebSocket checks origin, token and operator; one viewer at a time.
- **Network egress is a list** ([network.md](network.md)): the assistant's origin, the account provider's sign-in origin (live view only), the configured Mnemarr. The browser's route filter refuses the rest; the container's network policy repeats it.
- **The container** runs non-root, with a read-only root file system, no capabilities, no service-account token. The browser's sandbox needs are stated and minimal (Q-10).
- **Nothing of a conversation is ever logged**; the logs carry ids, counts, durations and reason codes.

### 11.3 Threats

| Threat | Control |
|---|---|
| Theft of the profile (volume read, container escape) | Dedicated account (impact limited to Gemini history); volume permissions; no clear backup; disconnect; revocation from the account's device list |
| Hijack of the live view (cross-site page, a second viewer, replay) | Authenticated POST mints a single-use token; origin check; one viewer; ten minutes; no frames on disk |
| Malicious Takeout archive (zip slip, bomb, symlink) | §8 safety rules; bounds; no extraction by name |
| Leak through a log or an error message | Reason codes only; tests that grep the logs of a run for the text of a turn and for cookie names |
| Theft or misuse of the Mnemarr key | Ingest-only, machine-bound, file mode `0600`, revocable in Mnemarr; HTTPS only |
| SSRF through the Mnemarr address | HTTPS, no redirects, timeouts, the address is operator configuration, never request input |
| The provider locks or limits the account | Read-only, sequential, slow, jittered; backoff; `rate-limited` pause; the account is dedicated, so the impact is bounded |
| A compromised browser binary or dependency | Pinned versions and digests; Dependabot with a cooldown; no new dependency without the maintainer (ADR 0008, as Mnemarr's 0014) |

### 11.4 The one-time import from the main account (D-10, step 3)

Only if Takeout and the extraction from the maintainer's own browser are both shown insufficient. The container then holds the session of the mailbox for the duration, and the automated reading may trigger a verification or a lock of the main account. Conditions: an **ephemeral profile in memory only** (no volume, destroyed when the container stops, never backed up); only during the import, **with the maintainer present**, at a slow pace; the login page behind authentication; afterwards the maintainer **signs out all sessions and removes the device** in the Google account's security page and checks the recent activity.

### 11.5 Accepted

Reading a consumer product by automation is against the provider's terms of use. The risk is accepted **on the dedicated account only**.

---

## 12. Non-functional requirements

| Area | Requirement |
|---|---|
| Footprint | Idle (no browser): ≤ 100 MiB. A pass: browser ≤ 700 MiB, ≤ 1 CPU, ≤ 5 minutes. Container limit 1.5 GiB (Chromium included). |
| Concurrency | One browser, one pass at a time; "run now" during a pass is refused with a clear message. |
| Resilience | Kill at any point loses nothing: the outbox is durable before the browser closes; positions are re-read from Mnemarr. |
| Idempotence | Sending the same batch twice changes nothing (Mnemarr skips the overlap). |
| Time | `TimeProvider` everywhere; no real sleep in tests. |
| Observability | Structured logs (ids, counts, codes); the page shows the last runs. |
| Upgrade | Schema steps are forward-only and tried on a copy in the tests; the profile format is the browser's: a browser upgrade is a version of the image with a runbook line. |
| Portability | Image for `linux/amd64` and `linux/arm64`; the browser binary is chosen by `TARGETARCH` (Dockerfile). |

---

## 13. Question register

| Id | Question | Status |
|---|---|---|
| Q-01 | Can the web product be read headless from the container with a profile created by the live view (no immediate block, no repeated challenge from the container's IP)? | 🧪 spike S1 |
| Q-02 | Is the list ordered by last activity, and does it give anything usable to detect a change without opening (a count, a timestamp)? | 🧪 S1 |
| Q-03 | Read the page (DOM) or the internal calls of the web application? Which is less fragile, and is it stable across a week? | 🧪 S1 |
| Q-04 | How long does the session last, and does the provider bind cookies to the device in a way that breaks a profile kept in a container? | 🧪 S1 (needs days) |
| Q-05 | Do voice conversations appear in the web history **with their text**? Are images and audio references only? | 🧪 S1 |
| Q-06 | What does a Takeout export hold (full replies, ids, dates, conversation grouping)? | 🧪 S2 |
| Q-07 | Rename of a conversation after its first send: ignore (proposed), or let the sender set a title through a Mnemarr API? | ❓ |
| Q-08 | Live view: screencast of the browser over CDP with relayed input (proposed), or a VNC-type view in the container? Lightest that works. | 🧪 S1 |
| Q-09 | A conversation imported from Takeout and later seen by the web reader are two sessions (proposed) or merged by content? | ❓ |
| Q-10 | What does the browser's sandbox need in a restricted pod (user namespaces, `--no-sandbox` with a seccomp profile)? | 🧪 S1 |
| Q-11 | Quality of the search by meaning on informal, spoken-style conversations (the model was measured on project documentation). | 🧪 after vertical 3 |
| Q-12 | Does Mnemarr's start-of-session thread (ADR 0037/0038) stay free of `gemini` conversations once M-2 exists? | ❓ to check in Mnemarr |
| Q-13 | License and visibility of this repository (proposed: public, GPL-3.0-or-later, like the family; the reader code names the product's pages, not its secrets). | ❓ maintainer |

---

## 14. Glossary

| Term | Meaning |
|---|---|
| **Pass** | One visit to the assistant: session check, list, open the changed conversations, send, heartbeat |
| **Reader** | The component that turns the assistant's pages (or an archive) into turns |
| **Sink** | The component that takes turns away (Mnemarr) |
| **Profile** | The browser's folder holding the signed-in session |
| **Live view** | The remote screen of the container's browser, used once to sign in |
| **Outbox** | Batches read but not yet acknowledged by Mnemarr |
| **Position** | The index of a line in a conversation; the unit of idempotence |
| **Attention** | A signal to Mnemarr that a person must act (sign in again) |
| **Dedicated account** | The Google account created only for this, sharing the plan through the family group |

---

## 15. Changelog

| Date | Change |
|---|---|
| 2026-11-11 | v0.1: first draft after the discussion of the day (questions only). |
| 2026-11-11 | v0.2: import ladder (D-10), main-account exception. |
| 2026-11-11 | v0.3: full functional and technical specification: decisions D-01 to D-17, architecture, data model, contract with Mnemarr (`chat-turns`, attention), reader, session and live view, Takeout, features, API, settings, health, security, questions. |
