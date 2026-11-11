# Geminarr

> A satellite of Mnemarr: brings the conversation history of a web assistant (Gemini) into Mnemarr's memory, in the spirit of the *arr suite.

**Project stage: foundation.** The architecture and the security model are decided: the decisions are in [docs/SPEC.md](docs/SPEC.md) and [docs/adr/](docs/adr/README.md) (see [docs/VISION.md](docs/VISION.md) for the original need). The foundation vertical (pipeline, quality chain and a walking skeleton) is built; **how Gemini can be read from a container is not known yet** and is the object of spike S1 ([docs/ROADMAP.md](docs/ROADMAP.md)): do not design the reader beyond SPEC §6 before it has been answered.

This file is read automatically by Claude Code. It applies to every task in this repository. It is the same as [Mnemarr's](https://github.com/MafLabs38/mnemarr/blob/develop/CLAUDE.md), adapted: where it differs, it says so.

## Read before any task

| Document | When |
|---|---|
| [docs/VISION.md](docs/VISION.md) | Always: why this exists, what it must do and not do |
| [docs/SPEC.md](docs/SPEC.md) | Always: decisions `D-xx`, features `F-xx`, the contract with Mnemarr, the reader, the security model, questions `Q-xx` |
| [docs/DEVELOPMENT-PROTOCOL.md](docs/DEVELOPMENT-PROTOCOL.md) | Before building a vertical: slice, build big locally, push once, review once |
| [docs/ROADMAP.md](docs/ROADMAP.md) | To know which vertical is next and what is *not now* |
| [docs/mnemarr-changes.md](docs/mnemarr-changes.md) | Before touching anything that Mnemarr must also change |

## Tech stack

Same as Mnemarr and Portarr, so the products share conventions ([ADR 0002](docs/adr/0002-the-family-stack-and-structure.md)):

- **Backend**: .NET 10 (LTS), C# 14, ASP.NET Core, FluentValidation. Layers: `Geminarr.Core` (no ASP.NET, no browser), `Geminarr.Api.V1`, `Geminarr.Host`, and `Geminarr.Browser` (Playwright adapter) from vertical 2.
- **Frontend**: React, TypeScript (strict), Vite, TanStack Query, CSS Modules
- **Tests**: xUnit v3 + NSubstitute + AwesomeAssertions, Vitest + Testing Library, Playwright
- **CI/CD**: GitHub Actions, single multi-arch container image on GHCR, semantic-release
- Storage: SQLite behind interfaces, a schema in numbered steps.

## Non-negotiable rules

1. **English only** for code, comments, documentation, commits, issues and PRs. The chat with the maintainer is in French; nothing French is committed except clearly marked quotes in `docs/VISION.md`.
2. **Public repo, generic product.** Never commit real infrastructure values (domain names, IPs, host names, personal project names, file paths of a personal setup, tokens), nor **any real conversation, account, cookie, identifier or address of the assistant's user**, in code, tests, fixtures, docs, screenshots or commit messages. Use examples (`homelab.example`, `server-1`, `a-dedicated-account@example.com`, `gemini-c1a2b3c4d5e6`). Recorded pages and archives are synthetic.
3. **The session is the crown jewel.** The browser profile is the signed-in session of a Google account, which is valid across all of that account's services. It is only ever the **dedicated** account's ([ADR 0004](docs/adr/0004-a-dedicated-google-account-never-the-main-one.md)); never the maintainer's main account, except by the ladder of SPEC §11.4 and only with the maintainer present. The profile is never logged, never returned by the API, never in a backup in the clear, never in this repository. Nothing a conversation said is ever logged.
4. **Read-only, on the assistant's own pages.** Geminarr never submits a prompt, never writes to the assistant, and the browser loads no other origin (SPEC §6, §7.3, [network.md](docs/network.md)). A new destination is a decision.
5. ***arr patterns first.** A single container, API first (the UI consumes the REST API), settings as environment variables and files, Sonarr-like screens. The UI follows Portarr's design rules; read [docs/DESIGN.md](docs/DESIGN.md) before writing any UI code.
6. **The constellation**: Mnemarr remembers; Geminarr only brings conversations to it. It keeps **no second copy** of a conversation (D-06), consolidates nothing (D-05), sends no mail (D-11), and talks to Mnemarr only through its public API. Integrate through APIs, never by duplicating Mnemarr's logic.
7. **Privacy by default.** Nothing leaves the container but turns to the configured Mnemarr and requests to the assistant; secrets are masked before the outbox; any new outbound call is opt-in and documented.
8. **Sandbox first, PR after acceptance.** Develop each vertical in a local sandbox **on the build machine, with a login**, at an address the maintainer can open (never a tunnel, never the production cluster). Give the maintainer the URL, the test accounts and what to try, iterate live, and open the PR only once the vertical is accepted. Technical pull requests are not verticals and skip this (rule 12).
9. **One vertical = one PR**, with its tests and documentation. Commit locally as often as useful; **push once** when the local gates are green ([docs/DEVELOPMENT-PROTOCOL.md](docs/DEVELOPMENT-PROTOCOL.md)).
10. **Ask, but build.** When gathering needs, ask open questions (never multiple-choice forms). Once the need is clear, code the vertical and ask only when it truly requires an answer.
11. **Never** commit secrets. Use `dotnet user-secrets` locally and GitHub Secrets in CI. Decisions get an ADR in `docs/adr/NNNN-title.md` and a `D-xx` row in the spec.
12. **Work autonomously; interrupt the maintainer only for a real advance** ([ADR 0008](docs/adr/0008-the-agent-merges-technical-pull-requests.md), which applies [Mnemarr's ADR 0014](https://github.com/MafLabs38/mnemarr/blob/develop/docs/adr/0014-the-agent-merges-technical-pull-requests.md) here). The agent prepares, has the review agents read, and merges technical pull requests; a vertical reaches the maintainer clean, with its demo, and they merge it. What only the maintainer merges is defined **by role** (the rules and decisions, what decides whether a gate passes, what runs with a privilege or is built into the image, any new dependency, the release pull request; here also the browser binary, the network policy and how the profile is stored); any doubt goes to the maintainer. The agent never bypasses a guard: no admin or ruleset bypass, no merge with a required check failing or pending, no push to a protected branch.
13. **The real target decides.** A vertical that touches the real assistant is validated **by the agent itself, on the real target (the dedicated account, a real conversation), before the maintainer is asked to try it**. A test against a fake is not enough; a failure of the real run is a finding to fix, not a demo to give. The spike's material (`.spike/`, `.profile/`) is git-ignored, lives on disk (not in `/tmp`, which is memory on the build machine) and is deleted when its result is written down.

## Quality gates

A vertical is not done, and its pull request is not opened, until all of this holds.

- **Tests with the vertical**, at the levels of [docs/TESTING.md](docs/TESTING.md): unit, integration and, for a user journey, end to end. A bug found by a review gets a failing test first. **The reader is tested against synthetic fixtures, never the network.**
- **The SonarQube rules run locally, so a smell fails before the push.** The C# build carries the Sonar analyzer with the 324 rules of the server's profile (`.editorconfig`), warnings are errors; ESLint carries SonarJS, unicorn and jsx-a11y. **Zero warnings**, in every project but the tests.
- SonarQube: 0 bugs, 0 vulnerabilities, 0 critical code smells, coverage at least 70 % overall and at least 80 % on new code (the server job runs once `SONAR_HOST_URL` and `SONAR_TOKEN` exist).
- The four review agents (`review-vertical`), triaged with the three-level protocol (`review-triage` skill): every finding checked in the code before it is accepted or declined; the serious ones that are not fixed are the maintainer's to decide. An architecture verdict of "no" blocks the pull request. **Read the reports of the pipeline's agents on the head commit before presenting a PR** ([docs/DEVELOPMENT-PROTOCOL.md](docs/DEVELOPMENT-PROTOCOL.md)), and check `gh pr checks`, not only the agents.
- A documented exception to a Sonar rule names the rule, the files and the reason, in `.editorconfig` or in the code; it is never silent.

SonarQube rules to watch: S2583/S2589 (always true or false conditions), S3168 (async void), S3776 (cognitive complexity at most 15), S1481 (unused variables), S1854 (dead stores), S1066 (mergeable ifs), S2486 (swallowed exceptions), S2325 (a method that could be static).

## Look at your own work before anyone else does

Defects that a person finds by hand are defects the agent could have found first.

- **Run every gate the CI runs, before pushing**: `dotnet format src/Geminarr.sln --verify-no-changes`, the .NET tests, `yarn lint`, `yarn stylelint`, `yarn format:check`, `yarn typecheck`, `yarn test`.
- **Run the end-to-end tests yourself**, in Chromium and Firefox, against the stack built from the branch: `docker compose -f tests/e2e/stack/docker-compose.yml up -d --build`, then `yarn e2e --workers=1` (stop the stack afterwards with `down -v`), and once the way the CI runs it, in parallel (`yarn e2e --grep @smoke`).
- **Open the sandbox in a real browser** and use what was built as a person would: click through the journey, read the page, watch the console for errors and Content-Security-Policy violations, look at the screenshot (then delete it: images are re-read at every turn and saturate the context).
- **Try it on real material, not on a toy**, in the sandbox only (rule 2).
- A defect found this way is fixed with a test that fails without the fix.

## The build machine is small and shared

The machine that builds and tests has 4 cores and 15 GB, shared with other sessions, and `/tmp` is memory. Learned the hard way (two window freezes on 2026-11-03): **at most two agents at a time**, never during a build or a test, and a reviewing agent reads code, it does not start builds or browsers; **one heavy command at a time**, always capped (`docker run --cpus 2 --memory 3g`, `nice -n 15`, Playwright `--workers=1`, one browser); check `uptime` and `free -m` first (load above 3.5 or under 3 GB available: wait); no .NET SDK is installed on the host, so .NET commands run in a capped container ([CONTRIBUTING.md](CONTRIBUTING.md)); run the targeted tests, let the CI run the whole suite; nothing large in `/tmp`; remove your own containers afterwards.

## The constellation

| Product | Job | Relation to Geminarr |
|---|---|---|
| **Mnemarr** | Remember | Receives the turns through its ingest API; shows Geminarr as a machine; sends the "sign in again" mail |
| **Portarr** | See and describe the fleet | Registers Geminarr as an application; checks `/health/ready` |
| **Orchestrarr** | Act | Not involved |
| **Geminarr** (this repo) | Bring a web assistant's history to Mnemarr | — |

## Git workflow

Branches `main` (stable releases) and `develop` (integration) are protected by GitHub Rulesets: **no direct push, ever**. Everything goes through pull requests.

```text
1. git checkout develop && git checkout -b feature/<issue>-<short-description>
2. Commit with Conventional Commits (locally, as often as useful)
3. Run every gate locally; then git push origin feature/<issue>-<short-description>   (once)
4. gh pr create --base develop
5. CI green + the pipeline's review read and triaged -> squash merge into develop, by the agent for a technical pull request and by the maintainer for a vertical (the PR title is a Conventional Commit: it decides the version)
6. When develop is stable -> release PR develop -> main, merged with a **merge commit, never a squash** (semantic-release must see the commits; `main` refuses the other methods). The pipeline then tags and publishes the image; deployments pull it: [docs/releasing.md](docs/releasing.md)
```

Prefixes: `feature/`, `fix/`, `chore/`, `docs/`. Conventional Commits: `feat(reader): ...`, `fix(outbox): ...`, `docs(spec): ...`, `chore(ci): ...`.

The repository, its branches, its rules and its secrets are described as code in the organisation's `github-template` (`repos/geminarr.yaml`): a change to them is a pull request there, not a setting changed by hand. The AI review that runs in CI on a pull request is advisory; the SonarQube quality gate, CodeQL and the tests are what block.

## Infrastructure as code

Geminarr must be describable and deployable as code, like Mnemarr ([ADR 0007](docs/adr/0007-geminarr-is-described-as-code-at-three-levels.md), [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)): the container is configured by environment variables and files only; `deploy/` holds generic examples and **no real value**; the real deployment is a folder of the maintainer's private GitOps repository, applied by Flux, with secrets encrypted. A change that adds a setting, a volume, a port, an egress destination or a secret updates `docs/configuration.md`, `docs/network.md`, `docs/DEPLOYMENT.md` and `deploy/` **in the same pull request**.
