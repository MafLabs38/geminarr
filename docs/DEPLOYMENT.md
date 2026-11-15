# Deploying Geminarr as code

> What a deployment of Geminarr looks like, so that the maintainer's private infrastructure repository can describe it without anything being rediscovered. **This repository holds no real value**: the generic examples are in [`deploy/`](../deploy/README.md); the real folder lives in the private GitOps repository.

## The three levels

See [ADR 0007](adr/0007-geminarr-is-described-as-code-at-three-levels.md): the repository (Terraform, `github-template/repos/geminarr.yaml`), the image (this repository's pipeline), the deployment (`apps/geminarr/` of the GitOps repository, applied by Flux).

## Shape of the GitOps folder (`apps/geminarr/`)

Same as the other applications of the cluster:

| File | Content | Geminarr-specific |
|---|---|---|
| `namespace.yaml` | The namespace `geminarr` | — |
| `pvc.yaml` | `geminarr-data` (database, outbox, Mnemarr key; ~2 Gi) **and `geminarr-browser`** (the configuration of the browser container, **its profile included: the signed-in session**; ~1 Gi) | Two claims on **local** storage with a retaining class, on the same node as the pod. **The browser claim is never backed up in the clear**: it is the session of a Google account |
| `secret-mnemarr.enc.yaml` (optional) | The key that Mnemarr gave to this machine, or a one-time code, mounted as a file: `GEMINARR__MNEMARR__KEYFILE` or `GEMINARR__MNEMARR__ENROLMENTCODEFILE` | SOPS-encrypted. The key is ingest-only and bound to the machine. Without either, the operator pairs on the page *Mnemarr* (the key is then written in the data folder) |
| `identity/` (optional) | The picture of the menu (a png, webp or svg), mounted as a file: `GEMINARR__IDENTITY__LOGOFILE`, with `GEMINARR__IDENTITY__NAME` | The same files as Portarr's and Mnemarr's GitOps folders hold; the picture is personal and never goes in this repository |
| `secret-operator.enc.yaml` | The operator's password (SOPS-encrypted), mounted as a file: `GEMINARR__AUTH__ADMINPASSWORDFILE` | The operator's name is a plain setting; choose one that is not obvious. Rotating the secret needs no restart |
| `deployment.yaml` | **One pod, two containers** (ADR 0016): Geminarr (non-root uid 1654, read-only root, no capabilities) and **the browser container** (an ordinary Chromium with a web desktop; a third party's image **pinned by digest**, never `latest`: the choice of the image is the maintainer's, SPEC Q-18). One replica, `Recreate`, no service-account token, `emptyDir` for `/tmp` and for the browser's shared memory (`/dev/shm`, 1 Gi, in memory), probes on `/health/*`, the Geminarr image line marked for the image policy. The two containers share the pod's network, so the browser's debugging port (`127.0.0.1:9222`) is never on a network | The browser container starts as root (its init sets the user): test that Chromium starts on your platform before hardening it. Limits: Geminarr 256 Mi; the browser 2 Gi |
| `service.yaml` | One `ClusterIP`: Geminarr on 8282, Geminarr alone. The browser's desktop (3000) and its debugging port (9222) are in none of them | — |
| `ingress.yaml` | **One page**: Geminarr's, with the login of your reverse proxy in front if you have one (HTTPS: the browser's desktop, relayed under `/browser/`, needs a secure context) | Not anonymous; WebSocket upgrades pass (the relayed desktop uses one). The browser has no ingress, no sub-domain and no login of its own |
| `networkpolicy.yaml` | Ingress: the proxy only, on 8282 (nothing lets in 3000 or 9222). Egress: DNS and the Mnemarr address for Geminarr; https to the assistant and to the account provider's sign-in for the browser container (a policy knows addresses, not names: Cilium `toFQDNs`, or port 443 for this pod only) | The list is the one in [network.md](network.md); a change is a decision. **No policy may open the debugging port** |
| `image-automation.yaml` | `ImageRepository`, `ImagePolicy` (`>=0.1.0`), `ImageUpdateAutomation` limited to this folder | Same as the other applications |
| `kustomization.yaml` | Lists the files; a `configMapGenerator` only if a config file appears | — |

The settings are the environment variables of [configuration.md](configuration.md); secrets are mounted files (`…FILE`).

## Order of a first deployment (vertical 6)

1. The release `0.1.0` exists and its image is public (or the pull secret is in the folder).
2. The folder is added to the GitOps repository; Flux applies it; the pod starts; `/health/ready` is green; the browser container is up; the state is `not-connected` (or asks to sign in).
3. The operator opens Geminarr's page (the proxy's single sign-on if there is one, then its own login), pairs it with Mnemarr (or the key/code was provided), then opens the browser from the page *Connection* (`/browser/`, relayed by Geminarr: no second login) and **signs in by hand to the dedicated account**. The next pass finds the session: `connected`.
4. Mnemarr shows the machine `gemini` online; the first pass sends the recent conversations; the backfill walks the rest slowly.
5. Portarr registers the application and checks its health endpoint.

## Test instances

A test or demo instance runs on the **build machine**, not on the production cluster, at an address the maintainer can open, **with a login** (a service that listens on the network protects itself). Its browser container's volume is that of a spike: it is deleted when the spike's result is written down.
