# Roadmap: from the foundation to Gemini in Mnemarr

> Decided with the maintainer on 2026-11-11, re-cut on 2026-11-13 once Mnemarr's side was delivered. One vertical = one demo sentence = one pull request ([DEVELOPMENT-PROTOCOL.md](DEVELOPMENT-PROTOCOL.md)). The order puts the **unknown first**: nobody knows yet whether and how Gemini can be read from a container, so a spike on the real target comes before the verticals that depend on it.
>
> **Numbering.** The first cut put access and the link to Mnemarr in one vertical; the protocol says that "and" is two. The ADRs written before 2026-11-13 say "vertical N" with the old numbers (the browser was 2, the reader 3): **this page is the reference**.

## Where we are

| Step | State |
|---|---|
| Vertical 0: foundation (repository, pipeline, review agents, skeleton, specification, deployment as code) | **Built, merged** |
| Mnemarr's changes (M-1 to M-4 and the mail), see [mnemarr-changes.md](mnemarr-changes.md) | **Delivered** (Mnemarr 0.4.0; production runs 0.6.0) |
| Vertical 1: operator access | **Built**, in review |
| Vertical 1b: identity of the environment (name, picture, version in the menu; F-SYS-04) | **Built**, in review |
| Spike S1: read Gemini from a container with a dedicated account | **Done**: the answer is an extension in the browser a person signed in to ([ADR 0015](adr/0015-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md)); the debugging-port route was tried and rejected |
| Spike S2: what a Takeout export holds | Waits for the export |
| Vertical 2: the link to Mnemarr | **Built**, in review |
| Vertical 3: Gemini is read by an extension | **Built**, in review |
| Verticals 4 to 7 | Not started |

## Vertical 0 — Foundation *(built)*

*Demo: the image builds and runs; its page says "API reachable" and shows the version; the whole quality chain is green on an empty product.*

## Vertical 1 — Operator access *(built)*

*Demo: open the page on the build machine: the sign-in is the first screen and nothing of the application is behind it; sign in as the operator, see the health of the container with the sentence of each reason code, the settings with the secrets hidden; sign out, and a copy of the cookie is dead; five wrong passwords lock the name; a request for another host name is refused; the API description is closed.*

- F-ACC-01 to F-ACC-04, F-SYS-02, F-SYS-03 ([ADR 0011](adr/0011-one-operator-a-session-in-memory-and-everything-closed-but-the-probes.md)); the two findings of the first review that were deferred here (R-1-10, R-1-12) are closed.
- Not now: Mnemarr, the browser, the reader.

## Vertical 1b — The identity of the environment *(built)*

*Demo: the menu ends with the name of the laboratory, its picture and the version, as Mnemarr's and Portarr's do; the picture is a file the deployment mounts.* [ADR 0013](adr/0013-the-menu-says-which-environment-it-is-with-a-name-a-logo-and-the-version.md). The Settings page says why a picture is not used.

## Spike S1 — Read Gemini from a container *(no pull request)*

*Output: [docs/spikes/s1-reading-gemini.md](spikes/) with the answers to Q-01 to Q-05, Q-08 and Q-10, and the recorded shape of the pages (no real content).*

- On the build machine, one Chromium, capped, behind the sign-in of vertical 1. The maintainer signs in once to the **dedicated** account through a throw-away live view.
- Measure: is the headless browser accepted with that profile; how to detect a signed-out session; how the list is ordered; how a conversation's turns are read (page or internal calls); whether voice conversations come with their text; how long the session lasts (days: the answer comes later).
- Everything lives in `.spike/` and `.profile/` (git-ignored). **Nothing real is committed.** The spike is deleted when its result is written down.

## Spike S2 — What a Takeout export holds

*Output: the answer to Q-06, and a synthetic fixture that has the same shape.*

## Vertical 2 — The link to Mnemarr *(built)*

*Demo: pair Geminarr with a Mnemarr (or give it a one-time code); it appears online on Mnemarr's Machines page; a **demonstration reader** (a setting for a sandbox only) makes a fake conversation grow, its turns arrive in Mnemarr one batch after the other and are found by meaning; stop Geminarr and Mnemarr shows it silent; make the demonstration reader say that its session ended: Mnemarr shows the sentence and the link, and the mail goes to a catcher.*

- F-LNK-01..04, F-RDR-01 (the ports, the pipeline, the outbox, the fake reader), the database and its numbered schema steps, the heartbeat with `attention`. Against Mnemarr 0.4.0 or later (the contract of [SPEC §5](SPEC.md#5-the-contract-with-mnemarr) was checked against a real 0.6.0).
- Built with the demonstration reader (a sandbox setting, `GEMINARR__READER__KIND=Demo`) and proved on a real Mnemarr (pairing, 3 000 lines, found by search). [ADR 0014](adr/0014-the-pass-the-outbox-and-mnemarrs-positions.md). **"Run now" and the list of passes are here** (they were planned for vertical 4: the demonstration needs them); the full *Runs* page is still vertical 4.
- Not now: the browser, the real assistant, masking (nothing real is read yet).

## Vertical 3 — Gemini is read by an extension *(built)*

*Demo: a browser that a person signed in to Gemini in carries the extension (installed by its policy); within one round, the conversations of the page are in Mnemarr, turn by turn, and found by describing what they were about; a conversation added on the phone arrives with the next round; sign out of Gemini in that browser and the page says "sign in again".*

- F-RDR-02..05 for the web reader, in the form of [ADR 0015](adr/0015-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md): the extension (`extension/`), its signed package served by Geminarr, the push route and its token, `extension-silent`, the candidates and positions of ADR 0014.
- Validated by the agent on the real target first (rule 13 of `CLAUDE.md`): a signed-in profile read through the policy-installed, signed package; and end to end in CI with a real Chromium, a fake assistant and a fake Mnemarr.
- Not now: signing in from Geminarr's page (vertical 4), masking and backfill at a slow pace (vertical 5: until then the web reader relies on Mnemarr's masking alone, said in SECURITY.md and configuration.md), the voice conversations' text and a very long list (measured with the real use).

## Vertical 4 — Sign in from Geminarr's page

*Demo: press Sign in on Geminarr's Connection page; the browser's desktop appears on the page; sign in to the dedicated account with the phone; the state is Connected; Disconnect and the session is gone.*

- F-CON-01..04: the desktop of the browser relayed on an origin of its own behind a single-use ticket of the operator's session, the two containers (the browser and Geminarr) in the examples, the Connection page.
- Not now: reading (vertical 3), the Takeout import.

## Vertical 5 — Masking, backfill, the Runs page

*Demo: the first pass walks the older conversations at a slow pace until the end of the list; a secret typed in a conversation never reaches Mnemarr in the clear.*

- F-RDR-04/07, F-VIEW-02/03: backfill, masking before the outbox, the Runs page.

## Vertical 6 — The past, by Takeout

*Demo: give Geminarr the archive; the conversations of the past appear in Mnemarr; give it again and nothing is duplicated.*

- F-IMP-01/02, with the safety rules of SPEC §8.

## Vertical 7 — Run it for real

*Demo: the maintainer lets the session expire (or disconnects), receives the mail from Mnemarr, follows it to Mnemarr's Machines page and from there to Geminarr, signs in again, and the reading resumes by itself; Geminarr is reached at its address on the cluster behind the single sign-on.*

- F-OPS-02/03: the runbook, the GitOps folder, the egress policy, the first release (0.1.0), registration in Portarr.

## Not now (parked, not scope)

- Other assistants (a second web product behind the same ports).
- Several Mnemarr destinations (as the collector does).
- Writing to Gemini, notifications other than Mnemarr's mail, a live search inside Geminarr.
- Propagating renames and rewritten turns (Q-07; Mnemarr's M-5, optional).
