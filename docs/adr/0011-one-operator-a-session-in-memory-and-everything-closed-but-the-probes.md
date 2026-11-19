# 0011 — One operator, a session in memory, and everything closed but the probes

- **Status**: accepted
- **Date**: 2026-11-13
- **Spec**: D-19, F-ACC-01 to F-ACC-04; closes the two findings deferred by the first review (R-1-10, R-1-12)

## Context

The container will hold the signed-in session of a Google account and the live view where the maintainer types its password (ADR 0004). Whatever reaches its page reaches that. The page is behind the proxy's single sign-on, but a pod that can be reached directly, a page that rebinds its name to this address, or a forgotten endpoint must not be a way in. Mnemarr solved the same problem for many people and many keys (ADR 0016: accounts, roles, a database of sessions); Geminarr has **one** person.

## Decision

1. **One operator.** A name and a password in a mounted file (`GEMINARR__AUTH__ADMINUSER`, `…ADMINPASSWORDFILE`), at least twelve characters, **read at each sign-in** so that rotating the secret needs no restart. No account to create, no role, no database. Without them **nobody can sign in**: the container stays alive (the platform must not restart it for a missing secret) and closed, and the health says `operator-not-configured`.
2. **Closed by default.** A fallback authorization policy requires a session on every endpoint; the only anonymous things are the probes, the static shell of the interface and the sign-in call. The API description is behind the session too, and an unknown API path is a 401 (nothing is told to a caller who is not signed in).
3. **The session is a cookie and a record in memory.** `HttpOnly`, `SameSite=Strict`, `Secure` with the `__Host-` prefix unless a plain-HTTP sandbox says otherwise (`SameAsRequest`), no expiry date; valid only while the server holds its session, which has an **absolute lifetime** (12 hours by default). Signing out removes it on the server, so a copy of the cookie is dead at once. The keys that sign the cookies are in memory too: **a restart signs everybody out**, which is the rule for a container that holds a session of someone's account (nothing at rest to steal, nothing to rotate).
4. **A lockout that tells almost nothing.** Five wrong passwords lock a **name** for fifteen minutes, whether or not it is the operator's (so a lockout does not say which name is), counted before the password is checked (a burst of parallel attempts cannot all pass), in memory, bounded (the operator's name is never forgotten to make room, the made-up ones are: so that flooding made-up names, a thousand of them, can no longer reset the count of the operator's. The price is known and accepted: someone who floods and then counts can learn that a guessed name is the operator's, which gives them a name and no more, against a password of twelve characters at least). The comparison takes the same time whatever is wrong, a wrong name and a wrong password are told the same, and a body larger than a sign-in can be is a 413. A restart lifts a lockout early. (Whoever knows the name can lock it out for fifteen minutes: choose one that is not obvious; the same trade as Mnemarr's.)
5. **A write needs `X-Geminarr-Request`**, checked by the server before the sign-in (a cross-site form cannot send it), on every non-safe method under `/api` and `/openapi`, the sign-in included.
6. **A request for a `Host` that is not allowed is refused** (DNS rebinding): `GEMINARR__ALLOWEDHOSTNAMES` (default the loopback names) and the host of `GEMINARR__PUBLICURL`, a port ignored. **The probes are exempt**: a platform calls them by the address of the pod. The list has its own key: ASP.NET's `AllowedHosts` stays `*` (it would reject the probes).

## Alternatives considered

- **The proxy's single sign-on alone**: rejected, a pod reachable directly (or a mistake in the proxy) would skip it; defence in depth, since the live view is the most sensitive page of the family.
- **Mnemarr's accounts, roles and database**: rejected, built for people and keys; one operator needs none of it, and a database of sessions is something at rest to steal.
- **Cookie sessions that survive a restart** (a key ring on disk): rejected for the same reason; the cost is signing in again after a restart, which for one operator is nothing.
- **A password hash in the configuration**: the file is the secret and is read as it is (compared as hashes in constant time); a hash would only add a tool to run before deploying.

## Consequences

- The sign-in page is the first screen; **the whole application is behind a gate in the interface** (nothing asked of the API before the session is known; a 401 anywhere puts the sign-in back and forgets what was read).
- Every setting added later that holds a secret follows the same rule: a file, read when used, never shown (the *Settings* page says only *set* or *not set*).
- A `/health` resource for the operator lists every check with a stable reason code (the page maps codes to sentences); the probes say nothing more than up or not.
- The roadmap is renumbered: access (this), the link to Mnemarr, the session and the live view, the reader, the Takeout import, production. The ADRs written before that say "vertical N" with the old numbers; the roadmap is the reference.
