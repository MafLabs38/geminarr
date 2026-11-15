# 0017 — The browser is for Geminarr alone, and Geminarr relays its screen behind its own sign-in

- **Status**: accepted by the maintainer in the conversation of 2026-11-15
- **Date**: 2026-11-15
- **Supersedes**: the parts of [0016](0016-the-browser-is-a-container-of-its-own.md) that gave the browser's web desktop an address of its own, a login of its own and a place behind the proxy's single sign-on (decisions 1 and 7 in part, the consequence "a sub-domain for the browser's page")
- **Spec**: D-25, F-CON-01, F-LGN; applies ADR 0004 (a dedicated account) and D-08

## Context

ADR 0016 made the browser a container of its own, that a person signs in to by hand and that Geminarr attaches to. It left the browser's web desktop as a page of its own: a sub-domain behind the single sign-on, and a login of the desktop on top. The first try of that design showed what the maintainer does not want:

- three identities in a row before Google's own (the proxy's single sign-on, the desktop's login, then the account), when the application is the one that should hold the right to reach the browser;
- a link that opened the browser on its default page (a search engine), not on the account's sign-in page;
- and, above all, a browser that is **reachable by something other than Geminarr**: a sub-domain and a login are an address and a door, and the maintainer wants none. The browser is a support that lets Geminarr reach the assistant without being taken for a bot; it serves that and nothing else.

## Decision

1. **The browser container is for Geminarr alone.** It publishes no port, has no Service, no ingress, no sub-domain, no sign-in of its own. It shares a network namespace with Geminarr (a pod; `network_mode: service:browser` in Compose); its debugging port (`9222`) and its web desktop (`3000`) listen there and are the loopback of that namespace for Geminarr. Both addresses must be loopback addresses (`GEMINARR__BROWSER__CDPURL`, `GEMINARR__BROWSER__DESKTOPURL`): the start is refused otherwise.
2. **Geminarr relays the web desktop under `/browser/`**, behind its own sign-in (the operator's session, the fallback policy that closes every endpoint). A person is signed in to Geminarr once; the page *Connection* links to `/browser/`, in a tab of its own; the only credentials typed are the account's own, on the provider's own page. HTTP and WebSocket both pass. Geminarr neither types nor clicks in the desktop: it relays what the person does.
3. **What the relay keeps apart.** The request that goes on carries neither Geminarr's session cookie nor credentials; what comes back sets no cookie on Geminarr's origin; a WebSocket opened by a page of another origin is refused (on top of `SameSite=Strict`); a request without a session is sent to the sign-in page, and reaches the desktop for nobody. The pages of the desktop have their own content security policy (it draws on a canvas, decodes video, starts workers from blobs and talks over a WebSocket to this origin), given to that path only; they are never framed. Audio is not available: its worklet starts from a `data:` script, which the policy does not allow.
4. **The browser opens on the assistant** (`CHROME_CLI` of the image: the assistant's address), which sends a person who is not signed in to the provider's sign-in page: "Sign in" lands there, with nothing in between.
5. **The desktop needs a secure context** (it uses WebCodecs): HTTPS, or `localhost`. The reverse proxy in front of Geminarr terminates TLS as for any page; a plain-HTTP address on a network does not show the desktop.
6. **Kubernetes**: the only port a network policy lets in is Geminarr's (`8282`); `3000` and `9222` are no container port of the pod and no policy opens them, so another pod of the cluster cannot reach a desktop that has no sign-in.
7. **A new dependency**: `Yarp.ReverseProxy` (Microsoft's reverse proxy library), used for the relay only. A hand-written WebSocket relay would be less code than its tests are long; the library is the one the platform's own teams use for this. Its approval is the maintainer's (rule 12).

## Alternatives considered

- **Keep the sub-domain and drop the desktop's login** (the single sign-on alone): rejected by the maintainer: an address and a door for something that is not meant to be reached by anything but Geminarr, and two identities instead of one.
- **A hand-written relay**: no new dependency, but a WebSocket relay that handles backpressure, close frames, upgrades and timeouts is the kind of code that is wrong in a corner; the library already has the tests of those corners.
- **Frame the desktop in Geminarr's page**: rejected: the desktop is another program's page with its own scripts; a frame would need the policy of Geminarr's whole interface to be loosened, and the relay's looser policy is better kept to one path.
- **Hide the browser behind a one-time link that Geminarr issues**: more machinery than the sign-in Geminarr already has, for the same effect.

## Consequences

- The deployment gets simpler: one Service, one address, one login to rotate, no sub-domain, no ingress rule and no secret for the browser. The examples of `deploy/`, `docs/network.md`, `docs/DEPLOYMENT.md`, `docs/configuration.md` and the runbook say so in the same pull request.
- **The security of the desktop is Geminarr's sign-in.** Whoever is signed in to Geminarr can use the browser, and with it the account: the operator's password is the key to the session. The relay is therefore the surface the review reads hardest (origin check, headers, no cookie in either direction, no way around the sign-in), and it is covered by integration tests (against a fake desktop and a real server for the WebSocket) and by the end-to-end journey (against a real Chromium container).
- **The egress of the pod** is unchanged by this decision and stays open for review (the price of the shared namespace, ADR 0016): the browser needs the provider's origins, and the pod's policy cannot tell the two containers apart. A proxy that lets through the provider's names only, between the browser and the network, would close it; it is a step of its own.
- A sandbox over plain HTTP on a network shows everything but the desktop; to see it, use HTTPS or `localhost`.
- Open: Q-17 (does the provider accept a program's read on a browser that a person signed in to?), Q-18 (which image, pinned by which digest).
