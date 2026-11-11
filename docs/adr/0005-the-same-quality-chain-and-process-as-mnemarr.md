# 0005 — The same quality chain and process as Mnemarr

- **Status**: accepted
- **Date**: 2026-11-11
- **Spec**: D-12

## Context

Geminarr holds a signed-in session and reads private conversations: it deserves at least the bar of the rest of the family. The maintainer wants the same pipeline, thresholds, review agents and way of working.

## Decision

- **The pipeline of Mnemarr, reduced to what Geminarr has**: lint (`dotnet format`, ESLint with SonarJS, unicorn and jsx-a11y, Stylelint, Prettier, `tsc`), unit and integration tests with coverage, the end-to-end smoke test in Chromium and Firefox against the image (non-root checked), SonarQube quality gate, CodeQL, gitleaks and Trivy, the release tooling tests, `release` and `image`. The SonarQube rules run in the build and in ESLint (zero warnings). Thresholds: 70 % overall, 80 % on new code (the server job), 70 % frontend.
- **Four review agents** (tests, quality, security, architecture) on every pull request, advisory, and the `review-vertical` skill locally; reports are triaged with the three-level protocol (fixed / declined minor / declined serious = the maintainer decides).
- **The development protocol** of Mnemarr: one vertical = one demo sentence; build big locally; every gate locally before the first push; demo; push once; one batched review round, two at most; local commits are free, pushes are few.
- **Adaptations for a small shared build machine**: at most two agents at a time, none of them starting builds or browsers; one heavy command at a time, always capped (`docker run --cpus 2 --memory 3g`, `nice`); `uptime` and `free -m` first.
- **A vertical that touches the real assistant is validated by the agent itself on the real target** before the maintainer is asked to look (spike first, then the vertical).
- The repository, its protections and its secrets are described as code in `github-template` (`repos/geminarr.yaml`); checks become required there once they pass.

## Consequences

- A slower first vertical (this foundation) for a repository that others, and agents, can work in safely.
- The SonarQube project, the model login of the review agents and the secrets of the CI are set up by the maintainer through the template's encrypted secrets.
