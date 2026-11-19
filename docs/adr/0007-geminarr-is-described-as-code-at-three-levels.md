# 0007 — Geminarr is described as code, at three levels

- **Status**: accepted
- **Date**: 2026-11-11
- **Spec**: D-14, F-OPS-01, F-OPS-03

## Context

The maintainer deploys the family on a Kubernetes cluster by GitOps and describes everything as code. Geminarr must be deployable the same way, and nothing about a real deployment may enter a public repository.

## Decision

| Level | Where | Tool |
|---|---|---|
| The repository (branches, rules, labels, secrets of the CI, quality options) | the organisation's `github-template`, `repos/geminarr.yaml` | Terraform, applied by its pipeline |
| The image | this repository: `Dockerfile`, `ci.yml`, `tools/release` | GitHub Actions, semantic-release, GHCR |
| The deployment (namespace, volumes, deployment, service, ingress, sealed secrets, image automation) | the maintainer's private GitOps repository, `apps/geminarr/` | Flux, with the secrets encrypted (SOPS) |

- The container is configurable by **environment variables and files only** (no flag, no interactive step), so that the whole deployment is a manifest. Secrets (admin password, Mnemarr key or enrolment code) are mounted files.
- This repository ships **generic examples** (`deploy/`) and the shape of the GitOps folder ([DEPLOYMENT.md](../DEPLOYMENT.md)); a real address, namespace, storage class or secret is never here.
- The deployment needs, beyond Mnemarr's: a **second volume** for the browser profile (own claim, local storage on the node, never in a clear backup), an **egress policy** (SPEC §11.2), a larger memory limit for the browser, and the page published **behind the single sign-on** (the live view must never be anonymous).
- Nothing is deployed to the production cluster for testing: a test instance runs on the build machine, with a login (a rule of the maintainer).

## Consequences

- Moving the container to another cluster is a copy of one folder and one key.
- The first production deployment is vertical 5, after the reader works on the real assistant.
