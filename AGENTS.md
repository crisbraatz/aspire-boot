# AGENTS.md

This project is managed by **AI Workflow**. These instructions apply to every AI agent (Claude Code, Codex, Gemini, Copilot, others).

## Start here
1. Read `.aiw/state.json`. If `currentFeature` is not null, a feature is in progress: resume it instead of starting new work (Claude Code: `/aiw:resume`; Codex: `/prompts:aiw-resume`).
2. Read `docs/architecture.md` and `docs/business/overview.md` before planning anything.
3. Load the skills relevant to the task:
   - **Always:** `.aiw/skills/level2/architecture-ddd-vertical-slices/SKILL.md` (where every file goes) and `.aiw/skills/level3/domain/SKILL.md` (business vocabulary and rules).
   - Backend: `aspnetcore-api-identity`, `efcore-postgresql`, `wolverine`, `opentelemetry`, `testing-dotnet`, `aspire`, `infrastructure`.
   - Frontend: `angular`, `tailwind-accessibility`.

## Project
- Summary: AspireBoot is an open-source (MIT) .NET Aspire template that lets technical and non-technical builders start secure web products focused on business. It ships ready-made infrastructure (database, messaging, cache, object storage, email, HTTPS, observability) plus Identity-based accounts, profiles, sessions, user management and RBAC, with the UI in pt-BR, en-US and es-ES. It targets single-VPS deployment by default (managed cloud optional) and exposes an API for the bundled web frontend and a future mobile app. A core premise is strong AI guidance, so code stays maintainable, organized, performant, secure and aligned with the architecture.
- Stack: .NET 10 LTS (SDK 10.0.404), Aspire 17.0, ASP.NET Core Identity, EF Core 10 + Npgsql 10, PostgreSQL 19, Wolverine 6.65 (outbox/inbox) + RabbitMQ 4.4, Redis 8.12 (HybridCache), RustFS 1.0.6 (S3), MailKit 5 + Mailpit (dev/test), Caddy 2.11, Scalar 3.2, OpenTelemetry 1.21, xUnit v3 + Testcontainers 4.17, Angular 22.3 + TypeScript 6.0 + Tailwind CSS 4.3, Node.js 26 LTS.
- Architecture: DDD + Vertical Slices in backend and frontend. Projects: AppHost, ServiceDefaults, Api, Worker, Core, Tests (Unit/Integration), Web (Angular).
- User language: pt-BR. Talk to the user and write business docs and Level 3 skills in it. Code, comments, technical docs and commits are in English.

## Commands (from the repository root)
- Install: `dotnet tool restore && dotnet restore && npm --prefix src/AspireBoot.Web ci`
- Run (everything, via Aspire): `dotnet run --project src/AspireBoot.AppHost` (or `aspire run`), then open https://localhost:8443
- Test: `./scripts/test.sh` (detects Docker or Podman for Testcontainers). Filters pass straight to `dotnet test`, for example `./scripts/test.sh --filter-namespace 'AspireBoot.Tests.Unit*'`.
- Lint/format: `dotnet format AspireBoot.slnx` and `npm --prefix src/AspireBoot.Web run lint && npm --prefix src/AspireBoot.Web run format`
- Build: `dotnet build AspireBoot.slnx && npm --prefix src/AspireBoot.Web run build`
- Full gate: `./scripts/verify.sh`
- New migration: `dotnet ef migrations add <Name> -p src/AspireBoot.Core -s src/AspireBoot.Api -o Persistence/Migrations`

## Non-negotiable engineering rules
These rules exist so that AI-generated code stays consistent. Follow them exactly, and when in doubt, ask instead of improvising.
1. **Placement:** put every file where `architecture-ddd-vertical-slices` says. A feature is a set of slice folders (`<Context>/<Group>/<VerbNoun>/`) in Api/Worker/Web, plus domain changes in `Core/<Context>/Domain`. No `Services/`, `Helpers/`, `Utils/`, `Managers/`, `DTOs/`, `components/` folders. Slices never call other slices.
2. **Domain first:** business rules and invariants live in aggregates and value objects (`Core/<Context>/Domain`), never in endpoints, handlers, EF configurations or Angular components. No anemic models.
3. **Secure by default:** the fallback authorization policy requires authentication, so anonymous endpoints must say `.AllowAnonymous()` deliberately. Authorization uses named policies. Validate all input. Return Problem Details, never exception details. Every /api endpoint declares a named rate-limit policy from `RateLimitPolicies` (partitioned by IP, and by user when authenticated); 429 returns Problem Details with Retry-After. Accounts are passwordless: passkeys only, with no password API (ADR 0003). Never log secrets or PII.
4. **Messaging:** cross-process side effects (email, audit, cleanup) go through the Wolverine outbox to the Worker. Never call SMTP or publish to RabbitMQ directly from a request.
5. **Data access:** use `AppDbContext` directly (no repository layer). Reads use `AsNoTracking` + `Select` projections + pagination. Every query-driven index is declared in configuration. Every model change ships with a reviewed migration.
6. **Observability:** every slice is traceable. Use source-generated `[LoggerMessage]` logging (no string interpolation) and business metrics per `opentelemetry`, with low-cardinality attributes only.
7. **Tests first:** TDD. Domain → `tests/AspireBoot.Tests/Unit`. Slices → `tests/AspireBoot.Tests/Integration` against real containers. Frontend → `*.spec.ts` beside the code. Lint, build and all tests must pass before committing.
8. **Frontend:** follow Angular's official AI rules (signals, standalone, `input()`/`output()`, native control flow, `inject()`, Signal Forms, lazy routes). No hard-coded user-facing text: translate with the `I18n` service (`| t` in templates, `I18n.t()` in TypeScript) and add each id to all three JSON catalogs in `public/i18n/` (pt-BR/en-US/es-ES; ADR 0001). WCAG 2.2 AA and responsive from 320px. Tailwind tokens only from `@theme`.
9. **Performance:** async with `CancellationToken` everywhere, no N+1, no unbounded lists, HybridCache for hot reads, and no blocking calls.
10. **Dependencies:** versions are pinned centrally (`Directory.Packages.props`, `package-lock.json`, image tags). Check official docs for the pinned version before using an API. Do not add a package when the framework already solves the problem.

## Workflow rules
- Follow the architecture and conventions in `docs/architecture.md`. Record new significant decisions as ADRs in `docs/adr/`.
- Never commit secrets. Configuration comes from environment variables or user-secrets (`deploy/.env.example` documents production keys).
- One commit per feature (`feat(<scope>): <summary>`). Intermediate state lives in `.aiw/current/`.
- Branch mode is stored in `.aiw/state.json` (`main` or `branch`), and so is the trunk branch (`trunkBranch`). Never commit project work to any other branch.
