# Runbook

> Operating Geminarr: what to watch, and what to do when it is degraded. It grows with the verticals; a failure mode is added here **in the pull request that introduces it** (the architecture reviewer checks).

## What to watch

| Where | What it says |
|---|---|
| `GET /health/ready` | The container can do its job (the data folder is writable). Used by the platform's probe. |
| The overview of the page | The state of the connection, of the link to Mnemarr, and the last runs *(from the verticals that add them)* |
| Mnemarr's machines page | Geminarr as an `always-on` machine: `online`, or `failing` with its sentence, or `silent` after 30 minutes without a heartbeat |
| The mail from Mnemarr | "Geminarr needs you to sign in again" (vertical 5), with a link to the page |

## Reasons and what to do

Codes are in [SPEC.md §10.3](SPEC.md#103-health-and-reason-codes). Until the health resource of vertical 1 exists, `/health/ready` answers only *Unhealthy*: the reason code is in the container's log, on the line "Health check data-folder".

| Reason | Meaning | Do |
|---|---|---|
| `data-folder-missing` / `data-folder-read-only` | The volume is not mounted or not writable by uid 1654 | Fix the mount or the volume's ownership; the readiness probe recovers within 15 seconds plus one probe period (its result is reused for 15 s) |
| `needs-login` | The session expired (or the provider asked for a new verification) | Open the link of the mail, press *Sign in*, sign in to the **dedicated** account on the live view, approve on the phone |
| `reader-broken` | The assistant's page no longer looks like what the reader knows | Nothing is lost (the outbox is kept, the position stays). Update Geminarr to a version of the reader that knows the new page; until then it only signals |
| `rate-limited` | The provider limited or challenged the session | Geminarr pauses by itself (six hours by default); look at the account's security page; do not shorten the pause |
| `unreachable` | The provider or the network is down | Wait: it backs off by itself |
| `mnemarr-refused` | The key was revoked or the machine removed in Mnemarr | Pair again (page *Mnemarr*) |
| `mnemarr-unreachable` | Mnemarr does not answer | Wait; the outbox keeps the turns. `outbox-growing` appears after an hour |
| `profile-not-private` | The profile folder is readable by others | Fix the permissions (`0700`, the container's user) before anything else |

## Recover

- **A lost profile** (volume lost, *Disconnect*, a restore): the state is `not-connected`; sign in again through the live view. Nothing else is lost: positions come from Mnemarr.
- **A restored Mnemarr** (fewer lines than Geminarr believes it sent): Geminarr goes back to Mnemarr's positions by itself and sends again what is missing.
- **A suspected leak of the profile**: *Disconnect* in Geminarr, then in the dedicated Google account's security page sign out of all sessions and remove the device, change its password; only then sign in again. Revoke the machine in Mnemarr if the key may have leaked too.
- **A stuck pass**: it stops at `PassTimeoutMinutes` by itself; "Run now" while one runs is refused.
- **Upgrade of the browser** (a new image version): the profile is the browser's own format; if the state turns `needs-login` after an upgrade, sign in again and write the version here.

## Spike hygiene (while a spike is going on)

`.spike/` and `.profile/` are git-ignored and **hold a signed-in session and real conversations**: they live on disk (not in `/tmp`, which is memory on the build machine), are deleted when the spike's result is written down, and a spike's findings written in `docs/spikes/` contain the *shape* of the pages and never their content.
