# Roadmap: from the foundation to Gemini in Mnemarr

> Decided with the maintainer on 2026-11-11. One vertical = one demo sentence = one pull request ([DEVELOPMENT-PROTOCOL.md](DEVELOPMENT-PROTOCOL.md)). The order puts the **unknown first**: nobody knows yet whether and how Gemini can be read from a container, so a spike on the real target comes before the verticals that depend on it.

## Where we are

| Step | State |
|---|---|
| Foundation (vertical 0): repository, pipeline, review agents, skeleton, specification, deployment as code | **Built**, presented |
| Spike S1: read Gemini from a container with a dedicated account | Next (needs the dedicated account) |
| Spike S2: what a Takeout export holds | Next (needs the export) |
| Changes in Mnemarr (M-1 to M-4) | Instructions written ([mnemarr-changes.md](mnemarr-changes.md)); to be done in the Mnemarr workspace |
| Verticals 1 to 5 | Not started |

## Vertical 0 — Foundation *(built)*

*Demo: the image builds and runs; its page says "API reachable" and shows the version; the whole quality chain is green on an empty product.*

- Repository layout, pipeline (lint, tests, e2e smoke in two browsers, Sonar, CodeQL, gitleaks, Trivy, release, image), review agents and skills, documents.
- A walking skeleton: host, health probes, system status, the page shell.
- Deployment as code: Compose and Kubernetes examples, the shape of the GitOps folder.
- Specification, ADRs, this roadmap, the change requests for Mnemarr.

## Spike S1 — Read Gemini from a container *(no pull request)*

*Output: [docs/spikes/s1-reading-gemini.md](spikes/) with the answers to Q-01 to Q-05, Q-08 and Q-10, and the recorded shape of the pages (no real content).*

- On the build machine, one Chromium, capped, behind a login. The maintainer signs in once to the **dedicated** account through a throw-away live view.
- Measure: is the headless browser accepted with that profile; how to detect a signed-out session; how the list is ordered; how a conversation's turns are read (page or internal calls); whether voice conversations come with their text; how long the session lasts (days: the answer comes later).
- Everything lives in `.spike/` and `.profile/` (git-ignored). **Nothing real is committed.** The spike is deleted when its result is written down.

## Spike S2 — What a Takeout export holds

*Output: the answer to Q-06, and a synthetic fixture that has the same shape.*

## Vertical 1 — Operator access and the link to Mnemarr

*Demo: sign in to Geminarr's page; pair it with a Mnemarr; a fake conversation that grows is sent turn by turn and appears, as notes, in that Mnemarr, which shows Geminarr as an online machine; stop Geminarr and Mnemarr shows it silent.*

- F-ACC-01/02, F-LNK-01..04, F-RDR-01 (ports, pipeline, outbox, fake reader), F-SYS-02/03, health with reason codes.
- Needs **M-1** (the format) and **M-3** (the heartbeat) in a Mnemarr build.
- Not now: the browser, the real assistant.

## Vertical 2 — The session and the live view

*Demo: press Sign in; the container's browser appears on the page; sign in to the dedicated account with the phone; the state is Connected; restart the container and it is still connected; Disconnect and the profile is gone.*

- F-CON-01..04, the browser project, the profile volume, the route filter, the Dockerfile stage for the browser by `TARGETARCH`, a smoke test of the arm64 image.
- Not now: reading conversations.

## Vertical 3 — Read the conversations

*Demo: talk to Gemini on the phone, wait one pass, and find the conversation in Mnemarr by describing what it was about.*

- F-RDR-02..07, F-VIEW-02/03: the web reader, candidates, positions, backfill at a slow pace, `reader-broken` and `rate-limited` handling, masking, "run now".
- Validated by the agent on the real target first (rule of `CLAUDE.md`).
- Not now: the Takeout import.

## Vertical 4 — The past, by Takeout

*Demo: give Geminarr the archive; the conversations of the past appear in Mnemarr; give it again and nothing is duplicated.*

- F-IMP-01/02, with the safety rules of SPEC §8.

## Vertical 5 — Run it for real

*Demo: the maintainer lets the session expire (or disconnects), receives the mail, signs in again from the link, and the reading resumes by itself; Geminarr is reached at its address on the cluster behind the single sign-on.*

- Needs **M-3** (the mail) in production; F-OPS-02/03: the runbook, the GitOps folder, the egress policy, the first release (0.1.0), registration in Portarr.

## Not now (parked, not scope)

- Other assistants (a second web product behind the same ports).
- Several Mnemarr destinations (as the collector does).
- Writing to Gemini, notifications other than the mail, a live search inside Geminarr.
- Propagating renames and rewritten turns (Q-07).
