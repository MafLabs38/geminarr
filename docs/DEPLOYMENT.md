# Deploying Geminarr as code

> What a deployment of Geminarr looks like, so that the maintainer's private infrastructure repository can describe it without anything being rediscovered. **This repository holds no real value**: the generic examples are in [`deploy/`](../deploy/README.md); the real folder lives in the private GitOps repository.

## The three levels

See [ADR 0007](adr/0007-geminarr-is-described-as-code-at-three-levels.md): the repository (Terraform, `github-template/repos/geminarr.yaml`), the image (this repository's pipeline), the deployment (`apps/geminarr/` of the GitOps repository, applied by Flux).

## Shape of the GitOps folder (`apps/geminarr/`)

Same as the other applications of the cluster:

| File | Content | Geminarr-specific |
|---|---|---|
| `namespace.yaml` | The namespace `geminarr` | — |
| `pvc.yaml` | `geminarr-data` (database, outbox, Mnemarr key; ~2 Gi) **and `geminarr-profile`** (the browser profile; ~1 Gi) | Two claims on **local** storage with a retaining class, on the same node as the pod. **The profile claim is never backed up in the clear and never shared** (SPEC §11.2). |
| `secret-admin.enc.yaml` | The first administrator's password (SOPS-encrypted) | — |
| `secret-mnemarr.enc.yaml` | Either the Mnemarr **key** (provided) or a one-time **enrolment code** | The key is ingest-only and machine-bound |
| `deployment.yaml` | One replica, `Recreate`, non-root (uid 1654), read-only root file system, no capabilities, no service-account token, `/tmp` and the browser's scratch space as `emptyDir`, probes on `/health/*`, the image line marked for the image policy | Memory limit 1.5 Gi (browser); the browser sandbox needs are decided by spike S1 (Q-10) |
| `service.yaml` | `ClusterIP` on 8282 | — |
| `ingress.yaml` | The page **behind the single sign-on** (default for the cluster's application zone) | The live view must never be anonymous; WebSocket upgrades pass |
| `networkpolicy.yaml` | Egress: DNS, the assistant's origin and static hosts, the account provider's sign-in origin, the Mnemarr address; ingress: the proxy only | The list is the one in [network.md](network.md); a change is a decision |
| `image-automation.yaml` | `ImageRepository`, `ImagePolicy` (`>=0.1.0`), `ImageUpdateAutomation` limited to this folder | Same as the other applications |
| `kustomization.yaml` | Lists the files; a `configMapGenerator` only if a config file appears | — |

The settings are the environment variables of [configuration.md](configuration.md); secrets are mounted files (`…FILE`).

## Order of a first deployment (vertical 5)

1. The release `0.1.0` exists and its image is public (or the pull secret is in the folder).
2. The folder is added to the GitOps repository; Flux applies it; the pod starts; `/health/ready` is green; the state is `not-connected`.
3. The operator opens the page (single sign-on, then Geminarr's login), pairs it with Mnemarr (or the key/code was provided), and signs in to the dedicated account through the live view.
4. Mnemarr shows the machine `gemini` online; the first pass sends the recent conversations; the backfill walks the rest slowly.
5. Portarr registers the application and checks its health endpoint.

## Test instances

A test or demo instance runs on the **build machine**, not on the production cluster, at an address the maintainer can open, **with a login** (a service that listens on the network protects itself). Its browser profile is that of a spike: it is deleted when the spike's result is written down.
