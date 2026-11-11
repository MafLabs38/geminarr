# 0009 — Default port 8282

- **Status**: accepted
- **Date**: 2026-11-11
- **Spec**: D-17

## Context

Same reasoning as Mnemarr's ADR 0011: the audience runs the *arr suite and installs the family side by side. The ports in use include Sonarr 8989, Radarr 7878, Lidarr 8686, Readarr 8787, Prowlarr 9696, Whisparr 6969, Bazarr 6767, Mnemarr 8383 and **Portarr 8484** — the first draft of Geminarr listened on 8484 and collided with Portarr on the build machine at the first run.

## Decision

Geminarr listens on **8282** by default, set in one place, the `Urls` setting of `appsettings.json` (overridable with `GEMINARR__URLS` / `ASPNETCORE_URLS`); the Dockerfile, the deployment examples, the end-to-end stack and the development proxy follow it.

## Consequences

- No collision with the *arr applications or the rest of the family on a default install.
- The value appears in a few files; the checks (end-to-end tests, deployment examples) fail if one is missed.
