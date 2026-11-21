# 0016 — The browser is a container of its own, and a person signs in to it by hand

- **Status**: accepted by the maintainer in the conversation of 2026-11-15; the choice of the image is open (Q-18). **Its decisions on the browser's web desktop (an address of its own, a login of its own, a sub-domain behind the single sign-on) are replaced by [0017](0017-the-browser-is-for-geminarr-alone-and-geminarr-relays-its-screen.md)**: the browser is for Geminarr alone and Geminarr relays its screen
- **Amended by**: [0021](0021-geminarr-has-no-debugging-port-to-the-browser.md): Geminarr has no debugging port to the browser any more, so whatever this record says of `CDPURL`, the debugging address, attaching to the browser or driving it no longer holds; the rest stands.
- **Date**: 2026-11-15
- **Supersedes**: [0015](0015-the-browser-the-live-view-and-the-profile.md) (the embedded browser, the live view, the sign-in detection, the sandbox of the embedded browser)
- **Spec**: D-24, F-CON-01 to F-CON-04, F-LGN; applies ADR 0004 (a dedicated account) and D-08 (read-only, on the assistant's own pages)

## Context

The first design embedded a Chromium that Geminarr launched and drove with Playwright, and showed its screen on a page (a live view) where the operator signed in. It was built and reviewed (pull request 23).

The first real sign-in showed that **the account provider refuses a browser that is launched and driven by a program**, whoever types: the page answered "Couldn't sign you in: this browser or app may not be secure" right after the address was typed. Measured on 2026-11-15 with a throw-away account, with the headless shell of Chromium (as the container ran it) **and** with the complete Google Chrome in a virtual display: both were refused, both announced `navigator.webdriver = true`. Hiding that mark is a way around a protection of the provider, and this project does not do that.

The same day the maintainer, as a one-off measurement with them present (the ladder of SPEC §11.4), signed in by hand to a Google account, in an **ordinary Chromium running in a container** with a web desktop, reached through a sub-domain behind the single sign-on, from his PC. It worked.

## Decision

1. **The browser is its own container**, an ordinary Chromium with a web desktop (the page shows its screen and takes the keyboard and the mouse), with a **volume of its own for its profile**: the signed-in session of the dedicated account (ADR 0004, never the main account). Nobody launches it with a program: it is a browser that a person uses.
2. **A person signs in by hand**, in that page, once, and again when the provider asks for it. Geminarr never types a credential, in any page.
3. **Geminarr only attaches to that browser to read** (the debugging port of the browser, `GEMINARR__BROWSER__CDPURL`): it checks that the session is good, and later reads the conversations (spike S1 measures whether the provider accepts it). It never launches a browser, never navigates the sign-in pages, never changes the browser's settings.
4. **The state of the connection** is what the attachment finds: `not-connected` (no browser, or no session), `connected`, `degraded`, `needs-login` (the session is gone: a person signs in again in the browser's page), `paused`. The `connecting` state and the live view of the first design disappear: the browser's own page is where a person signs in.
5. **The debugging port is the key to the session**: whoever reaches it controls the browser. Chromium listens on the **loopback of the browser container only**, and Geminarr reaches it by **sharing that network namespace** (two containers of one pod in Kubernetes; `network_mode: service:browser` in Compose): the port is never on a network at all. The page of the browser is protected twice (the login of the reverse proxy, and the login of the browser's own page), never reachable without a login from the internet.
6. **The Geminarr image carries no browser** and no browser binary: it goes back to a small image. The browser's image is a third party's, pinned by digest; **choosing it is the maintainer's** (Q-18).
7. **The browser container is part of the product and goes into the infrastructure as code and into every file that describes a deployment, in the same pull request**: the examples of `deploy/` (Compose and Kubernetes), `docs/DEPLOYMENT.md`, `docs/network.md`, `docs/configuration.md`, `docs/runbook.md`, the spec, the GitOps folder of the maintainer, and Portarr. A container that is not declared there is a container that a deployment forgets.

This replaces decisions 1, 3, 4, 7 and 8 of the first design (the embedded browser, the live view, the sign-in detection, the image with Chromium, the sandbox of the embedded browser). What stays: the origin policy as the list of what the browser may reach is now the browser's own network (`docs/network.md`), the state machine's idea of "going back to what it was" after a pause, the single operator.

## Alternatives considered

- **Hide the automation marks** (the switch of Chromium that Playwright adds, the property `webdriver`): rejected. It is a way around the provider's protection, whatever the account.
- **A live view of a browser that Geminarr launches**: the first design. Refused by the provider at sign-in.
- **Copy the cookies of the maintainer's own browser to Geminarr**: rejected for now (ADR 0004: a cookie that is pasted is a secret that passes through too many places, and the provider may invalidate a session that it sees from another machine). It stays possible, as a tool of the maintainer's, if this decision fails.
- **Read from the maintainer's PC** (a collector, like Mnemarr's): possible later; it needs the PC to be on, and the maintainer wants to sign in from a phone too.
- **An import of the past alone**: there is none (withdrawn on 2026-11-20).

## Consequences

- Two containers instead of one, a second volume (the session), a sub-domain for the browser's page (behind the single sign-on), and a debugging port that must not leave the network of the pair.
- The first real read of Gemini is spike S1, on a throw-away account first, then on the dedicated one. If the provider refuses a read that a program drives on a browser that a person signed in to, the project has no other way forward (what the extension reads is what it reads).
- Pull request 23 (the embedded browser and its live view) is **withdrawn** (ADR 0015 stays as the record of that design, marked superseded) and replaced by this vertical: what it taught (the state machine that goes back to what it was, the profile folder rules, the reviews' lessons) is carried over where it still applies.
- **The price of the shared namespace**: a network policy applies to a pod, not to a container, so the egress that the browser needs (https to the assistant and to the account provider, whose addresses are not stable) also applies to Geminarr's own process, which would otherwise reach only Mnemarr. The alternative, two pods with the debugging port on a network, puts the key to the session on a network, which is worse. The maintainer refused this cost: **it is ended by [ADR 0020](0020-geminarrs-own-egress-is-limited-by-its-user-in-the-shared-namespace.md)** (Geminarr's own process is limited by its user in the shared namespace, whatever the policy says).
- Open: Q-17 (does the provider accept a program's read on a browser that a person signed in to?), Q-18 (which image, pinned by which digest).
