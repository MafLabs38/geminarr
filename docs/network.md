# Network

Geminarr is **outbound only** towards the outside, and has **one inbound path**: its page, which must be behind a login.

## Inbound

| Port | What | Who may reach it |
|---|---|---|
| 8282/tcp | The page, the API, the live view (a WebSocket on the same origin), `/health/*` | The operator, **through the reverse proxy with TLS and the single sign-on**. Nothing else is published. Probes (`/health/live`, `/health/ready`) may be reached by the platform. |

**The names the page may be reached by are a list** (`GEMINARR__ALLOWEDHOSTNAMES`, and the host of `GEMINARR__PUBLICURL`): a request for any other `Host` is refused with a 400, so that a web page that points its own name at this address (DNS rebinding) cannot read what the container says. The default is the loopback names; **add the name or the address you reach it by** (for example the address of the machine for a sandbox, or the host of the public address behind a proxy that keeps the `Host` header). The probes are exempt, because a platform calls them by the address of the pod.

The live view is the page where the operator types the dedicated account's password into the container's browser: it must never be anonymous. Geminarr's own sign-in stands behind the proxy's (D-19): every call but the probes needs the operator's session.

## Outbound (the list is the policy)

| Destination | Purpose | When |
|---|---|---|
| The assistant's origin (`gemini.google.com`) and the static hosts it loads its page from | Read the conversations | Every pass |
| The account provider's sign-in origin (`accounts.google.com`) | Sign in | Only during a live view |
| The configured Mnemarr (HTTPS) | Send turns, positions, heartbeat; pairing and enrolment when the key is asked for | Every pass (a pairing: while it waits, a call every few seconds) |

The browser has a **route filter** that refuses every other origin, and the container's network policy repeats the list at the cluster level. The exact host names the assistant's page needs are written down by the first spike and listed here, **and a new destination is a decision** (ADR 0008: the network policy is in the trust anchor).

A proxy in front of the container: it must pass WebSocket upgrades on `/api/v1/connection/live-views/` (from vertical 3) and must not buffer them; and it must not cache `/api`.

## Firewalls and proxies at the other end

Geminarr reaches Mnemarr over HTTPS only (a loopback address or an explicit sandbox switch, `GEMINARR__MNEMARR__ALLOWINSECUREHTTP`, excepted); redirects are not followed, every call is bounded (30 seconds), and the key goes only to the address that was configured. Which of Mnemarr's paths must be open to it: `/api/v1/ingest/*` (conversations, positions, heartbeat, pairing, enrolment). Mnemarr's own page, where a person approves a pairing, is a link that Geminarr shows with its host visible: Geminarr never calls it.
