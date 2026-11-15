# Network

Geminarr is **outbound only** towards the outside, and has **one inbound path**: its page, which must be behind a login.

## Inbound

Two pages, both behind a login: Geminarr's own, and the page of the browser container where a person signs in by hand (ADR 0016).

| Port | Container | What | Who may reach it |
|---|---|---|---|
| 8282/tcp | Geminarr | The page, the API, `/health/*` | The operator, **through the reverse proxy with TLS and the single sign-on**. Nothing else is published. Probes (`/health/live`, `/health/ready`) may be reached by the platform. |
| 3000/tcp | Browser | The web desktop of the browser: the screen of an ordinary Chromium, where a person signs in to the **dedicated** Google account | The operator, **through the reverse proxy with TLS and the single sign-on**, and the browser's own login on top. The browser holds the session: whoever opens this page uses the account, so it is never reachable anonymously, never from the internet without a login. |
| 9222/tcp (loopback) | Browser | The debugging port of Chromium: **the key to the session** | **Nobody**: it listens on the loopback of the network namespace that the two containers share (one pod; `network_mode: service:browser` in Compose). It is in no Service, no published port, no network policy; Geminarr alone reaches it, as `127.0.0.1`. |

**The names the page may be reached by are a list** (`GEMINARR__ALLOWEDHOSTNAMES`, and the host of `GEMINARR__PUBLICURL`): a request for any other `Host` is refused with a 400, so that a web page that points its own name at this address (DNS rebinding) cannot read what the container says. The default is the loopback names; **add the name or the address you reach it by** (for example the address of the machine for a sandbox, or the host of the public address behind a proxy that keeps the `Host` header). The probes are exempt, because a platform calls them by the address of the pod.

The live view is the page where the operator types the dedicated account's password into the container's browser: it must never be anonymous. Geminarr's own sign-in stands behind the proxy's (D-19): every call but the probes needs the operator's session.

## Outbound (the list is the policy)

| From | Destination | Purpose | When |
|---|---|---|---|
| The browser container | The assistant's origin (`gemini.google.com`), the account provider's sign-in origin (`accounts.google.com`) and what Chromium itself reaches | A person signs in; a pass looks at the assistant's page; later the conversations are read | When a person uses it, and at every pass |
| Geminarr | The configured Mnemarr (HTTPS) | Send turns, positions, heartbeat; pairing and enrolment when the key is asked for | Every pass (a pairing: while it waits, a call every few seconds) |

Geminarr itself reaches nothing of the assistant: it asks the browser container, through its debugging address, to open the assistant's address in a page of its own (the only host it navigates to, `GEMINARR__BROWSER__ASSISTANTURL`). What Chromium reaches is **the pod's network**, and a network policy applies to a pod, not to a container: **the egress that the browser needs also applies to Geminarr's own process** (the price of sharing a namespace so that the debugging port is never on a network, ADR 0016). The network policy of the pod is where the list is held (see the Kubernetes example: what a policy can say depends on the network plugin, and a policy knows addresses, not names). The exact list of hosts that the assistant's page needs is written down by spike S1 and **a new destination is a decision** (ADR 0008: the network policy is in the trust anchor; ADR 0016).

A proxy in front of either page: it must pass WebSocket upgrades (the web desktop of the browser uses them) and must not buffer them; and it must not cache `/api`. With SWAG, the maintainer's sub-domain API with its `default` template is enough (the `websocket` template sends the `Upgrade` headers twice, which the web desktop refuses); add the single sign-on as in the other applications.

## Firewalls and proxies at the other end

Geminarr reaches Mnemarr over HTTPS only (a loopback address or an explicit sandbox switch, `GEMINARR__MNEMARR__ALLOWINSECUREHTTP`, excepted); redirects are not followed, every call is bounded (30 seconds), and the key goes only to the address that was configured. Which of Mnemarr's paths must be open to it: `/api/v1/ingest/*` (conversations, positions, heartbeat, pairing, enrolment). Mnemarr's own page, where a person approves a pairing, is a link that Geminarr shows with its host visible: Geminarr never calls it.
