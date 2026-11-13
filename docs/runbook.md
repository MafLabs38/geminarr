# Runbook

> Operating Geminarr: what to watch, and what to do when it is degraded. It grows with the verticals; a failure mode is added here **in the pull request that introduces it** (the architecture reviewer checks).

## What to watch

| Where | What it says |
|---|---|
| `GET /health/ready` | The container can do its job (the data folder is writable). Used by the platform's probe. |
| The overview of the page | The state of the connection, of the link to Mnemarr, and the last runs *(from the verticals that add them)* |
| Mnemarr's machines page | Geminarr as an `always-on` machine: `online`, or `failing` with its sentence, or `silent` after 30 minutes without a heartbeat |
| The mail from Mnemarr | "Geminarr needs you to sign in again" (vertical 6), with a link to Mnemarr's Machines page, where the link to Geminarr's page is |

## Reasons and what to do

Codes are in [SPEC.md §10.3](SPEC.md#103-health-and-reason-codes). Until the health resource of vertical 1 exists, `/health/ready` answers only *Unhealthy*: the container's log says it on the line "Health check data-folder" ("The data folder does not exist." or "The data folder is not writable."); the code is the one of the table.

| Reason | Meaning | Do |
|---|---|---|
| `data-folder-missing` / `data-folder-read-only` | The volume is not mounted or not writable by uid 1654 | Fix the mount or the volume's ownership; the readiness probe recovers within 15 seconds plus one probe period (its result is reused for 15 s) |
| `needs-login` | The session expired (or the provider asked for a new verification) | Open the link of the mail, press *Sign in*, sign in to the **dedicated** account on the live view, approve on the phone |
| `reader-broken` | The assistant's page no longer looks like what the reader knows | Nothing is lost (the outbox is kept, the position stays). Update Geminarr to a version of the reader that knows the new page; until then it only signals |
| `rate-limited` | The provider limited or challenged the session | Geminarr pauses by itself (six hours by default); look at the account's security page; do not shorten the pause |
| `unreachable` | The provider or the network is down | Wait: it backs off by itself |
| `mnemarr-refused` | The key was revoked or the machine removed in Mnemarr | Pair again (page *Mnemarr*) |
| `mnemarr-unreachable` | Mnemarr does not answer | Wait; the outbox keeps the turns. `outbox-growing` appears after an hour |
| `operator-not-configured` | No name and password file: nobody can sign in, everything but the probes is closed | Set `GEMINARR__AUTH__ADMINUSER` and `…ADMINPASSWORDFILE` in the manifest and restart |
| `operator-password-unreadable` | The password file is missing, empty or not readable by uid 1654 | Mount it, with the right permissions; it is read at each sign-in, no restart needed |
| `operator-password-weak` | The password has fewer than twelve characters: it is refused, not accepted | Put a longer one in the file |
| `profile-not-private` | The profile folder is readable by others | Fix the permissions (`0700`, the container's user) before anything else |

## Recover

- **A lost profile** (volume lost, *Disconnect*, a restore): the state is `not-connected`; sign in again through the live view. Nothing else is lost: positions come from Mnemarr.
- **A restored Mnemarr** (fewer lines than Geminarr believes it sent): Geminarr goes back to Mnemarr's positions by itself and sends again what is missing.
- **A suspected leak of the profile**: *Disconnect* in Geminarr, then in the dedicated Google account's security page sign out of all sessions and remove the device, change its password; only then sign in again. Revoke the machine in Mnemarr if the key may have leaked too.
- **A stuck pass**: it stops at `PassTimeoutMinutes` by itself; "Run now" while one runs is refused.
- **Upgrade of the browser** (a new image version): the profile is the browser's own format; if the state turns `needs-login` after an upgrade, sign in again and write the version here.

## Spike hygiene (while a spike is going on)

`.spike/` and `.profile/` are git-ignored and **hold a signed-in session and real conversations**: they live on disk (not in `/tmp`, which is memory on the build machine), are deleted when the spike's result is written down, and a spike's findings written in `docs/spikes/` contain the *shape* of the pages and never their content.

## Signing in

- **Locked out** (HTTP 429 after five wrong passwords, for fifteen minutes): wait, or restart the container (the lockout is in memory). If it happens without you having tried, someone is guessing the name: change it (`GEMINARR__AUTH__ADMINUSER`).
- **Rotate the password**: replace the content of the file; the next sign-in uses it, and sessions that are open stay valid until their end. **If the password leaked, restart the container after changing it: that ends every session.**
- **Nobody can sign in and the page only says "wrong"**: the log of the container says why at its start (`Nobody can sign in (NotConfigured | PasswordUnreadable | PasswordWeak)`), and the reason codes above say what to do. Each refused sign-in and each lockout is in the log too, without the name or the password typed.
- **A page that answers 400 "The host name is not allowed"**: add the name or the address you reach it by to `GEMINARR__ALLOWEDHOSTNAMES` (or set `GEMINARR__PUBLICURL`).
- **Signed out all of a sudden**: the container restarted (sessions are in memory), or the 12 hours passed.
