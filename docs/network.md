# Network

Geminarr is **outbound only** towards the outside, and has **one inbound path**: its page, which must be behind a login.

## Inbound

One page, behind a login: Geminarr's own. The browser container has no page, no port and no sign-in of its own: Geminarr relays its web desktop under `/browser/`, behind its own sign-in (ADR 0017).

| Port | Container | What | Who may reach it |
|---|---|---|---|
| 8282/tcp | Geminarr | The page, the API, `/health/*`, and the relay of the browser's web desktop (`/browser/`, WebSocket included) | The operator, **through the reverse proxy with TLS** (and its single sign-on if you have one), then Geminarr's own login. Nothing else is published. Probes (`/health/live`, `/health/ready`) may be reached by the platform. **The desktop needs a secure context (HTTPS or `localhost`)**. |
| 3000/tcp (loopback) | Browser | The web desktop of the browser: the screen of an ordinary Chromium, where a person signs in to the **dedicated** Google account. No sign-in of its own | **Nobody but Geminarr**: it listens **on the loopback** of the network namespace that the two containers share (one pod; `network_mode: service:browser` in Compose), which an init script of the browser container makes true (`deploy/browser/loopback-only.sh`: the image listens on every interface otherwise). It is published nowhere and in no Service, and no other container or pod can open it, whether or not a network policy is enforced. |
| 9222/tcp (loopback) | Browser | The debugging port of Chromium: **the key to the session** | **Nobody but Geminarr**, the same way. It is in no Service, no published port, no network policy. |

**The names the page may be reached by are a list** (`GEMINARR__ALLOWEDHOSTNAMES`, and the host of `GEMINARR__PUBLICURL`): a request for any other `Host` is refused with a 400, so that a web page that points its own name at this address (DNS rebinding) cannot read what the container says. The default is the loopback names; **add the name or the address you reach it by** (for example the address of the machine for a sandbox, or the host of the public address behind a proxy that keeps the `Host` header). The probes are exempt, because a platform calls them by the address of the pod.

Whoever is signed in to Geminarr can use the browser, and with it the account: **the operator's password is the key to the session**, and the relay of `/browser/` is closed without it like every other endpoint (D-19, D-25): every call but the probes needs the operator's session.

## Outbound (the list is the policy)

| From | Destination | Purpose | When |
|---|---|---|---|
| The browser container | The assistant's origin (`gemini.google.com`), the account provider's sign-in origin (`accounts.google.com`) and what Chromium itself reaches | A person signs in; a pass looks at the assistant's page; later the conversations are read | When a person uses it, and at every pass |
| Geminarr | The configured Mnemarr (HTTPS) | Send turns, positions, heartbeat; pairing and enrolment when the key is asked for | Every pass (a pairing: while it waits, a call every few seconds) |

Geminarr itself reaches nothing of the assistant: it asks the browser container, through its debugging address, to open the assistant's address in a page of its own (the only host it navigates to, `GEMINARR__BROWSER__ASSISTANTURL`). What Chromium reaches is **the pod's network**, and a network policy applies to a pod, not to a container: **the egress that the browser needs also applies to Geminarr's own process** (the price of sharing a namespace so that the debugging port is never on a network, ADR 0016). The network policy of the pod is where the list is held (see the Kubernetes example: what a policy can say depends on the network plugin, and a policy knows addresses, not names). The exact list of hosts that the assistant's page needs is written down by spike S1 and **a new destination is a decision** (ADR 0008: the network policy is in the trust anchor; ADR 0016).

A proxy in front of Geminarr: it must pass WebSocket upgrades (the relayed desktop uses one, under `/browser/api/websockets`) and must not buffer them; and it must not cache `/api`. Check that the `Upgrade` and `Connection` headers are sent once: the desktop answers 204 to a request whose `Upgrade` header is not exactly `websocket`, which a proxy that adds them twice produces (a reverse proxy with its own WebSocket template may do it). The proxy keeps the `Host` header (or its name is in `GEMINARR__ALLOWEDHOSTNAMES`).

## Firewalls and proxies at the other end

Geminarr reaches Mnemarr over HTTPS only (a loopback address or an explicit sandbox switch, `GEMINARR__MNEMARR__ALLOWINSECUREHTTP`, excepted); redirects are not followed, every call is bounded (30 seconds), and the key goes only to the address that was configured. Which of Mnemarr's paths must be open to it: `/api/v1/ingest/*` (conversations, positions, heartbeat, pairing, enrolment). Mnemarr's own page, where a person approves a pairing, is a link that Geminarr shows with its host visible: Geminarr never calls it.
