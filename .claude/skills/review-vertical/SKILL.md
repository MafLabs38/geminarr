---
name: review-vertical
description: Run the four review agents (tests, quality, OWASP security, architecture) in parallel on the current branch before opening or updating a vertical PR, then triage and apply their findings.
---

# Review a vertical before its PR

1. Base: `develop` (or the given base). Make sure the branch is committed and the full test suite is green.
2. Launch the four agents **two at a time at most** (the build machine is small and shared: four agents compiling together freeze it), each with the base and the branch name, asking for its Markdown report. A reviewing agent reads the code; it does not start builds, tests or browsers:
   - `test-reviewer`: relevance, redundancy, missing cases, speed;
   - `quality-reviewer`: spec compliance, design, smells, frontend and Rust conventions;
   - `security-reviewer`: OWASP and privacy review;
   - `architecture-reviewer`: technical lead, *arr patterns, layers, hard-coded vs configurable, decisions.
   If the agents are not registered in the session, spawn general-purpose agents with the content of `.claude/agents/<name>.md` as instructions.
3. **Triage** the four reports (and the CodeQL and SonarQube findings) with the protocol of `.claude/skills/review-triage/` (ADR 0010): fix what is right; decline medium and below with the reason in one line; a blocker, critical, high or major finding that you would not fix is the maintainer's to accept, so tell them and wait for their comment. Do not accept a finding without checking it in the code. An **architecture verdict of "no" blocks the PR** until it is addressed or decided by the maintainer (written in the PR's Review section).
4. Re-run the tests, note the durations before and after.
5. Add a **Review** section to the PR description (and the entries of `docs/review-decisions/<pr>.md`): per agent, what was fixed, what was declined and why, test count and duration before and after.
