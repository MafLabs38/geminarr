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

**True today** (foundation)

- The container answers its probes, a version and an uptime, and serves its page. It reads nothing, holds no session and no key.
- **No sign-in exists yet**, so publish it on the loopback address only (the examples do). Operator access (a first administrator from a password file, a session cookie, the header of writes) is the first vertical and comes **before** any session or conversation exists.
- Every response, errors included, carries conservative security headers (nosniff, no framing, no referrer, a Content-Security-Policy that lets everything come from the application only); the API is never cached; an unknown path under `/api` is a 404 and never the HTML shell; an error never shows its cause.
- The image runs as a non-root user with no shell; the deployment examples run it with a read-only root file system, no capability and no service-account token.
- The CI scans for secrets and vulnerable dependencies, and the code is analysed by CodeQL and SonarQube; Dependabot waits a week before taking a new version.

**The target model** (decided, not yet built)

- **The profile** (the session) lives on a volume of its own, mode `0700`, readable by the container's user only; never in logs, an API response, a health page, a crash dump or a backup in the clear; no extension, no sync, no saved password in the browser; *Disconnect* destroys it.
- **Read-only, on the assistant's own pages.** The browser has a route filter that loads no other origin (the account provider's sign-in only, during the live view); the container's network policy repeats the list ([network.md](docs/network.md)).
- **The live view** (where the operator signs in) is behind the proxy's single sign-on and Geminarr's own login, opened by an authenticated call that mints a single-use token, checked for origin, one viewer at a time, ten minutes at most; its frames never reach a log or a disk.
- **The Mnemarr key** is ingest-only and bound to the machine, in a file only the container's user can read, over HTTPS without redirects.
- **No second copy** of a conversation, and **nothing a conversation said is ever logged**; secrets are masked before anything leaves the container, and again by Mnemarr.
- **A Takeout archive is hostile input**: read without extracting by name, with bounds on entries and sizes.
- **Geminarr sends no mail** and holds no mail credential: it signals Mnemarr, which writes to the operator a link and never a token.
