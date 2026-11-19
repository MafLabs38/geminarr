# Deployment examples

Generic examples only. A real deployment (host names, addresses, secrets, storage classes) belongs in your own private infrastructure repository, never in this one.

| File | For |
|---|---|
| [docker-compose.example.yml](docker-compose.example.yml) | A Docker host |
| [kubernetes/geminarr.example.yaml](kubernetes/geminarr.example.yaml) | A Kubernetes cluster |
| [browser/](browser/) | What goes into the browser container: its policy (it installs the extension and keeps the browser to the assistant's pages) and the script that keeps its desktop on the loopback |

Both run the image as a non-root user with a read-only root filesystem and no Linux capabilities, and keep the data folder on a **local** volume.

## Geminarr is described as code, at three levels

| Level | Where | What it holds |
|---|---|---|
| The repository | the organisation's `github-template`, `repos/geminarr.yaml` (Terraform) | the repository, its branches and rules, labels, secrets of the CI, quality options |
| The image | this repository, `Dockerfile` and `.github/workflows/ci.yml` | built, tested and published by the pipeline as `ghcr.io/<owner>/geminarr:X.Y.Z` (semantic-release, [docs/releasing.md](../docs/releasing.md)) |
| The deployment | your private infrastructure repository (GitOps), `apps/geminarr/` | namespace, volume, deployment, service, ingress, sealed secrets and the automation that follows the versions; **never in this repository** |

A change to the deployment is a commit in the infrastructure repository; nobody runs `kubectl apply` by hand and the CI has no access to the cluster. The examples here are what that `apps/geminarr/` folder is made from; the shape it should have is in [docs/DEPLOYMENT.md](../docs/DEPLOYMENT.md).

## What to know before running it

- **Closed by default.** The container answers its probes without a sign-in; everything else needs the **operator** (a name and a password file, see [configuration](../docs/configuration.md)). Without them nobody can sign in. It reads nothing until the reader is set (`GEMINARR__READER__KIND=Web`, with the token of the extension). Publish it behind a reverse proxy with TLS and your single sign-on, and tell it the name you reach it by (`GEMINARR__PUBLICURL`, or `GEMINARR__ALLOWEDHOSTNAMES`).
- **The reader is an extension** in a browser that a person signs in to by hand ([ADR 0022](../docs/adr/0022-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md)). Geminarr holds **no session** and starts no browser: the browser is a container of its own, with a volume of its own for the signed-in session of a **dedicated** Google account (never shared, never backed up in the clear), and its network egress limited to what [docs/network.md](../docs/network.md) lists. Do not sign it in to your main Google account: a session is valid across all the services of an account ([SPEC §7.1 and §11](../docs/SPEC.md)). What the extension needs from a deployment: a token (a secret, in Geminarr and in the browser's policy) and the policy file ([DEPLOYMENT.md](../docs/DEPLOYMENT.md#the-extension-and-the-browser)).

- **The browser is a container of its own** ([ADR 0016](../docs/adr/0016-the-browser-is-a-container-of-its-own.md)): an ordinary Chromium with a web desktop that a person signs in to by hand, and that Geminarr only relays (no debugging port, ADR 0021). **Both examples declare the egress guard too** ([ADR 0020](../docs/adr/0020-geminarrs-own-egress-is-limited-by-its-user-in-the-shared-namespace.md)): two one-shot containers that limit Geminarr's own process to Mnemarr in the namespace that it shares with the browser, from the rules of `egress/` that you fill in. **Both examples declare the browser next to Geminarr** (a second container of the same pod, or `network_mode: service:browser`), with a volume of its own for its profile (the signed-in session of a **dedicated** Google account: never shared, never backed up in the clear), **no port, no address and no login of its own** (Geminarr relays its web desktop on **an origin of its own** (a second port, `8283`, which you publish like the page, on a host of its own or on a port; give its address as `GEMINARR__BROWSER__DESKTOPPUBLICURL`), opened by a single-use ticket of the operator's session ([ADR 0019](../docs/adr/0019-the-desktop-has-an-origin-of-its-own-opened-by-a-ticket.md)), with no second password, [ADR 0017](../docs/adr/0017-the-browser-is-for-geminarr-alone-and-geminarr-relays-its-screen.md)), and the desktop on the loopback of the namespace that the two share (never published; `browser/loopback-only.sh` makes the image's desktop listen there only). Its image is a third party's, pinned by digest: fill the `<digest>` yourself. Its network egress is limited to what [docs/network.md](../docs/network.md) lists. Do not sign it in to your main Google account: a session is valid across all the services of an account ([SPEC §7.1 and §11](../docs/SPEC.md)).
