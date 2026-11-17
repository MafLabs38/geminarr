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
| `deployment.yaml` | **One pod, two containers and the egress guard's two init containers** (ADR 0016, 0020): Geminarr (non-root uid 1654, read-only root, no capabilities) and **the browser container** (an ordinary Chromium with a web desktop; a third party's image **pinned by digest**, never `latest`: the choice of the image is the maintainer's, SPEC Q-18). One replica, `Recreate`, no service-account token, `emptyDir` for `/tmp` and for the browser's shared memory (`/dev/shm`, 1 Gi, in memory), probes on `/health/*`, the Geminarr image line marked for the image policy. The two containers share the pod's network, so the browser's debugging port (`127.0.0.1:9222`) is never on a network | The browser container starts as root (its init sets the user): test that Chromium starts on your platform before hardening it. Limits: Geminarr 256 Mi; the browser 2 Gi |
| `configmap-egress.yaml` (in the example: `geminarr-egress`) | The rules of the egress guard, `rules.v4` and `rules.v6` (the files of `deploy/egress/`), loaded by two init containers of the pod | **Geminarr's own process reaches Mnemarr and nothing else** (ADR 0020): it is named by its user (1654) in the namespace that the browser shares, whatever the network policy allows the browser. Write Mnemarr's address, port and the name servers; a change of Mnemarr's address is a change here and a restart. The init containers need `NET_ADMIN` and nothing else: the namespace must allow it (Pod Security `privileged`, or an exemption for them) |
| `configmap-browser-init.yaml` | The init script of the browser container, `loopback-only.sh` (the file of `deploy/browser/`), mounted in `/custom-cont-init.d/` with mode `0755` | It makes the browser's web desktop listen on the loopback of the pod only (the image listens on every interface) and refuse any request that names another origin than Geminarr's: without it, another pod, or a page that the browser itself loads, could open a desktop that has no sign-in. Test it again for any other image or version (`ss -ltn` in the container: nothing off the loopback but Geminarr's own ports, 8282 and 8283; Geminarr checks it too and closes the desktop's origin otherwise) |
| `service.yaml` | One `ClusterIP`: Geminarr on 8282 and the desktop's own origin on 8283 (ADR 0019), Geminarr alone. The browser's desktop (3000) and its debugging port (9222) are in none of them | — |
| `ingress.yaml` | **Geminarr's page, and the desktop's own origin** (a second host, or a second port, of the same proxy; its address is `GEMINARR__BROWSER__DESKTOPPUBLICURL`), with the login of your reverse proxy in front of the page if you have one (HTTPS: the browser's desktop needs a secure context) | Not anonymous; WebSocket upgrades pass (the relayed desktop uses one). The desktop's origin is closed without the cookie that a ticket of the operator's session gives, so it needs no login of the proxy (a second login would only get in the way of the window that the page opens). The browser container has no ingress and no login of its own |
| `networkpolicy.yaml` | Ingress: the proxy only, on 8282 and 8283 (nothing lets in 3000 or 9222). Egress: DNS and the Mnemarr address for Geminarr; https to the assistant and to the account provider's sign-in for the browser container (a policy knows addresses, not names: Cilium `toFQDNs`, or port 443 for this pod only) | The list is the one in [network.md](network.md); a change is a decision. **No policy may open the debugging port** |
| `image-automation.yaml` | `ImageRepository`, `ImagePolicy` (`>=0.1.0`), `ImageUpdateAutomation` limited to this folder | Same as the other applications |
| `kustomization.yaml` | Lists the files; a `configMapGenerator` only if a config file appears | — |

The settings are the environment variables of [configuration.md](configuration.md); secrets are mounted files (`…FILE`).

## Order of a first deployment (vertical 6)

1. The release `0.1.0` exists and its image is public (or the pull secret is in the folder).
2. The folder is added to the GitOps repository; Flux applies it; the pod starts; `/health/ready` is green; the browser container is up; the state is `not-connected` (or asks to sign in).
3. The operator opens Geminarr's page (the proxy's single sign-on if there is one, then its own login), pairs it with Mnemarr (or the key/code was provided), then presses **Sign in** on the page *Connection*, which opens the browser's desktop in a window (its own origin, opened by a ticket of the session: no second login), and **signs in by hand to the dedicated account**. The next pass finds the session: `connected`.
4. Mnemarr shows the machine `gemini` online; the first pass sends the recent conversations; the backfill walks the rest slowly.
5. Portarr registers the application and checks its health endpoint.

## Test instances

A test or demo instance runs on the **build machine**, not on the production cluster, at an address the maintainer can open, **with a login** (a service that listens on the network protects itself). Its browser container's volume is that of a spike: it is deleted when the spike's result is written down.
