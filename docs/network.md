# Network

Geminarr is **outbound only** towards the outside, and has **one inbound path**: its page, which must be behind a login.

## Inbound

| Port | What | Who may reach it |
|---|---|---|
| 8282/tcp | The page, the API, the live view (a WebSocket on the same origin), `/health/*` | The operator, **through the reverse proxy with TLS and the single sign-on**. Nothing else is published. Probes (`/health/live`, `/health/ready`) may be reached by the platform. |

**The names the page may be reached by are a list** (`GEMINARR__ALLOWEDHOSTNAMES`, and the host of `GEMINARR__PUBLICURL`): a request for any other `Host` is refused with a 400, so that a web page that points its own name at this address (DNS rebinding) cannot read what the container says. The default is the loopback names; **add the name or the address you reach it by** (for example the address of the machine for a sandbox, or the host of the public address behind a proxy that keeps the `Host` header). The probes are exempt, because a platform calls them by the address of the pod.

The live view is the page where the operator types the dedicated account's password into the container's browser: it must never be anonymous. Geminarr's own sign-in stands behind the proxy's (D-19): every call but the probes needs the operator's session.

### From the browser (ADR 0015)

The extension in the browser container calls Geminarr on the same port (8282): `POST /api/v1/extension/push` with the token of its policy, and the browser fetches `GET /api/v1/extension/update.xml` and the package from it. These three need no session of the operator (the browser has none) and are the only anonymous paths besides the probes and the sign-in. **Nothing limits the browser's pod to these routes** (it does not go through the proxy's single sign-on): what protects every other route is Geminarr's own sign-in, which is why the browser is hardened (its policy, its sandbox and capabilities, no way in) and why its egress is a list. The name by which the browser reaches Geminarr (`http://geminarr:8282` between two containers) **must be in `GEMINARR__ALLOWEDHOSTNAMES`**, or it gets a 400. Allow the browser's address alone: nothing else needs to reach port 8282 but the operator's proxy and this browser. **The token travels in clear over plain HTTP**: that is acceptable on one host or on a network of its own shared by the two containers (the examples), not across nodes of a cluster that does not encrypt its pod traffic: there, give the browser an `https` address (`geminarrUrl`, and `GEMINARR__EXTENSION__PUBLICURL` for the update manifest) or use a mesh that encrypts.

## Outbound (the list is the policy)

| Destination | Purpose | When |
|---|---|---|
| The assistant's origin (`gemini.google.com`) and the static hosts it loads its page from | **From the browser container, not from Geminarr**: the extension reads the conversations | Every round of the extension |
| The account provider's sign-in origin (`accounts.google.com`) | **From the browser container**: a person signs in | Only when the session ends |
| The configured Mnemarr (HTTPS) | Send turns, positions, heartbeat; pairing and enrolment when the key is asked for | Every pass (a pairing: while it waits, a call every few seconds) |

**Geminarr itself reaches only Mnemarr.** The extension has a single host permission, the assistant's origin, and sends to a single address, the one in its policy; the network policy of the **browser's** container repeats the list at the cluster level (the assistant's origin and the hosts its page loads, the account provider's sign-in, Geminarr). The browser's own policy repeats it for the pages it may open (`URLBlocklist` `*` and `URLAllowlist`: the assistant's origin and its sign-in), the role that a route filter had when Geminarr drove the browser. **A new destination is a decision** (ADR 0008: the network policy is in the trust anchor).

A proxy in front of the container: it must pass WebSocket upgrades on `/api/v1/connection/live-views/` (from vertical 3) and must not buffer them; and it must not cache `/api`.

## Firewalls and proxies at the other end

Geminarr reaches Mnemarr over HTTPS only (a loopback address or an explicit sandbox switch, `GEMINARR__MNEMARR__ALLOWINSECUREHTTP`, excepted); redirects are not followed, every call is bounded (30 seconds), and the key goes only to the address that was configured. Which of Mnemarr's paths must be open to it: `/api/v1/ingest/*` (conversations, positions, heartbeat, pairing, enrolment). Mnemarr's own page, where a person approves a pairing, is a link that Geminarr shows with its host visible: Geminarr never calls it.
