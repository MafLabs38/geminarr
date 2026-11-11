# Geminarr — Testing strategy

> Same stack, conventions and gates as Mnemarr (ADR 0005), reduced to what Geminarr has today. The pyramid is the same; levels are added with the features that need them.

## 1. Pyramid

```text
                 ▲   E2E (Playwright)                       ~10 %   few, user journeys, on the real image
              ▲▲▲▲   Integration (API in memory)           ~20 %   the real host, a real data folder, a fake Mnemarr
         ▲▲▲▲▲▲▲▲▲   Unit (backend, frontend)               ~70 %   fast, isolated, all business logic
```

## 2. Tools

| Level | Backend (.NET) | Frontend |
|---|---|---|
| Unit | xUnit v3, NSubstitute, AwesomeAssertions, `FakeTimeProvider` | Vitest, Testing Library |
| Integration | `WebApplicationFactory<Program>` over the real host (`Category=Integration`) | — |
| E2E | — | Playwright (Chromium and Firefox) with `@axe-core/playwright`, against the image |
| Mutation | Stryker.NET on `Geminarr.Core` | StrykerJS |

## 3. What is specific to Geminarr

- **The reader is tested against fixtures, never the network.** The web reader and the Takeout importer are tested against *recorded or synthetic* pages and archives kept in `tests/fixtures/` (the shape of the product's pages, with invented conversations). A test that reaches the real product is a manual spike (`.spike/`, git-ignored), not part of the suite.
- **No real content in the repository.** No real conversation, account, cookie, identifier or address in a test, a fixture, a snapshot or a screenshot. Examples only (`a-dedicated-account@example.com`, `gemini-c1a2b3c4d5e6`).
- **The pipeline is tested with fakes of its ports** (`IConversationReader`, `IConversationSink`) and `FakeTimeProvider`: a conversation that grows, a Mnemarr that is down, restored from a backup (fewer lines than believed), that refuses the key; a kill between the browser closing and the flush (the outbox survives); a turn rewritten after it was sent.
- **The state machine is a table test**: every state, every event, the expected state and signal (SPEC §7.2).
- **The sink is tested against a fake Mnemarr** that implements the contract of SPEC §5 (positions, overlap skipped, no hole filled, heartbeat): the same fake serves the integration tests and, with `tests/e2e/stack`, the end-to-end ones.
- **The session never leaks**: tests grep the logs, the API responses and the health output of a run for cookie names and for the text of a turn.
- **The live view** is tested at its edges: a missing, reused or expired token, a wrong `Origin`, a second viewer, an unauthenticated caller.
- **A Takeout archive is hostile input**: a path with `..`, an absolute path, a symlink, a bomb, a huge entry and a malformed file each have a test.

## 4. Conventions

- Backend test classes are named `ClassUnderTestFixture`, methods `should_<result>_when_<condition>`. Integration tests carry `[Trait("Category", "Integration")]` (set once per assembly).
- No real `Thread.Sleep` or delay: use `FakeTimeProvider`. No test depends on the order of another or on the network.
- A test protects a behavior someone relies on; it does not assert a framework or a getter. The `test-reviewer` agent challenges this on every pull request.
- E2E selectors use roles and visible text, not styling.

## 5. CI gates

| Rule | Threshold |
|---|---|
| Core (.NET) coverage | at least 70 % overall, **at least 80 % on new code**, enforced by the SonarQube quality gate. That job only runs once a SonarQube server is configured, so **no check is made required in the repository rules before it is** |
| Frontend coverage | at least 70 %, enforced by Vitest (`coverage.thresholds`) |
| Unit tests | 100 % green, under 3 minutes |
| Integration | 100 % green |
| E2E smoke | 100 % green on every pull request, with no accessibility violation; the image runs as a non-root user |
| Static analysis | The SonarQube rules run in the build (C# analyzer, warnings are errors) and in ESLint (SonarJS, unicorn, jsx-a11y): zero warnings |
| Security | CodeQL (security queries, no new high or critical alert), gitleaks, Trivy |
| Release tooling | The rules that decide a version, the version of an image and the guard on the seed tag are tested (`tools/release/test`) |
| Mutation | Reported weekly, not enforced |

## 6. Running the tests

See [CONTRIBUTING.md](../CONTRIBUTING.md#development-setup) for the commands, including the container-based way for a small machine (the build machine has no .NET SDK installed and is shared: every heavy command goes through a capped container).
