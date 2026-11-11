# 0006 — Releases are decided by semantic-release from main, and consumed by pulling

- **Status**: accepted
- **Date**: 2026-11-11
- **Spec**: D-13

## Decision

The same design as Mnemarr's ADR 0013, whose rationale and consequences apply unchanged:

1. semantic-release runs on `main` only, as a job of the CI after every gate (tests, scans, end-to-end, Sonar) passed; its tooling is its own package (`tools/release`), installed by that job alone, without install scripts or cache, with no credential left in the checkout.
2. Rules: `feat` is a minor, `fix`/`perf`/`revert` a patch, a breaking change of any type is a minor below 1.0; `chore(deps)` is a patch; `docs`, `test`, `refactor`, `style`, `build`, `ci`, `chore` release nothing. The first release is 0.1.0 (a seed tag `v0.0.0` on `main`); 1.0 is the maintainer's decision.
3. `main` takes merge commits only; every other pull request is squash-merged and its **title** (a checked Conventional Commit) is the commit that decides the version.
4. One image `ghcr.io/<owner>/geminarr` for `linux/amd64` and `linux/arm64`, tagged `X.Y.Z`, `latest`, the branch and `sha-…`, built with `VERSION=X.Y.Z.<run>`. Pull requests publish nothing; a push whose gate failed publishes nothing; a run on `main` is never cancelled.
5. **The CI never reaches a host.** A version is consumed by pulling: a Compose file pins `X.Y.Z`, or a GitOps tool watches the registry (Flux image automation in the maintainer's cluster) and commits the new version in the infrastructure repository.

## Consequences

- **The browser binary is architecture-specific** (vertical 2): it is chosen with `TARGETARCH` in the Dockerfile, never by the builder's architecture; and the end-to-end test only runs the amd64 image, so a smoke test of the arm64 image becomes a precondition of that vertical, as it was for Mnemarr's vector library.
- The details and the table of what each push does are in [releasing.md](../releasing.md).
