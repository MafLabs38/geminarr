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
| `needs-login` | The session expired (or the provider asked for a new verification) | Open the link of the mail, or the page *Connection* and *Open the browser*, and **sign in by hand** to the **dedicated** account in the browser's page (approve on the phone if asked); the next pass finds it |
| `browser-failed` | The browser container could not be reached: not started, the debugging address wrong (`GEMINARR__BROWSER__CDPURL`), the two containers not sharing a network namespace, or a limit killed it | Look at the log of the browser container and of Geminarr (the kind of failure is named, never an address); start it, fix the address or the limit. Nobody is asked to sign in, and Mnemarr is not asked for attention |
| `reader-broken` | The assistant's page no longer looks like what the reader knows | Nothing is lost (the outbox is kept, the position stays). Update Geminarr to a version of the reader that knows the new page; until then it only signals |
| `rate-limited` | The provider limited or challenged the session | Geminarr pauses by itself (six hours by default); look at the account's security page; do not shorten the pause |
| `unreachable` | The provider or the network is down | Wait: it backs off by itself |
| `not-connected` | No reader or no browser is configured (the default), or *Disconnect* has just forgotten the connection: nothing is read, and the machine still says it is alive to Mnemarr. With a browser configured and nobody signed in, the state is `needs-login` (a person signs in by hand). A connection that had worked and loses its profile (the volume replaced, the folder emptied) is `needs-login`, not this | Nothing, until the reader of the assistant is set (`GEMINARR__READER__KIND`) |
| `internal-error` (a run) | Something this version did not foresee ended the pass; the next one tries again, and the log names the kind of error | Update Geminarr; if it stays, open an issue with the kind of error (never what was read) |
| `pass-timeout` / `stopped` (a run) | Reading ran out of its time (the reader hangs: it counts as `unreachable` and backs off), or the container was stopping | `pass-timeout`: wait; `stopped`: nothing, the next pass resumes |
| `mnemarr-refused` | The key was revoked or the machine removed in Mnemarr | Pair again (page *Mnemarr*) |
| `mnemarr-unreachable` | Mnemarr does not answer | Wait; the outbox keeps the turns. `outbox-growing` appears after an hour |
| `operator-not-configured` | No name and password file: nobody can sign in, everything but the probes is closed | Set `GEMINARR__AUTH__ADMINUSER` and `…ADMINPASSWORDFILE` in the manifest and restart |
| `operator-password-unreadable` | The password file is missing, empty or not readable by uid 1654 | Mount it, with the right permissions; it is read at each sign-in, no restart needed |
| *(the container does not start, and its log names `GEMINARR__IDENTITY__…`)* | The name of the environment is longer than 40 characters or has a line break, or a color is not `#rgb`, `#rrggbb` or a theme token | Correct the setting: a typo in a name must not be a silent default |
| `database-unreadable` / `database-step-unknown` | The database cannot be opened (the volume, its permissions), or was written by a newer version | Fix the volume; for a newer database, run the version that wrote it, or restore a copy. The readiness probe fails: the container is not sent traffic until it is fixed |
| `mnemarr-not-linked` | No address, or no key | On the page *Mnemarr*: give the address, then *Pair with Mnemarr* and approve the code **in Mnemarr, as an always-on machine**; or give a one-time code file and restart |
| `mnemarr-refused` | Mnemarr says the key is not good (revoked, machine removed) | Unlink, then pair again |
| `mnemarr-unreachable` | Mnemarr does not answer | Wait: what waits is kept in the outbox and goes when it does |
| `mnemarr-rejected` | Mnemarr said no to a batch (a limit, an unknown format) | The batch is kept with its reason; update Geminarr or Mnemarr to versions that agree (Mnemarr 0.4.0 or later) |
| `mnemarr-insecure-address` | The address is plain http to a host that is not the loopback | Use https, or, in a sandbox only, `GEMINARR__MNEMARR__ALLOWINSECUREHTTP=true` |
| `outbox-growing` | Something has waited for Mnemarr for more than an hour | Look at the `mnemarr` check: it says why |
| `paused` | The operator asked for quiet | *Resume* on the page *Connection* |
| `browser-exposed` | A port of the network namespace that Geminarr shares with the browser listens off the loopback (the web desktop, its TLS port or the debugging port): something else than Geminarr could reach a browser that has no sign-in and holds a Google session. Geminarr closes `/browser/` (a 503) and says so on the *Overview* | Check that the init script `loopback-only.sh` is mounted in `/custom-cont-init.d/` of the browser container, executable, and that it ran (the container's log says `10-loopback-only.sh: exited 0`); a new version of the image may need the script adapted. `ss -ltn` in the container must show nothing off the loopback but Geminarr's 8282 |
| `signing-in` (the reason of a skipped run, not a state) | A tab of the browser is on a sign-in page: a person is signing in, so the pass attached to nothing and said nothing of the session | Finish the sign-in, or close the tab. A tab that stays on a sign-in page for `GEMINARR__BROWSER__SIGNINGINPASSES` passes in a row stops making the pass step aside: the session is then checked as usual (attached, measured), and the state says what is found |
| `operator-password-weak` | The password has fewer than twelve characters: it is refused, not accepted | Put a longer one in the file |

## Recover

- **`/browser/` shows nothing (a gateway error, or a blank page over plain HTTP), or Geminarr says `browser-failed`**: the browser container is not started or not reachable. The desktop needs HTTPS or `localhost` to draw its screen. Look at its log and at the `browser` check of the health (the page *Overview*); in Compose Geminarr must run with `network_mode: service:browser`, in Kubernetes in the same pod. Chromium needs about 1 GB of shared memory (`shm_size`) and memory above 1 GB.
- **A lost browser volume** (volume lost, a restore): the state turns `needs-login` (the session is gone); sign in again in the browser (the page *Connection*, *Open the browser*). *Disconnect* on the page *Connection* clears the cookies of the browser and forgets the connection (`not-connected`).
- **A restored Mnemarr** (fewer lines than Geminarr believes it sent): Geminarr goes back to Mnemarr's positions by itself and sends again what is missing.
- **A suspected leak of the session**: *Disconnect* in Geminarr is **local**: it clears the cookies of the browser and forgets the connection, and revokes nothing at the provider (a cookie copied earlier stays valid, and what the assistant's pages stored in the browser is not cleared). The step that counts is in the dedicated Google account's security page: sign out of all sessions and remove the device, change the password, and sign in again by hand. The browser's volume is the session: nobody else may read it, and its page and its debugging port must never be reachable without a login.
- **A stuck pass**: it stops at `PassTimeoutMinutes` by itself; "Run now" while one runs is refused.
- **Upgrade of the browser** (a new image version, pinned by digest): its profile is its own format; if the state turns `needs-login` after an upgrade, sign in again and write the version here.

## Spike hygiene (while a spike is going on)

`.spike/` and `.profile/` are git-ignored and **hold a signed-in session and real conversations**: they live on disk (not in `/tmp`, which is memory on the build machine), are deleted when the spike's result is written down, and a spike's findings written in `docs/spikes/` contain the *shape* of the pages and never their content.

## Signing in

- **Locked out** (HTTP 429 after five wrong passwords, for fifteen minutes): wait, or restart the container (the lockout is in memory). If it happens without you having tried, someone is guessing the name: change it (`GEMINARR__AUTH__ADMINUSER`).
- **Rotate the password**: replace the content of the file; the next sign-in uses it, and sessions that are open stay valid until their end. **If the password leaked, restart the container after changing it: that ends every session.**
- **Nobody can sign in and the page only says "wrong"**: the log of the container says why at its start (`Nobody can sign in (NotConfigured | PasswordUnreadable | PasswordWeak)`), and the reason codes above say what to do. Each refused sign-in and each lockout is in the log too, without the name or the password typed.
- **A page that answers 400 "The host name is not allowed"**: add the name or the address you reach it by to `GEMINARR__ALLOWEDHOSTNAMES` (or set `GEMINARR__PUBLICURL`).
- **Signed out all of a sudden**: the container restarted (sessions are in memory), or the 12 hours passed.
