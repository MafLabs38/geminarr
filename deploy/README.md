# Deployment examples

Generic examples only. A real deployment (host names, addresses, secrets, storage classes) belongs in your own private infrastructure repository, never in this one.

| File | For |
|---|---|
| [docker-compose.example.yml](docker-compose.example.yml) | A Docker host |
| [kubernetes/geminarr.example.yaml](kubernetes/geminarr.example.yaml) | A Kubernetes cluster |

Both run the image as a non-root user with a read-only root filesystem and no Linux capabilities, and keep the data folder on a **local** volume.

## Geminarr is described as code, at three levels

| Level | Where | What it holds |
|---|---|---|
| The repository | the organisation's `github-template`, `repos/geminarr.yaml` (Terraform) | the repository, its branches and rules, labels, secrets of the CI, quality options |
| The image | this repository, `Dockerfile` and `.github/workflows/ci.yml` | built, tested and published by the pipeline as `ghcr.io/<owner>/geminarr:X.Y.Z` (semantic-release, [docs/releasing.md](../docs/releasing.md)) |
| The deployment | your private infrastructure repository (GitOps), `apps/geminarr/` | namespace, volume, deployment, service, ingress, sealed secrets and the automation that follows the versions; **never in this repository** |

A change to the deployment is a commit in the infrastructure repository; nobody runs `kubectl apply` by hand and the CI has no access to the cluster. The examples here are what that `apps/geminarr/` folder is made from; the shape it should have is in [docs/DEPLOYMENT.md](../docs/DEPLOYMENT.md).

## What to know before running it

- **Closed by default.** The container answers its probes without a sign-in; everything else needs the **operator** (a name and a password file, see [configuration](../docs/configuration.md)). Without them nobody can sign in. It still reads nothing; the reader and the session come with the next verticals, behind this access. Publish it behind a reverse proxy with TLS and your single sign-on, and tell it the name you reach it by (`GEMINARR__PUBLICURL`, or `GEMINARR__ALLOWEDHOSTNAMES`).
- **The browser is a container of its own** ([ADR 0016](../docs/adr/0016-the-browser-is-a-container-of-its-own.md)): an ordinary Chromium with a web desktop that a person signs in to by hand, and that Geminarr attaches to. **Both examples declare it next to Geminarr** (a second container of the same pod, or `network_mode: service:browser`), with a volume of its own for its profile (the signed-in session of a **dedicated** Google account: never shared, never backed up in the clear), **no port, no address and no login of its own** (Geminarr relays its web desktop under `/browser/` behind its own sign-in, [ADR 0017](../docs/adr/0017-the-browser-is-for-geminarr-alone-and-geminarr-relays-its-screen.md)), and the debugging address and the desktop on the loopback of the namespace that the two share (never published). Its image is a third party's, pinned by digest: fill the `<digest>` yourself. Its network egress is limited to what [docs/network.md](../docs/network.md) lists. Do not sign it in to your main Google account: a session is valid across all the services of an account ([SPEC §7.1 and §11](../docs/SPEC.md)).
