# 0019 — The desktop has an origin of its own, opened by a ticket of the operator's session

- **Status**: decided by the maintainer in the conversation of 2026-11-17 (the review's finding R-24-71: "fix it"); it amends [ADR 0017](0017-the-browser-is-for-geminarr-alone-and-geminarr-relays-its-screen.md), which served the desktop on Geminarr's own origin
- **Date**: 2026-11-17
- **Spec**: D-27

## Context

ADR 0017 relays the web desktop of the browser container under `/browser/` of Geminarr's page. The desktop is another program's, with scripts that need `'unsafe-inline' 'unsafe-eval'`: served on Geminarr's origin, a flaw in it would run where the operator's session cookie is sent, and could act as the operator on Geminarr's API. The review raised it as major (R-24-71). The answer cannot be to accept it: the operator's session is the key to the Google session.

## Decision

1. **The desktop is served on an origin of its own: a second port of Geminarr** (`GEMINARR__BROWSER__DESKTOPPORT`, 8283 by default; `GEMINARR__BROWSER__DESKTOPPUBLICURL` when a proxy gives it an address). The port is open only when a desktop is configured. It answers for the desktop and for **nothing else**: not the API, not the interface; and the interface's origin no longer serves `/browser/`.
2. **The operator's session opens it by a ticket**, not by a second password. `POST /api/v1/connection/desktop` gives an address `<desktop origin>/enter?ticket=…`: the ticket is random (256 bits), **worth one opening and a minute**, bound to the session that asked. The desktop's origin exchanges it for a cookie **of that origin only** (`HttpOnly`, `SameSite=Strict`, `__Host-` when the cookie is `Secure`) and redirects to `/browser/`, so that the ticket does not stay in the address bar. The cookie stands for the operator's session behind it: signing out, or the end of its lifetime, ends the desktop's too, and an open WebSocket ends with it.
3. **Nothing of the desktop can act as the operator.** The operator's cookie is sent by a browser to every port of the same host, but the desktop's pages are another origin: they cannot read the answers of Geminarr's API, and a write needs the `X-Geminarr-Request` header, which another origin cannot send without a preflight that Geminarr never allows. The desktop's cookie is not the operator's: it is worth nothing on the API. The relay still forwards no cookie in either direction (ADR 0017).
4. **The page opens the window blank, on the click, then sends it to the address with its ticket** (a browser lets a window open only on a click, and the ticket is asked for after).

## Alternatives considered

- **A sandboxed document (CSP `sandbox` without `allow-same-origin`)**: an opaque origin does not send the cookie to its own sub-requests (credentials are for the same origin), so the desktop would be refused by its own relay; and it would break what the client keeps in the page's storage. Rejected.
- **A sub-domain with the proxy's single sign-on and a login of its own**: the same isolation, but one more address and one more password for the person, and a deployment-specific piece. Kept possible: `DESKTOPPUBLICURL` is how a proxy gives the desktop an address of its own.
- **The same port, the same origin (ADR 0017)**: accepted at first, refused after the review. Superseded by this record.

## Consequences

- A **second port** to publish and to let through: the Compose and Kubernetes examples, the Service, the ingress (two hosts or two ports), the network policy, and the TLS proxy of a sandbox. The desktop's origin must be a **secure context** like the page (HTTPS, or `localhost`).
- The origin of the desktop is judged on the WebSocket's handshake against its own address, not the page's.
- The relay's security headers (the loose policy that the desktop's pages need) apply on the desktop's origin only; the page's own origin keeps the strict policy on every path.
- The ticket is in a query string for the second it takes to follow the redirect: `Referrer-Policy: no-referrer` is on every answer, and a ticket used, expired, or of an ended session gives nothing.
- A restart ends the sessions, the tickets and the desktop cookies (in memory, like the sessions).
