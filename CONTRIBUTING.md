# Contributing to Geminarr

Thanks for your interest! Geminarr is at an early stage; the best way to help right now is to read the [specification](docs/SPEC.md) and open an issue with feedback.

## Ground rules

- **English** for code, comments, docs, commits, issues and pull requests.
- **No real data** in anything you submit: no domain names, IP addresses, host names, personal paths, tokens, and above all **no real conversation, account, cookie or identifier of an assistant's user**. Use `homelab.example`, `server-1`, `a-dedicated-account@example.com`. Recorded pages and archives used as fixtures are synthetic. CI rejects commits containing secrets.
- **Never sign Geminarr in to your main Google account**, in a test or anywhere ([why](docs/adr/0004-a-dedicated-google-account-never-the-main-one.md)).
- Be kind. See the [Contributor Covenant](https://www.contributor-covenant.org/version/2/1/code_of_conduct/).

## Workflow

1. Open (or pick) an issue.
2. Branch from `develop`: `feature/<issue>-<short-description>` (also `fix/`, `chore/`, `docs/`).
3. Commit with [Conventional Commits](https://www.conventionalcommits.org/): `feat(reader): read the list of conversations`.
4. Run every gate **locally**, then push once and open a pull request against `develop`; complete the PR template. Its title is a Conventional Commit too: it becomes the squash commit, and it decides the next version ([releasing](docs/releasing.md)). How a vertical is developed: [DEVELOPMENT-PROTOCOL.md](docs/DEVELOPMENT-PROTOCOL.md).
5. CI must be green: lint, unit, integration, E2E smoke, CodeQL, gitleaks, Trivy, and SonarQube once configured.

Direct pushes to `main` and `develop` are blocked for everyone. One vertical (a feature end to end) is one pull request, with its tests and documentation. A structural decision gets an ADR in `docs/adr/` and a `D-xx` row in the spec. A change that adds a setting, a volume, a port, an outbound destination or a secret updates the configuration, network and deployment documents and `deploy/` in the same pull request.

## What a good PR contains

- **Zero warnings.** The SonarQube rules run in the build and in ESLint, so `dotnet build` and `yarn lint` already say what the quality gate will say. Fix a smell rather than silence it; a justified exception is documented where it is made.
- Tests at the right level of the pyramid ([TESTING.md](docs/TESTING.md)); the reader against synthetic fixtures, never the network.
- Documentation updates (spec, ADR when a decision is made).
- A self-review with the four review agents of `.claude/agents/` (tests, quality, security, architecture): run the `review-vertical` skill before opening the PR, and answer every finding of the pipeline's agents in the PR's *Review* section and in `docs/review-decisions/<pr>.md` (three levels: fixed, declined with a reason, or — for a serious finding — accepted by the maintainer; [the register](docs/review-decisions/README.md)).

## Development setup

Needs the .NET 10 SDK, Node 22 with Yarn 1 and Docker. **On the maintainer's build machine no .NET SDK is installed and the machine is small and shared**: every .NET command runs in a capped container, one heavy command at a time, after `uptime` and `free -m`:

```bash
# .NET, in a container with a CPU and memory cap (the SDK image is the one the Dockerfile pins)
docker run --rm --cpus 2 --memory 3g -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  sh -c 'dotnet build src/Geminarr.sln && dotnet test --solution src/Geminarr.sln'
docker run --rm --cpus 2 --memory 3g -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet format src/Geminarr.sln --verify-no-changes
```

Coverage of the .NET code (the target is 85 % overall; the report is XML under `coverage/backend/`):

```bash
docker run --rm --user "$(id -u):$(id -g)" --cpus 2 --memory 3g -e HOME=/tmp/home -e DOTNET_CLI_HOME=/tmp/home -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test --solution src/Geminarr.sln --configuration Release -- --coverage --coverage-output-format xml --results-directory /src/coverage/backend/all
```

With the SDK installed locally:

```bash
yarn install --frozen-lockfile
dotnet build src/Geminarr.sln

# Backend, then frontend with hot reload (the frontend proxies /api to the backend)
# The data folder must exist and be writable (it defaults to /config, the image's volume)
mkdir -p .data
GEMINARR__DATAFOLDER="$PWD/.data" dotnet run --project src/Geminarr.Host   # http://localhost:8282
yarn start                                                                  # http://localhost:5173

# Checks, as CI runs them
dotnet format src/Geminarr.sln --verify-no-changes
yarn lint && yarn stylelint && yarn format:check && yarn typecheck
dotnet test --solution src/Geminarr.sln
yarn test --coverage
```

`.data/` is ignored by Git, and so are `.spike/` and `.profile/` (spikes: a signed-in session and real conversations live there, on disk and not in `/tmp`; delete them when the spike's result is written down).

End-to-end tests run against an instance of the image: `docker compose -f tests/e2e/stack/docker-compose.yml up -d --build`, then `yarn e2e --grep @smoke --workers=1` (stop the stack with `down -v`).
