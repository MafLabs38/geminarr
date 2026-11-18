# Deploying Geminarr as code

> What a deployment of Geminarr looks like, so that the maintainer's private infrastructure repository can describe it without anything being rediscovered. **This repository holds no real value**: the generic examples are in [`deploy/`](../deploy/README.md); the real folder lives in the private GitOps repository.

## The three levels

See [ADR 0007](adr/0007-geminarr-is-described-as-code-at-three-levels.md): the repository (Terraform, `github-template/repos/geminarr.yaml`), the image (this repository's pipeline), the deployment (`apps/geminarr/` of the GitOps repository, applied by Flux).

## Shape of the GitOps folder (`apps/geminarr/`)

Same as the other applications of the cluster:

| File | Content | Geminarr-specific |
|---|---|---|
| `namespace.yaml` | The namespace `geminarr` | — |
| `pvc.yaml` | `geminarr-data` (database, outbox, Mnemarr key; ~2 Gi) **and `geminarr-browser-profile`** (the browser's profile; ~1 Gi) | Two claims on **local** storage with a retaining class. **The profile claim belongs to the browser's pod, is never backed up in the clear and never shared** (SPEC §11.2). |
| `secret-extension.enc.yaml` | The token of the extension (32 characters or more, random), mounted as a file: `GEMINARR__EXTENSION__TOKENFILE`, **and the same token in the browser's policy** | SOPS-encrypted. Rotating it is two changes (this and the policy), one after the other |
| `browser.yaml`, `secret-browser-policy.enc.yaml` | The browser's pod, and the policy that installs the extension in it and gives it its address and its token | See below; the browser's egress policy is the list of [network.md](network.md) |
| `secret-mnemarr.enc.yaml` (optional) | The key that Mnemarr gave to this machine, or a one-time code, mounted as a file: `GEMINARR__MNEMARR__KEYFILE` or `GEMINARR__MNEMARR__ENROLMENTCODEFILE` | SOPS-encrypted. The key is ingest-only and bound to the machine. Without either, the operator pairs on the page *Mnemarr* (the key is then written in the data folder) |
| `identity/` (optional) | The picture of the menu (a png, webp or svg), mounted as a file: `GEMINARR__IDENTITY__LOGOFILE`, with `GEMINARR__IDENTITY__NAME` | The same files as Portarr's and Mnemarr's GitOps folders hold; the picture is personal and never goes in this repository |
| `secret-operator.enc.yaml` | The operator's password (SOPS-encrypted), mounted as a file: `GEMINARR__AUTH__ADMINPASSWORDFILE` | The operator's name is a plain setting; choose one that is not obvious. Rotating the secret needs no restart |
| `deployment.yaml` | One replica, `Recreate`, non-root (uid 1654), read-only root file system, no capabilities, no service-account token, `/tmp` as `emptyDir`, probes on `/health/*`, the image line marked for the image policy | Small (256 Mi): Geminarr starts no browser |
| `service.yaml` | `ClusterIP` on 8282 | — |
| `ingress.yaml` | The page **behind the single sign-on** (default for the cluster's application zone) | The live view must never be anonymous; WebSocket upgrades pass |
| `networkpolicy.yaml` | Egress of Geminarr: DNS and the Mnemarr address; ingress: the proxy and the browser's pod. Egress of the browser's pod: DNS, Geminarr, the assistant's origins and its sign-in | The lists are the ones in [network.md](network.md); a change is a decision |
| `image-automation.yaml` | `ImageRepository`, `ImagePolicy` (`>=0.1.0`), `ImageUpdateAutomation` limited to this folder | Same as the other applications |
| `kustomization.yaml` | Lists the files; a `configMapGenerator` only if a config file appears | — |

The settings are the environment variables of [configuration.md](configuration.md); secrets are mounted files (`…FILE`).

## The extension and the browser

The reader is an extension in a browser that a person signed in to ([ADR 0015](adr/0015-the-reader-is-an-extension-in-the-browser-a-person-signed-in-to.md)). To describe it as code:

1. **The signing key** (once, by the maintainer): an RSA 2048 key in PKCS#8 PEM (`openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048`), stored as the repository secret `EXTENSION_SIGNING_KEY` (in `github-template`, `repos/geminarr.yaml`) and in the maintainer's own vault. **Losing it, or changing it, makes a new extension**: the browsers of the deployments would have to be pointed at the new id. The release pipeline refuses to publish without it.
2. **The id of the extension**: read it from the first release's package (`extension.json` in the image, or `GET /api/v1/extension/update.xml`, the `appid`). It is the same for every release signed by the same key.
3. **The token**: random, 32 characters or more, one secret. In Geminarr it is the file of `GEMINARR__EXTENSION__TOKENFILE`; in the browser it is the `token` of the policy.
4. **The browser's policy** (`/etc/chromium/policies/managed/geminarr.json`), built from [`deploy/browser/policy.example.json`](../deploy/browser/policy.example.json): the id, the `update_url` (`<address of Geminarr as the browser reaches it>/api/v1/extension/update.xml`), `geminarrUrl`, the token. A Secret, since it holds the token.
5. **The names**: the name that the browser reaches Geminarr by is in `GEMINARR__ALLOWEDHOSTNAMES`; if it is not the name of the request that the update manifest should carry (a proxy that ends TLS), set `GEMINARR__EXTENSION__PUBLICURL`.
6. **Updating the extension**: a release of Geminarr carries the new package; the browser takes it within hours (its own update check is about every five hours, and the extension asks every hour). To force it at once, remove the entry of the policy, restart the browser, put it back.

## Order of a first deployment (vertical 7)

1. The release `0.1.0` exists and its image is public (or the pull secret is in the folder).
2. The folder is added to the GitOps repository; Flux applies it; the pod starts; `/health/ready` is green; the state is `not-connected`.
3. The operator opens the page (single sign-on, then Geminarr's login), pairs it with Mnemarr (or the key/code was provided), and signs in to the dedicated account **in the browser's container** (the live view of Geminarr's page comes with vertical 4).
4. Mnemarr shows the machine `gemini` online; the first pass sends the recent conversations; the backfill walks the rest slowly.
5. Portarr registers the application and checks its health endpoint.

## Test instances

A test or demo instance runs on the **build machine**, not on the production cluster, at an address the maintainer can open, **with a login** (a service that listens on the network protects itself). Its browser profile is that of a spike: it is deleted when the spike's result is written down.
