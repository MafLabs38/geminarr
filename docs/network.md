# Network

Geminarr is **outbound only** towards the outside, and has **one inbound path**: its page, which must be behind a login.

## Inbound

| Port | What | Who may reach it |
|---|---|---|
| 8282/tcp | The page, the API, the live view (a WebSocket on the same origin), `/health/*` | The operator, **through the reverse proxy with TLS and the single sign-on**. Nothing else is published. Probes (`/health/live`, `/health/ready`) may be reached by the platform. |

The live view is the page where the operator types the dedicated account's password into the container's browser: it must never be anonymous. Geminarr's own login stands behind the proxy's (vertical 1); until it exists, publish nothing but the loopback address.

## Outbound (the list is the policy)

| Destination | Purpose | When |
|---|---|---|
| The assistant's origin (`gemini.google.com`) and the static hosts it loads its page from | Read the conversations | Every pass |
| The account provider's sign-in origin (`accounts.google.com`) | Sign in | Only during a live view |
| The configured Mnemarr (HTTPS) | Send turns, positions, heartbeat, pairing | Every pass |

The browser has a **route filter** that refuses every other origin, and the container's network policy repeats the list at the cluster level. The exact host names the assistant's page needs are written down by the first spike and listed here, **and a new destination is a decision** (ADR 0008: the network policy is in the trust anchor).

A proxy in front of the container: it must pass WebSocket upgrades on `/api/v1/connection/live/` (from vertical 2) and must not buffer them; and it must not cache `/api`.

## Firewalls and proxies at the other end

Geminarr reaches Mnemarr over HTTPS only (a loopback address or an explicit sandbox switch excepted); redirects are not followed. Which of Mnemarr's paths must be open to it: `/api/v1/ingest/*` (conversations, positions, heartbeat, pairing, enrolment).
