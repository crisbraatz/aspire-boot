# Architecture

## Stack
Versions verified on 2026-11-25 against NuGet, npm, the .NET release index, nodejs.org and Docker Hub. Container image tags marked *pin at scaffold* are resolved to the latest stable tag when the scaffold is generated.

| Layer | Technology | Version | Why |
| :--- | :--- | :--- | :--- |
| Runtime | .NET (SDK) | 10.0 LTS (SDK 10.0.404) | Long-term support; Aspire-native |
| Orchestration | Aspire | 17.0.0 | Local orchestration, service defaults, dashboard |
| API | ASP.NET Core Minimal APIs | 10.0 | Lightweight endpoints that fit vertical slices |
| Auth | ASP.NET Core Identity (cookies) + roles | 10.0 | Proven account/session/RBAC foundation |
| API docs | Scalar.AspNetCore + Microsoft.AspNetCore.OpenApi | 3.2.0 / 10.0 | Interactive OpenAPI documentation |
| ORM | EF Core + Npgsql provider (migrations) | 10.0.15 / 10.0.4 | Typed data access, versioned schema |
| Database | PostgreSQL | 19 (alpine) | Relational store for Identity, domain, outbox/inbox |
| Messaging | Wolverine (EF Core + PostgreSQL outbox, RabbitMQ transport) | 6.65.1 | Transactional outbox, durable inbox, handlers |
| Broker | RabbitMQ | 4.x management-alpine (*pin at scaffold*) | Reliable message transport API → Worker |
| Cache | Redis (StackExchange.Redis) | 8.x alpine (*pin at scaffold*) | Cache, session tickets, rate limits |
| Object storage | RustFS (S3-compatible, AWS SDK S3) | 1.0.6 | Self-hosted file storage (avatars) |
| Email | MailKit | 5.0.0 | SMTP delivery |
| Dev/test email | Mailpit (CommunityToolkit.Aspire.Hosting.Mailpit) | v1.34 / 17.0.0 | Captures all mail locally and in tests |
| Reverse proxy | Caddy | 2.x alpine (*pin at scaffold*) | Automatic HTTPS locally and in production |
| Observability | OpenTelemetry (.NET SDK, OTLP) | 1.21.0 | Logs, metrics, traces across API and Worker |
| Tests | xUnit v3 + Testcontainers + NSubstitute + Respawn | 4.0.3 / 4.17.0 | Real infrastructure in integration tests |
| Frontend | Angular (standalone, signals, zoneless) | 22.3.0 | Structured SPA framework |
| Frontend language | TypeScript | 6.0.x (TypeScript 7 is excluded: Angular 22 and typescript-eslint both require >=6.0 <6.1) | Type safety |
| Styling | Tailwind CSS | 4.3.5 | Utility-first, responsive styling |
| Frontend tests | Vitest (Angular unit-test builder) + @vitest/coverage-v8 | 5.x | Fast unit/component tests |
| Node.js | Node.js | 26 LTS | Frontend toolchain |
| Containers | Podman or Docker (auto-detected) | — | Local infra, tests, deployment |

## Architecture style
**DDD + Vertical Slices**, in both the backend and the frontend.

- **Domain-Driven Design:** business concepts are modeled as aggregates, entities, value objects and domain events in `Core`. Invariants live inside the domain types, never in endpoints or handlers.
- **Vertical Slices:** each use case (for example `Register` or `ListUsers`) lives in one folder with its request, validation, handler, endpoint/page and tests. Slices do not call each other directly. Shared behavior goes in the bounded context's `Shared/` folder or in the domain.
- **Projects and dependency rule:**
  - `AspireBoot.AppHost` orchestrates everything (dev only).
  - `AspireBoot.ServiceDefaults` holds the OpenTelemetry, health and resilience defaults.
  - `AspireBoot.Api` contains HTTP slices (endpoints) and Identity wiring. Depends on Core.
  - `AspireBoot.Worker` contains message and scheduled handlers. Depends on Core. It is a slim ASP.NET Core host that exposes only `/health` and `/alive` on the internal network (used by container health checks).
  - `AspireBoot.Core` contains the domain, use-case logic shared by API and Worker, persistence (EF DbContext, migrations), messaging contracts and infrastructure adapters. It depends on no host project.
  - `AspireBoot.Tests` is a single project split into `Unit/` and `Integration/`, mirroring slice paths.
  - `AspireBoot.Web` is the Angular app.
  - Dependencies point inward: Api/Worker → Core. Core never references Api or Worker.
- **Communication:** the API writes state and publishes messages through the Wolverine outbox in the same transaction. The Worker consumes them through a durable inbox. Trace context flows through the messages.

## Folder structure
```
AspireBoot.slnx
global.json  Directory.Build.props  Directory.Packages.props  .editorconfig
src/
  AspireBoot.AppHost/
  AspireBoot.ServiceDefaults/
  AspireBoot.Core/
    Identity/                    # bounded context
      Domain/                    # aggregates, value objects, domain events
      Users/<Slice>/             # shared use-case logic per slice when needed by Api+Worker
      Shared/
    Persistence/                 # AppDbContext, configurations, Migrations/
    Messaging/                   # message contracts
    Infrastructure/              # email, storage, cache adapters
  AspireBoot.Api/
    Identity/
      Auth/<Slice>/              # Register, ConfirmEmail, ...
      Users/<Slice>/             # ListUsers, GetUser, UpdateUser, ...
      Sessions/<Slice>/
    Shared/                      # filters, problem details, auth policies
  AspireBoot.Worker/
    Identity/<Slice>/            # SendEmail, AuditUser, CleanupAvatars
  AspireBoot.Web/                # Angular
    src/app/
      core/                      # app-wide singletons: http, auth state, i18n, layout
      shared/ui/                 # reusable, accessible UI components
      identity/
        auth/<slice>/            # register, sign-in, recovery, ...
        users/<slice>/           # list-users, get-user, ...
        profile/<slice>/
        sessions/<slice>/
        shared/                  # context-level models, api clients, guards
tests/
  AspireBoot.Tests/
    Unit/<mirrors src slices>/
    Integration/<mirrors src slices>/
deploy/
  compose.yaml  Caddyfile  .env.example
docs/
  business/overview.md  architecture.md  adr/
```

## Bounded contexts
- **Identity:** accounts, profiles, sessions, users, roles (RBAC).
- **Platform:** technical, cross-cutting endpoints such as public app info. No business rules live here.

## Conventions
- **Language:** all code, identifiers, comments, logs and technical docs are in English. The UI ships in pt-BR, en-US and es-ES, and no user-facing string is hard-coded. The scaffold marks text with Angular i18n (`i18n` attributes and `$localize` with stable `@@ids`). Whether locales switch at build time (one build per locale) or at runtime is decided in the first UI feature and recorded as an ADR.
- **Naming:** a slice folder is named with a verb and a noun (`ListUsers`). Files inside it carry the slice name (`ListUsersEndpoint`, `ListUsersRequest`, `ListUsersHandler`).
- **API style:** REST-ish resource routes under `/api`, Minimal API endpoint groups, `TypedResults`, RFC 9457 Problem Details for every error, and pagination with explicit limits.
- **Errors:** expected failures return result types/Problem Details. Exceptions are reserved for the truly exceptional.
- **Configuration:** strongly typed options, validated on start. Secrets come only from user-secrets (dev) or environment variables (prod).
- **Logging:** structured logs with source-generated `LoggerMessage`. No PII or secrets in logs. Every slice emits traces. Key business metrics use `System.Diagnostics.Metrics`.
- **Performance:** async everywhere, `AsNoTracking` for reads, projections instead of loading entities, indexes declared in EF configurations, and output caching or Redis caching where reads dominate.
- **Frontend:** standalone components, signals, `OnPush`/zoneless, lazy-loaded routes per slice, Tailwind utilities, and WCAG 2.2 AA (semantic HTML, keyboard navigation, focus management, contrast).
- **Formatting:** `dotnet format` + `.editorconfig` with analyzers as errors. ESLint + Prettier on the frontend.

## Testing strategy
- **Unit (`tests/AspireBoot.Tests/Unit`):** domain invariants and pure logic, with xUnit v3 and NSubstitute.
- **Integration (`tests/AspireBoot.Tests/Integration`):** full HTTP slices and Worker handlers against real PostgreSQL, RabbitMQ, Redis, RustFS and Mailpit through Testcontainers, using `WebApplicationFactory` and Respawn to reset data.
- **Frontend:** Vitest for components and services.
- **Run:** `./scripts/test.sh` (Docker or Podman auto-detected). CI runs everything.
- **TDD:** every behavior change starts with a failing test.

## Security
- Identity with secure, HttpOnly, SameSite cookies, antiforgery (CSRF), account lockout, required email confirmation and role-based authorization policies (RBAC: Admin, User), enforced on the server.
- Input validation in every slice. EF parameterized queries only.
- Security headers (CSP, HSTS and others) set by Caddy and the API. Rate limiting on auth endpoints.
- Data Protection keys persisted in PostgreSQL and protected with a certificate in production.
- Uploads: allow-listed content types, size limits and server-generated object keys.
- Dependency audit (osv-scanner, `dotnet list package --vulnerable`, `npm audit`), secret scanning (gitleaks), container scanning (trivy) and SAST (semgrep) in CI.

## Deployment
- **Local:** the AppHost runs every container plus the Api, the Worker and the Angular dev server. Caddy serves both on `https://localhost:8443` (same origin as production, so cookies and antiforgery behave identically).
- **Default:** a single VPS running `deploy/compose.yaml` (Podman or Docker) with Caddy (public HTTPS through Let's Encrypt), the API, the Worker, the Angular static build served by Caddy, PostgreSQL, RabbitMQ, Redis and RustFS on a private network. All containers run as non-root.
- Migrations run as an explicit, controlled step: the one-shot `migrator` service runs the EF Core migration bundle and `resources setup` (Wolverine tables and RabbitMQ topology) before the API and Worker start. Production never migrates on startup. The API migrates automatically only in Development.
- Backups: scheduled PostgreSQL dumps and RustFS bucket backups, documented in the README.
- Telemetry: an OTLP endpoint is configurable (the Aspire dashboard in standalone mode by default).
- **Optional:** managed cloud (Azure/AWS) by swapping container resources for managed services through Aspire.

## ADR-001 — Initial stack and architecture
- Status: accepted
- Context: an open-source template so that technical and non-technical builders can start secure web products with authentication, RBAC and production-grade infrastructure ready, cheap to host on one VPS, and easy for AI assistants to extend without architectural drift.
- Decision: .NET 10 LTS with Aspire. The solution has AppHost, ServiceDefaults, Api, Worker, Core and a single Tests project. The infrastructure is PostgreSQL + EF Core migrations, Wolverine (outbox/inbox) over RabbitMQ, Redis, RustFS, MailKit with Mailpit for dev/tests, Caddy, ASP.NET Core Identity, Scalar and OpenTelemetry. The frontend is Angular 22 + Tailwind 4. The architecture is DDD + Vertical Slices in both backend and frontend.
- Consequences: a solid, secure base with more moving parts than a minimal template. Aspire hides that complexity locally, and production needs a documented compose deployment. Strict slice and dependency rules plus Level 2/3 AI skills are required to keep contributions consistent.
