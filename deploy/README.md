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

- **Foundation only so far.** The container answers (`/health/live`, `/health/ready`, `/api/v1/system/status`) and serves its interface; it does not read anything yet. Access control comes with the first vertical, before any session exists: until then, publish it on the loopback address only, as the Compose example does.
- **From the reader vertical on**, the container holds a browser profile (the signed-in session of a **dedicated** Google account). It gets a volume of its own, never shared and never backed up in the clear, and its network egress is limited to what [docs/network.md](../docs/network.md) lists. Do not sign it in to your main Google account: a session is valid across all the services of an account ([SPEC §7.1 and §11](../docs/SPEC.md)).
