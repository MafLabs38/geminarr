# 0002 — The family stack and structure

- **Status**: accepted
- **Date**: 2026-11-11
- **Spec**: D-01

## Context

Mnemarr, Portarr and Orchestrarr share one stack, one repository layout and one way of working, so that a contributor (or an agent) of one is at home in the others. Geminarr is the fourth of the family and a satellite of Mnemarr. The maintainer asked for exactly the same structures, processes and skills.

## Decision

- **Backend** .NET 10 (LTS), C# 14, ASP.NET Core, in the layers `Core` (no ASP.NET, no browser), `Api.V1`, `Host`, plus `Browser` from vertical 2 (the session and the live view; the web reader joins it in vertical 3). **Frontend** React, TypeScript (strict), Vite, TanStack Query, CSS Modules, the design rules of Portarr as Mnemarr applies them. **Tests** xUnit v3, NSubstitute, AwesomeAssertions, Vitest, Testing Library, Playwright. **Storage** SQLite behind interfaces, a schema in numbered steps (Mnemarr ADR 0018). **Delivery** one multi-architecture image on GHCR.
- **API first**: the page consumes the REST API under `/api/v1`; settings are `GEMINARR__*` environment variables and files for secrets; health with reason codes.
- **The browser is an adapter**: browser automation (Playwright for .NET) lives in its own project behind the ports of Core, so that the pipeline is tested with fakes and the product's pages are touched in one place.
- The repository layout, the review agents (`.claude/agents`), the skills (`develop-vertical`, `review-vertical`), the documents (`CLAUDE.md`, `CONTRIBUTING.md`, `docs/DEVELOPMENT-PROTOCOL.md`, `docs/TESTING.md`, `docs/DESIGN.md`) and the workflows are those of Mnemarr, adapted. Where Geminarr differs, the difference is stated in the file.

## Consequences

- Fixes to the shared chain are applied in both repositories (as for Portarr and Mnemarr).
- The Rust collector, the assistant runner, the vector search and the notes machinery of Mnemarr are **not** part of Geminarr: it is smaller on purpose.
