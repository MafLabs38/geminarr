# Security Policy

Geminarr holds **the signed-in session of a Google account** (a browser profile) and reads someone's private conversations. We take security reports seriously.

## Reporting a vulnerability

**Do not open a public issue.** Use GitHub's [private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability) on this repository ("Security" tab, then "Report a vulnerability").

Please include the affected version, reproduction steps and the impact you observed. We aim to acknowledge reports within 7 days.

## Supported versions

Until 1.0, only the latest release receives security fixes.

## Security model (summary)

The full model, with the threats and their controls, is [SPEC §11](docs/SPEC.md#11-security-and-privacy); the decisions are [ADR 0004](docs/adr/0004-a-dedicated-google-account-never-the-main-one.md) and [ADR 0008](docs/adr/0008-the-agent-merges-technical-pull-requests.md).

**The one rule that matters to you as an operator: sign Geminarr in to a dedicated Google account, never to your main one.** A Google session is valid across all the services of an account, mailbox included, and a copy of it bypasses 2-step verification. A leak of this container must reach nothing but Gemini conversations.

**True today** (foundation, operator access, the link to Mnemarr)

- **Closed by default** ([ADR 0011](docs/adr/0011-one-operator-a-session-in-memory-and-everything-closed-but-the-probes.md)). Anonymous are the probes (`/health/live`, `/health/ready`), the static shell of the interface and the sign-in call. **Every other call, the API description included, needs a session; an unknown API path is a 401 too.** A fallback policy requires it everywhere, so an endpoint nobody thought about is closed.
- **One operator**: a name and a password in a mounted file (at least twelve characters, read at each sign-in). Without them nobody can sign in: the container stays alive and closed. The comparison takes the same time whatever is wrong, a wrong name and a wrong password are told the same, and **five wrong passwords lock a name for fifteen minutes** (counted per name whether it exists or not, in memory).
- **The session** is a cookie (`HttpOnly`, `SameSite=Strict`, `Secure` and `__Host-` prefixed unless a sandbox says otherwise, no expiry date) backed by a session **in memory** with an absolute lifetime (12 hours by default). Signing out removes it on the server, so a copied cookie is dead at once; a restart ends every session (the keys are in memory too).
- **A write needs the `X-Geminarr-Request` header**, checked by the server before the sign-in (a cross-site form or link cannot send it), on top of `SameSite=Strict`.
- **A request for a `Host` that is not allowed is refused** (DNS rebinding), the probes excepted (a platform calls them by the address of the pod): `GEMINARR__ALLOWEDHOSTNAMES` and the host of `GEMINARR__PUBLICURL`.
- The page *Settings* never shows a secret, nor the path of its file, nor the name of the operator; the health says what is wrong with a stable code and never a path or an exception.
- Every response, errors included, carries conservative security headers (nosniff, no framing, no referrer, a Content-Security-Policy that lets everything come from the application only); the API and its description are never cached; an unknown path under `/api` is never the HTML shell; an error never shows its cause; a body larger than a sign-in can be is a 413.
- The image runs as a non-root user with no shell; the deployment examples run it with a read-only root file system, no capability, no service-account token and an ingress-only network policy.
- The CI scans for secrets and vulnerable dependencies, and the code is analysed by CodeQL and SonarQube; Dependabot waits a week before taking a new version.
- **The Mnemarr key** (vertical 2) is ingest-only and bound to the machine, and is **a file** (mode `0600`, written by a move over the old one) that is never in the database, a log, an answer of the API or the page; it goes only to the address that was configured, over HTTPS (a loopback address or an explicit sandbox switch excepted), redirects are not followed, every call is bounded, and a failure says what kind it was and never what was sent or where. Unlinking forgets it.
- **What waits for Mnemarr** is the only text of a conversation that Geminarr ever holds, in a database file that only its owner reads, until Mnemarr acknowledges it; the database has no column for a turn, and **no log line holds a title or a turn** (counts, codes and durations only).
- **The session is made only through the live view** (vertical 3, [ADR 0015](docs/adr/0015-the-browser-the-live-view-and-the-profile.md)): one Chromium on a profile folder of its own (mode `0700`), no sync, no extension, no saved password. **A route filter refuses every origin but the assistant's** (and its files' hosts); the account provider's sign-in pages load only during a live view. The live view is opened with a single-use token (used up by the first attempt, void after a minute), checked with the operator's session and the `Origin`, one viewer at a time, ten minutes at most; what the page sends is bounded and checked again; nothing shown or typed is logged or kept. *Disconnect* destroys the profile.
- It still **reads nothing real** (a demonstration reader with invented conversations is a setting for a sandbox), holds a Google session only if the operator signs one in: the conversations come with the next vertical, behind this access.

**The target model** (decided, not yet built)

- **Chromium runs without its own sandbox unless the platform allows it** (`GEMINARR__BROWSER__CHROMIUMSANDBOX`, [ADR 0015](docs/adr/0015-the-browser-the-live-view-and-the-profile.md) §8): a hardened container cannot give it the namespaces it needs. What holds instead is the list of origins (a page is navigated to only if it is the assistant's, or a sign-in page during a live view; WebSockets are filtered too), the dedicated account, the non-root container with no capability and the network policy.
- **The profile** (the session) lives on a volume of its own, mode `0700`, readable by the container's user only; never in logs, an API response, a health page, a crash dump or a backup in the clear; no extension, no sync, no saved password in the browser; *Disconnect* destroys it.
- **Read-only, on the assistant's own pages.** The browser has a route filter that loads no other origin (the account provider's sign-in only, during the live view); the container's network policy repeats the list ([network.md](docs/network.md)).
- **The live view** (where the operator signs in) is behind the proxy's single sign-on and Geminarr's own sign-in (D-19), opened by an authenticated call that mints a single-use token, checked for origin, one viewer at a time, ten minutes at most; its frames never reach a log or a disk.
- **Secrets are masked before anything leaves the container**, and again by Mnemarr (with the reader that reads something real, vertical 4).
- **A Takeout archive is hostile input**: read without extracting by name, with bounds on entries and sizes.
- **Geminarr sends no mail** and holds no mail credential: it signals Mnemarr, which writes to the operator from its own mailbox, pointing to Mnemarr's own page and never repeating anything Geminarr wrote ([ADR 0012](docs/adr/0012-the-attention-mail-points-to-mnemarr-not-to-the-satellite.md)).
