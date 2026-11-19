# 0023 — What changes is pushed to the screens as it changes

- **Status**: accepted
- **Date**: 2026-11-19
- **Spec**: D-31 (applies [Orchestrarr's ADR 0025](https://github.com/MafLabs38/orchestrarr/blob/develop/docs/adr/0025-everything-that-changes-is-pushed-to-the-screens-as-it-changes.md) here)

## Context

A person who signs in to the assistant, or disconnects, waits for the page to say so. Until now the page asked the API every few seconds and the extension said that it was there every half minute: a sign-in took up to two minutes to show, which is a black box for a product whose whole job is a session that a person makes by hand. The *arr family shows what happens as it happens.

## Decision

1. **The host opens a WebSocket** (SignalR, which falls back to server-sent events or long polling where a proxy refuses it) at `/api/v1/live`, for whoever is signed in (the fallback policy closes it like any other route; the negotiation is a write and carries the `X-Geminarr-Request` header). It announces **that something changed, never the data**: a message is a topic, `connection`, `status`, `runs` or `mnemarr`. The page then reads what changed through the API, with its session, so the channel adds no way to read anything and carries nothing that is not already a public name.
2. **Announced once the change is made.** Every write of the connection's record, a sign-in that begins, a sign-out that is asked, confirmed or given up, a word of the extension about who is signed in, and the end of a pass or of a heartbeat. A burst is coalesced: each topic is sent once per 150 ms, and the last one is never dropped.
3. **The extension tells Geminarr when a page of the assistant that a person has open has loaded** (`page`, in the same route as `hello`): a sign-in ends on such a page, so Geminarr may ask for a round at once (three seconds apart at the shortest, only while a person is signing in), instead of at the next half-minute heartbeat. The pages that the extension opened itself do not count; two loads closer than five seconds are told once.
4. **An order reaches the extension at once.** The extension keeps one request open to Geminarr (`listen`, in the same route), which holds it for 25 seconds at most and answers it as soon as the operator asks something (*Disconnect*, *Sign in*, *Run now*, or gives a sign-out up). A *Disconnect* is therefore done in the second, before a person looks at the desktop. A sign-out that was sent and is not confirmed is not sent again to this request for two minutes (a request that came back at once would spin); at most eight requests wait; a worker that was stopped starts the loop again with an alarm.
5. **Polling stays as the fallback**: a page whose channel cannot open reads as before; while it is open, a read that was asked again every few seconds is asked again every minute, in case something was not announced. After a reconnection everything is read again.
6. **A rule for every screen to come**: what changes by itself is wired to a topic. Polling alone is a defect.

## Alternatives considered

- **Server-sent events**: no dependency on the client, but the family's other products use SignalR, and the maintainer asked for it.
- **A faster poll**: more requests for the same delay of the extension's half minute; it hides the problem.

## Consequences

- **A new dependency**: `@microsoft/signalr` (the interface; the server side is in the framework). It is the one that Orchestrarr uses, pinned.
- A proxy in front of Geminarr passes WebSocket upgrades (it already must for the desktop) or accepts the fallback.
- One request is held open by each extension: Geminarr has no other long request, and the cap bounds it.
- The visit of a page of the assistant by a person triggers one round at most every three seconds while they sign in (ten minutes at most): it is the only faster pace, and it follows the person, not a clock.
