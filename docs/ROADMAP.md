# Roadmap: from the foundation to Gemini in Mnemarr

> Decided with the maintainer on 2026-11-11, re-cut on 2026-11-13 once Mnemarr's side was delivered. One vertical = one demo sentence = one pull request ([DEVELOPMENT-PROTOCOL.md](DEVELOPMENT-PROTOCOL.md)). The order puts the **unknown first**: nobody knows yet whether and how Gemini can be read from a container, so a spike on the real target comes before the verticals that depend on it.
>
> **Numbering.** The first cut put access and the link to Mnemarr in one vertical; the protocol says that "and" is two. The ADRs written before 2026-11-13 say "vertical N" with the old numbers (the browser was 2, the reader 3): **this page is the reference**.

## Where we are

| Step                                                                                                                                 | State                                                                                                                                                                                                                |
| ------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Vertical 0: foundation (repository, pipeline, review agents, skeleton, specification, deployment as code)                            | **Built, merged**                                                                                                                                                                                                    |
| Mnemarr's changes (M-1 to M-4 and the mail), see [mnemarr-changes.md](mnemarr-changes.md)                                            | **Delivered** (Mnemarr 0.4.0; production runs 0.6.0)                                                                                                                                                                 |
| Vertical 1: operator access                                                                                                          | **Built**, in review                                                                                                                                                                                                 |
| Vertical 1b: identity of the environment (name, picture, version in the menu; F-SYS-04)                                              | **Built**, in review                                                                                                                                                                                                 |
| Spike S1: read Gemini from a container with a dedicated account                                                                      | **Done**: the answer is an extension in the browser a person signed in to ([ADR 0022](adr/0022-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md)); the debugging-port route was tried and rejected |
| Spike S2: what a Takeout export holds                                                                                                | Waits for the export                                                                                                                                                                                                 |
| Vertical 2: the link to Mnemarr                                                                                                      | **Built**, in review                                                                                                                                                                                                 |
| Vertical 3: Gemini is read by an extension, and the sign-in through it                                                               | **Built**, in review                                                                                                                                                                                                 |
| Vertical 3 (browser): the browser container and the connection to it ([ADR 0016](adr/0016-the-browser-is-a-container-of-its-own.md)) | **Built**, merged; the debugging port is gone ([ADR 0021](adr/0021-geminarr-has-no-debugging-port-to-the-browser.md))                                                                                                |
| Vertical 4: sign in from Geminarr's page                                                                                             | **Built** with the extension (who is signed in, the end of a sign-in within a minute, Disconnect)                                                                                                                    |
| Vertical 5: backfill, the Runs page (and the Conversations page)                                                                      | **Built**, in review ([ADR 0024](adr/0024-the-history-is-walked-slowly-and-its-end-is-told-by-the-list.md))                                                                                                                                                                                                  |
| Verticals 6 and 7                                                                                                                    | Not started                                                                                                                                                                                                          |

## Vertical 0 — Foundation _(built)_

_Demo: the image builds and runs; its page says "API reachable" and shows the version; the whole quality chain is green on an empty product._

## Vertical 1 — Operator access _(built)_

_Demo: open the page on the build machine: the sign-in is the first screen and nothing of the application is behind it; sign in as the operator, see the health of the container with the sentence of each reason code, the settings with the secrets hidden; sign out, and a copy of the cookie is dead; five wrong passwords lock the name; a request for another host name is refused; the API description is closed._

- F-ACC-01 to F-ACC-04, F-SYS-02, F-SYS-03 ([ADR 0011](adr/0011-one-operator-a-session-in-memory-and-everything-closed-but-the-probes.md)); the two findings of the first review that were deferred here (R-1-10, R-1-12) are closed.
- Not now: Mnemarr, the browser, the reader.

## Vertical 1b — The identity of the environment _(built)_

_Demo: the menu ends with the name of the laboratory, its picture and the version, as Mnemarr's and Portarr's do; the picture is a file the deployment mounts._ [ADR 0013](adr/0013-the-menu-says-which-environment-it-is-with-a-name-a-logo-and-the-version.md). The Settings page says why a picture is not used.

## Spike S1 — Read Gemini from a container _(no pull request)_

_Output: [docs/spikes/s1-reading-gemini.md](spikes/) with the answers to Q-01 to Q-05, Q-08 and Q-10, and the recorded shape of the pages (no real content)._

- On the build machine, one Chromium, capped, behind the sign-in of vertical 1. The maintainer signs in once to the **dedicated** account through a throw-away live view.
- Measure: is the headless browser accepted with that profile; how to detect a signed-out session; how the list is ordered; how a conversation's turns are read (page or internal calls); whether voice conversations come with their text; how long the session lasts (days: the answer comes later).
- Everything lives in `.spike/` and `.profile/` (git-ignored). **Nothing real is committed.** The spike is deleted when its result is written down.

## Spike S2 — What a Takeout export holds

_Output: the answer to Q-06, and a synthetic fixture that has the same shape._

## Vertical 2 — The link to Mnemarr _(built)_

_Demo: pair Geminarr with a Mnemarr (or give it a one-time code); it appears online on Mnemarr's Machines page; a **demonstration reader** (a setting for a sandbox only) makes a fake conversation grow, its turns arrive in Mnemarr one batch after the other and are found by meaning; stop Geminarr and Mnemarr shows it silent; make the demonstration reader say that its session ended: Mnemarr shows the sentence and the link, and the mail goes to a catcher._

- F-LNK-01..04, F-RDR-01 (the ports, the pipeline, the outbox, the fake reader), the database and its numbered schema steps, the heartbeat with `attention`. Against Mnemarr 0.4.0 or later (the contract of [SPEC §5](SPEC.md#5-the-contract-with-mnemarr) was checked against a real 0.6.0).
- Built with the demonstration reader (a sandbox setting, `GEMINARR__READER__KIND=Demo`) and proved on a real Mnemarr (pairing, 3 000 lines, found by search). [ADR 0014](adr/0014-the-pass-the-outbox-and-mnemarrs-positions.md). **"Run now" and the list of passes are here** (they were planned for vertical 4: the demonstration needs them); the full _Runs_ page is still vertical 4.
- Not now: the browser, the real assistant, masking (nothing real is read yet).

## Vertical 3 — Gemini is read by an extension _(built)_

_Demo: a browser that a person signed in to Gemini in carries the extension (installed by its policy); within one round, the conversations of the page are in Mnemarr, turn by turn, and found by describing what they were about; a conversation added on the phone arrives with the next round; sign out of Gemini in that browser and the page says "sign in again"._

- F-RDR-02..06 for the web reader (the masking of secrets before the outbox, with the samples shared with Mnemarr), in the form of [ADR 0022](adr/0022-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md): the extension (`extension/`), its signed package served by Geminarr, the push route and its token, `extension-silent`, the candidates and positions of ADR 0014.
- Validated by the agent on the real target first (rule 13 of `CLAUDE.md`): a signed-in profile read through the policy-installed, signed package; and end to end in CI with a real Chromium, a fake assistant and a fake Mnemarr.
- Not now: backfill at a slow pace (vertical 5), the voice conversations' text and a very long list (measured with the real use).

## Vertical 3 — The browser container and the connection to it

_Demo: press *Sign in* on Geminarr's page (the browser's desktop opens in a window, on an origin of its own opened by a ticket, no second login), sign in to the dedicated account by hand, and Geminarr's page says Connected; restart both containers and it is still connected; disconnect (the session of the browser is cleared) and it says not connected._

- ADR 0016. F-CON-01..04: the browser container and its web desktop relayed (`GEMINARR__BROWSER__DESKTOPURL`), the state machine, pause, the health of the browser. No debugging port (ADR 0021): the session state, the account and _Disconnect_ are the extension's (ADR 0022).
- **The browser container in the infrastructure as code and in every deployment file, in this pull request**: `deploy/` (Compose and Kubernetes, its volume, its limits, the shared network namespace so that the debugging port never leaves the pair), `docs/DEPLOYMENT.md`, `docs/network.md`, `docs/configuration.md`, `docs/runbook.md`, the spec, the maintainer's GitOps folder and Portarr; the browser has no sub-domain, no port and no login of its own: Geminarr relays its web desktop on an origin of its own (a second port of Geminarr, opened by a single-use ticket of the operator's session, no second password: ADR 0017, 0019).
- Spike S1 starts here: the first read of Gemini on a throw-away account, then on the dedicated one.
- Not now: reading conversations (vertical 4).

## Vertical 4 — Sign in from Geminarr's page _(built with the extension)_

_Demo: press Sign in on Geminarr's Connection page; the browser's desktop appears on the page; sign in to the dedicated account with the phone; the state is Connected; Disconnect and the session is gone._

- F-CON-01..04: the desktop of the browser relayed on an origin of its own behind a single-use ticket of the operator's session, the two containers (the browser and Geminarr) in the examples, the Connection page.
- Who is signed in, the end of a sign-in within a minute and _Disconnect_ are the extension's (§7.3 of the spec, [ADR 0022](adr/0022-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md)): a heartbeat every half minute, the label of the account button, the deletion of the cookies of the browser.
- Not now: the Takeout import.

## Vertical 5 — Backfill, the Runs page _(built)_

_Demo: the first pass walks the older conversations at a slow pace until the end of the list, and the Runs page shows what each pass did._

- F-RDR-04/07, F-VIEW-02/03: backfill, the Runs page. [ADR 0024](adr/0024-the-history-is-walked-slowly-and-its-end-is-told-by-the-list.md): a few older conversations a pass, no stored flag, the list says whether it ends there, the pages of an answer opened one after the other; the _Conversations_ page (metadata only, with the progress of the history) and the _Runs_ page.
- Not now: `rate-limited` and its pause of six hours (F-RDR-05, the failure classes are those of the pass today), a very long list (the Takeout import).

## Vertical 6 — The past, by Takeout

_Demo: give Geminarr the archive; the conversations of the past appear in Mnemarr; give it again and nothing is duplicated._

- F-IMP-01/02, with the safety rules of SPEC §8.

## Vertical 7 — Run it for real

_Demo: the maintainer lets the session expire (or disconnects), receives the mail from Mnemarr, follows it to Mnemarr's Machines page and from there to Geminarr, signs in again, and the reading resumes by itself; Geminarr is reached at its address on the cluster behind the single sign-on._

- F-OPS-02/03: the runbook, the GitOps folder, the egress policy (the example holds DNS and Mnemarr; the assistant's hosts join it with the list that spike S1 measures), the first release (0.1.0), registration in Portarr.

## Not now (parked, not scope)

- Other assistants (a second web product behind the same ports).
- Several Mnemarr destinations (as the collector does).
- Writing to Gemini, notifications other than Mnemarr's mail, a live search inside Geminarr.
- Propagating renames and rewritten turns (Q-07; Mnemarr's M-5, optional).
