# Architecture

## Stack
Versions verified on 2026-11-25 against NuGet, npm, the .NET release index, nodejs.org and Docker Hub. Container image tags marked *pin at scaffold* are resolved to the latest stable tag when the scaffold is generated.

| Layer | Technology | Version | Why |
| :--- | :--- | :--- | :--- |
| Runtime | .NET (SDK) | 10.0 LTS (SDK 10.0.404) | Long-term support; Aspire-native |
| Orchestration | Aspire | 17.0.0 | Local orchestration, service defaults, dashboard |
| API | ASP.NET Core Minimal APIs | 10.0 | Lightweight endpoints that fit vertical slices |
| Auth | ASP.NET Core Identity 10 with passkeys (WebAuthn) + cookies + roles | 10.0.15 | Passwordless accounts; see ADR 0003 |
| Rate limiting | ASP.NET Core `System.Threading.RateLimiting` (in memory) | 10.0 | Named policies on every `/api` endpoint; see ADR 0002 |
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
| Frontend i18n | In-house signal `I18n` service with JSON catalogs | — | Runtime language switch without reload; see ADR 0001 |
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
- **Language:** all code, identifiers, comments, logs and technical docs are in English. The UI ships in pt-BR, en-US and es-ES, and no user-facing string is hard-coded.
- **Runtime i18n (ADR 0001):** the locale switches at runtime without a reload. `I18n` (`core/i18n/i18n.ts`) is a signal-based service, `| t` (`TranslatePipe`) is the template pipe, and `I18nTitleStrategy` translates route titles.
  - Catalogs are flat JSON files keyed by stable message ids in `src/AspireBoot.Web/public/i18n/{pt-BR,en-US,es-ES}.json`. en-US is the fallback and loads with every culture.
  - Every new message id goes into all three catalogs. The catalog parity spec (`catalog-parity.spec.ts`) fails on missing or extra keys.
  - Use `{param}` placeholders for interpolation. Plurals use separate ids.
  - The culture is stored in `localStorage` for anonymous visitors and in `Users.PreferredCulture` for accounts. Server emails use the same culture.
  - `@angular/localize` is not used.
- **Persistence naming:** database columns are snake_case, including in raw SQL (`UseSnakeCaseNamingConvention`). The identity schema is `IdentitySchemaVersions.Version3` (passkey table `AspNetUserPasskeys`), set in `AddCoreInfrastructure` so the API and the Worker share one model.
- **Transactions:** the outbox is the transaction boundary. Slices that change state and send messages call `SaveChangesAndFlushMessagesAsync`, which commits the transaction and flushes the outbox in one step. Locks that protect invariants (for example the last passkey) run inside the same transaction.
- **Naming:** a slice folder is named with a verb and a noun (`ListUsers`). Files inside it carry the slice name (`ListUsersEndpoint`, `ListUsersRequest`, `ListUsersHandler`).
- **API style:** REST-ish resource routes under `/api`, Minimal API endpoint groups, `TypedResults`, RFC 9457 Problem Details for every error, and pagination with explicit limits.
- **Errors:** expected failures return result types/Problem Details. Exceptions are reserved for the truly exceptional.
- **Configuration:** strongly typed options, validated on start. Secrets come only from user-secrets (dev) or environment variables (prod).
- **Logging:** structured logs with source-generated `LoggerMessage`. No PII or secrets in logs. Every slice emits traces. Key business metrics use `System.Diagnostics.Metrics`. Identity metrics use the meter `AspireBoot.Identity` (`IdentityMetrics`) with low-cardinality tags only (outcome or policy name, never email, user id or passkey name).
- **Rate limiting (ADR 0002):** every endpoint under `/api` declares one named policy from `RateLimitPolicies` (`signup`, `email-link`, `passkey-ceremony`, `authenticated`, `public`). Authenticated callers are partitioned by user, anonymous callers by client IP. Only the Caddy network is trusted for `X-Forwarded-For` (`ReverseProxy:KnownNetworks`, `ForwardLimit = 1`). A rejected request returns 429 Problem Details with `Retry-After`. `RateLimitCoverageTests` fails when an endpoint has no policy or an unsafe endpoint accepts a request without the antiforgery token.
- **Performance:** async everywhere, `AsNoTracking` for reads, projections instead of loading entities, indexes declared in EF configurations, and output caching or Redis caching where reads dominate.
- **Frontend:** standalone components, signals, `OnPush`/zoneless, lazy-loaded routes per slice, Tailwind utilities, runtime i18n (ADR 0001), and WCAG 2.2 AA (semantic HTML, keyboard navigation, focus management, contrast). The API client sends `X-XSRF-TOKEN` read from the `XSRF-TOKEN` cookie. The cookie is issued by `GET /api/antiforgery/token`, and the token must be refreshed after sign-in and sign-out, because the server rotates it with the session.
- **Formatting:** `dotnet format` + `.editorconfig` with analyzers as errors. ESLint + Prettier on the frontend.

## Testing strategy
- **Unit (`tests/AspireBoot.Tests/Unit`):** domain invariants and pure logic, with xUnit v3 and NSubstitute.
- **Integration (`tests/AspireBoot.Tests/Integration`):** full HTTP slices and Worker handlers against real PostgreSQL, RabbitMQ, Redis, RustFS and Mailpit through Testcontainers, using `WebApplicationFactory` and Respawn to reset data.
  - Integration test classes run serialized (`MaxThreads = 1`), because they share one database and one Mailpit inbox.
  - Mailpit (`axllent/mailpit`, same version as the AppHost) captures the mail. `MailpitClient` polls its HTTP API with a timeout.
  - `AspireBootWorkerFactory` hosts the Worker in memory, so outbox messages reach real SMTP. Wolverine uses dynamic code generation in the `Testing` environment (production stays static).
  - `SoftwareAuthenticator` (in `Support/WebAuthn`) performs WebAuthn ceremonies: an ES256 key, `clientDataJSON`, authenticator data and a `none` attestation, all against origin `https://localhost`.
  - `IdentityClient` keeps a cookie-aware client and fetches the antiforgery token. The API factory uses high rate-limit values. Only the 429 test lowers them.
  - Expiry tests use `AspireBootExpiringApiFactory`, which sets a 1 s token lifespan, instead of `FakeTimeProvider`.
  - Test users are created with culture `pt-BR`, so localized emails are covered.
- **Frontend:** Vitest for components and services, with `HttpTestingController` and a mocked `navigator.credentials`.
- **Run:** `./scripts/test.sh` (Docker or Podman auto-detected). CI runs everything.
- **TDD:** every behavior change starts with a failing test.

## Security
- **Passwordless accounts (ADR 0003):** sign-in and account recovery use passkeys (WebAuthn / FIDO2) only. No password is accepted or stored: there are no password APIs, and `PasswordHash` stays null. `NoPasswordApisTests` fails if the API or Core reference a password API.
  - Relying party: `Passkeys:ServerDomain` and `Passkeys:AllowedOrigins` pin the RP ID and the exact origins (`ValidateOrigin`). Resident keys are required.
  - Confirmation and recovery tokens are single use and last 2 hours. Completing a flow updates the security stamp, which ends the other sessions.
  - The last passkey of an account cannot be removed. The removal locks the account row, so parallel removals cannot both succeed.
- **Sessions:** cookie `__Host-aspireboot`, HttpOnly, Secure, SameSite=Strict, 8 hours with sliding expiration. "Keep me signed in" is a persistent cookie only by explicit choice. The security stamp is checked every `Identity:SecurityStampValidationSeconds` (default 60).
- Antiforgery (CSRF) with the `X-XSRF-TOKEN` header on every unsafe `/api` request, and role-based authorization policies (RBAC: Admin, User), enforced on the server. The fallback policy requires an authenticated user.
- **Enumeration:** sign-up, resend and recovery return identical `202` responses. Sign-in failures return one generic `401`. Residual timing risk is accepted (ADR 0003).
- **Rate limiting (ADR 0002):** named policies on every endpoint, partitioned by user or client IP, with only the Caddy network trusted for forwarded headers. In-memory counters are accepted for a single VPS.
- **Configuration keys** (documented in `deploy/.env.example`):
  - `Passkeys__ServerDomain`, `Passkeys__AllowedOrigins__0`: relying party and exact origins.
  - `App__PublicBaseUrl`: base of the links in emails (`/confirm`, `/recover/complete`).
  - `Identity__EmailCooldownSeconds` (default 60) and `Identity__SecurityStampValidationSeconds` (default 60).
  - `RateLimiting__<policy>__PermitLimit` and `RateLimiting__<policy>__WindowSeconds`, with the defaults in `RateLimitingExtensions`.
  - `ReverseProxy__KnownNetworks__0`: the proxy network whose forwarded headers are trusted.
- Input validation in every slice. EF parameterized queries only. Credential JSON is capped in size and passkey names are 1 to 50 characters.
- Security headers (CSP, HSTS and others) set by Caddy and the API.
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

## Decision records
Significant decisions live in [`adr/`](adr/README.md): the initial stack and architecture (ADR 0000), runtime i18n (ADR 0001), rate limiting (ADR 0002) and passwordless passkeys (ADR 0003).
