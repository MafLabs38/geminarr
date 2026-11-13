# 0012 — The attention mail points to Mnemarr, not to the satellite

- **Status**: accepted
- **Date**: 2026-11-13
- **Spec**: D-20; supersedes the part of [ADR 0003](0003-geminarr-is-a-satellite-that-sends-turns-to-mnemarr-as-a-machine.md) §6 that says the mail holds a link to Geminarr's sign-in page; follows Mnemarr's ADR 0041 and 0042

## Context

ADR 0003 decided that Mnemarr sends the mail that asks the maintainer to sign in again, so that Geminarr holds no mail credential, and wrote that the mail would carry a link to Geminarr's sign-in page. Mnemarr built it (0.4.0) and, in doing so, decided differently and for a reason that is right: an **ingest-only key is low-trust**. A leaked key could make Mnemarr send, from the maintainer's own mailbox, a convincing "sign in here" to an address of the attacker's choosing.

## Decision

1. On an `attention` signal Mnemarr shows its **own sentence** for the closed code (`needs-login`, `reader-broken`) and the satellite's link **on its Machines page**, where the person is signed in and the host of the link is visible.
2. The mail (one per episode, a reminder at most once a day for a few days, stored so that a restart does not repeat it) is sent **from Mnemarr's mailbox to Mnemarr's configured recipient**, by SMTP, and holds Mnemarr's sentence and **the address of Mnemarr's Machines page** — **never the link, and never any text, that Geminarr wrote.**
3. Geminarr's part is unchanged: it states `needs-login` (or `reader-broken`) in its heartbeat with the address of its own sign-in page (`GEMINARR__PUBLICURL`), and holds no mail credential. Its `error` sentence is for the page, not for the mail.

## Consequences

- One more click for the maintainer (the mail, then Mnemarr, then Geminarr) for a mail that cannot be forged by a key.
- Geminarr's runbook says so: the mail leads to Mnemarr's Machines page, where the link to Geminarr's page is.
- The address of Geminarr's page must be valid for Mnemarr to keep it (https, or http to a loopback address; a host; no credentials; at most 512 characters): `GEMINARR__PUBLICURL` is checked at start for what can be known there (an http or https address with a host, no credentials, at most 512 characters); the rule on the scheme (https, or http to a loopback address) is applied where the link is sent (the link to Mnemarr, vertical 2), which refuses to send an address that Mnemarr would drop and says so in the health, instead of stopping a sandbox that is reached over plain http on a LAN.
