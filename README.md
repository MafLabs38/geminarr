# Geminarr

> Bring your Gemini conversations to your memory. A satellite of [Mnemarr](https://github.com/MafLabs38/mnemarr), in the spirit of the *arr suite.

**Status: foundation and operator access.** The container runs, its page is closed behind a sign-in and shows its health and its settings, the whole quality chain is in place, and the specification is written. **It does not read Gemini yet**: how to read a product that has no API from a container is the first thing to be measured ([roadmap](docs/ROADMAP.md)).

Gemini keeps your conversations to itself: no API gives access to the history, and a good idea told by voice a year ago cannot be found again. Geminarr signs in to a **dedicated Google account** (the one that shares your Google AI plan), reads the conversations that appeared or grew since its last visit, and sends their new turns to Mnemarr, which stores them as notes and finds them **by meaning** and by text. It keeps no copy of its own, summarises nothing, and when its session ends it tells Mnemarr, which mails you a link to sign in again.

> ⚠️ **Never sign it in to your main Google account.** A Google session is valid across all of the account's services (mailbox included): Geminarr is made for an account that holds nothing but Gemini conversations ([why](docs/adr/0004-a-dedicated-google-account-never-the-main-one.md)). Reading a consumer product by automation is against its terms of use; the risk is yours, on that dedicated account.

## Documentation

**Understand it:**

| Document | Content |
|---|---|
| [docs/VISION.md](docs/VISION.md) | Why, what it must and must not do |
| [docs/SPEC.md](docs/SPEC.md) | Decisions, architecture, data model, the contract with Mnemarr, the reader, the session and live view, the Takeout import, features, API, settings, health, security, questions |
| [docs/adr/](docs/adr/README.md) | Architecture decision records |
| [docs/ROADMAP.md](docs/ROADMAP.md) | The spikes and the verticals, built and to come |
| [docs/mnemarr-changes.md](docs/mnemarr-changes.md) | What Mnemarr must gain for Geminarr (four small changes) |

**Run it:**

| Document | Content |
|---|---|
| [deploy/](deploy/README.md) | Generic deployment examples (Docker, Kubernetes) and the three levels of "as code" |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | The shape of the GitOps folder, the order of a first deployment |
| [docs/configuration.md](docs/configuration.md) | Every setting |
| [docs/network.md](docs/network.md) | Ports, directions, the list of outbound destinations |
| [docs/runbook.md](docs/runbook.md) | Operating it: what to watch, what to do when it is degraded |
| [docs/releasing.md](docs/releasing.md) | How versions are made and followed |
| [SECURITY.md](SECURITY.md) | Reporting a vulnerability, security model |

**Contribute:**

| Document | Content |
|---|---|
| [CONTRIBUTING.md](CONTRIBUTING.md) | Workflow, conventions, development setup |
| [docs/DEVELOPMENT-PROTOCOL.md](docs/DEVELOPMENT-PROTOCOL.md) | Build big, check it yourself, push once |
| [docs/TESTING.md](docs/TESTING.md) | Testing strategy and CI gates |
| [docs/review-decisions/](docs/review-decisions/README.md) | How the findings of the four review agents are answered (the register, the three levels) |
| [docs/DESIGN.md](docs/DESIGN.md) | Interface guidelines (Portarr's, plus what is specific here) |
| [CLAUDE.md](CLAUDE.md) | Rules for AI assistants working in this repository |

## What is in the repository

```text
src/            .NET solution: Core, Api.V1, Host (the container's entry point) and their tests
frontend/       React app (Vite) with the application shell, built into the image
tests/e2e/      Playwright tests and the stack they run against
tools/release/  semantic-release (its own package, run by the release job alone)
deploy/         deployment examples
docs/           specification, decisions, testing strategy
.claude/        the four review agents and the two skills of the process
```

## Run it

```bash
docker build -t geminarr:dev .
secrets="$(mktemp -d)"                               # a folder only you can enter
openssl rand -base64 24 > "$secrets/operator-password"  # the operator's password: a file, never a variable
chmod 644 "$secrets/operator-password"                   # the container's user must read it; the folder keeps everybody else out
docker run --rm -p 127.0.0.1:8282:8282 --tmpfs /config:uid=1654,gid=1654 \
  -v "$secrets/operator-password:/run/operator-password:ro" \
  -e GEMINARR__AUTH__ADMINUSER=operator-name -e GEMINARR__AUTH__ADMINPASSWORDFILE=/run/operator-password \
  -e GEMINARR__AUTH__COOKIESECURITY=SameAsRequest geminarr:dev   # SameAsRequest: this is plain HTTP; behind TLS leave the default
# http://localhost:8282 (published on this machine only)  ·  health: /health/live and /health/ready  ·  every other call needs the operator's sign-in (see below)
```

See [CONTRIBUTING.md](CONTRIBUTING.md#development-setup) for the development setup.

## License

Geminarr is free software under the **GNU General Public License, version 3 or (at your option) any later version** (GPL-3.0-or-later), like Mnemarr, Portarr and Orchestrarr. The text is in [LICENSE](LICENSE).
