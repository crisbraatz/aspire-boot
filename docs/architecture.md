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
| Object storage | RustFS (S3-compatible, AWS SDK S3), private bucket | 1.0.6 | Self-hosted private file storage (avatars), provisioned by a Wolverine system part |
| Image processing | SkiaSharp + SkiaSharp.NativeAssets.Linux.NoDependencies (MIT) | 4.157.0 | Decodes and re-encodes avatars server side; see ADR 0004 |
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
      Domain/                    # aggregates, value objects, domain events, RoleAssignment
      Users/<Slice>/             # shared use-case logic per slice when needed by Api+Worker
      Shared/
    Persistence/                 # AppDbContext, configurations, Auditing/ (ADR 0006), Migrations/
    Messaging/                   # message contracts
    Infrastructure/              # email, storage, cache adapters
  AspireBoot.Api/
    Identity/
      Auth/<Slice>/              # Register, ConfirmEmail, ...
      Account/<Slice>/           # GetMe, UploadAvatar, RemovePasskey, ...
      Admin/<Slice>/             # ListUsers, GetUser, GetUserAvatar, ChangeUserRole (group /api/identity/admin)
      Shared/                    # session validation, security stamp cache, bootstrap options
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
- **Problem Details codes:** every error carries a camelCase `code` (also the `type` suffix), namespaced by the feature, for example `avatar.tooLarge` or `avatar.dimensionsTooLarge`. Clients branch on the code, never on the title.
- **Persistence naming:** database columns are snake_case, including in raw SQL (`UseSnakeCaseNamingConvention`). Raw SQL that selects entity rows must include `xmin`, or use projections. The identity schema is `IdentitySchemaVersions.Version3` (passkey table `AspNetUserPasskeys`), set in `AddCoreInfrastructure` so the API and the Worker share one model.
- **Transactions:** the outbox is the transaction boundary. Slices that change state and send messages call `SaveChangesAndFlushMessagesAsync`, which commits the transaction and flushes the outbox in one step. Locks that protect invariants (the last passkey, the Admin role row) run inside the same transaction. Audit rows are written by the SaveChanges interceptor in that same transaction (ADR 0006).
- **Naming:** a slice folder is named with a verb and a noun (`ListUsers`). Files inside it carry the slice name (`ListUsersEndpoint`, `ListUsersRequest`, `ListUsersHandler`).
- **API style:** REST-ish resource routes under `/api`, Minimal API endpoint groups, `TypedResults`, RFC 9457 Problem Details for every error, and pagination with explicit limits.
- **Errors:** expected failures return result types/Problem Details. Exceptions are reserved for the truly exceptional.
- **Configuration:** strongly typed options, validated on start. Secrets come only from user-secrets (dev) or environment variables (prod).
- **Logging:** structured logs with source-generated `LoggerMessage`. No PII or secrets in logs. Every slice emits traces. Key business metrics use `System.Diagnostics.Metrics`. Identity metrics use the meter `AspireBoot.Identity` (`IdentityMetrics`) with low-cardinality tags only (outcome or policy name, never email, user id or passkey name).
- **Rate limiting (ADR 0002):** every endpoint under `/api` declares one named policy from `RateLimitPolicies` (`signup`, `email-link`, `passkey-ceremony`, `authenticated`, `avatar-upload`, `admin`, `public`). Authenticated callers are partitioned by user, anonymous callers by client IP. Only the Caddy network is trusted for `X-Forwarded-For` (`ReverseProxy:KnownNetworks`, `ForwardLimit = 1`). A rejected request returns 429 Problem Details with `Retry-After`. `RateLimitCoverageTests` fails when an endpoint has no policy or an unsafe endpoint accepts a request without the antiforgery token.
- **Performance:** async everywhere, `AsNoTracking` for reads, projections instead of loading entities, indexes declared in EF configurations, and output caching or Redis caching where reads dominate.
- **Frontend:** standalone components, signals, `OnPush`/zoneless, lazy-loaded routes per slice, Tailwind utilities, runtime i18n (ADR 0001), and WCAG 2.2 AA (semantic HTML, keyboard navigation, focus management, contrast). The API client sends `X-XSRF-TOKEN` read from the `XSRF-TOKEN` cookie. The cookie is issued by `GET /api/antiforgery/token`, and the token must be refreshed after sign-in and sign-out, because the server rotates it with the session.
- **Formatting:** `dotnet format` + `.editorconfig` with analyzers as errors. ESLint + Prettier on the frontend.

## Avatars and private object storage
Implemented in feature 002. Files: `Core/Infrastructure/Images`, `Core/Infrastructure/Storage`, `Core/Identity/Domain/AvatarReference.cs`, `Api/Identity/Account/{UploadAvatar,RemoveAvatar,GetAvatar}`, `Worker/Identity/Avatars/{DeleteAvatarObject,CheckAvatarOrphan}`.

- **Processing (`AvatarImageProcessor`, ADR 0004).** `Process(Stream)` returns `Result<byte[]>`:
  1. Magic bytes pick the codec (JPEG `FF D8 FF`, PNG, WebP `RIFF....WEBP`). The file extension and Content-Type are ignored. Anything else fails with `avatar.unsupportedType` (415).
  2. `SKCodec` reads the header without decoding pixels. A width or height above 8000 px, or more than 24 MP, fails with `avatar.dimensionsTooLarge` (400). A missing codec or a failed decode fails with `avatar.corrupt` (400).
  3. JPEG and WebP decode at the smallest scale whose short side is still at least 256 px. PNG has no scaled decode, so it decodes at full size, which the header cap already bounds.
  4. The EXIF orientation is applied, the image is center-cropped to a square, resampled to 256x256 (Mitchell cubic) and encoded as WebP quality 80. Re-encoding from pixels drops EXIF, XMP and GPS data.
- **Concurrency limit.** `AvatarProcessingLimiter` is a process-wide `SemaphoreSlim`, sized by `Avatars:MaxConcurrentProcessing` (default 2). Peak decode memory is about 96 MB per slot (24 MP x 4 B, the worst PNG), so the default of 2 needs about 192 MB of headroom on the API host; see ADR 0004 before raising it. A request waits up to `Avatars:ProcessingWaitSeconds` (default 10 s) for a slot, then gets `503 avatar.busy`. Decoding runs off the request thread, and the slot is always released.
- **Storage (`IAvatarStorage`, `S3AvatarStorage`).** Objects are written with `image/webp` to the bucket `Storage:Bucket` (default `aspireboot`), which is private: the code sets no bucket policy and no ACL. Keys are `avatars/{userId:N}/{objectId:N}.webp`, built only by `AvatarReference.KeyFor` (`For` uses it too). The Worker accepts a key only when `AvatarKeys.BelongsTo` finds exactly one GUID object of that user. The client never supplies a key or a file name. `DeleteAsync` is idempotent.
- **Bucket provisioning.** `StorageBucketResource` (an `IStatefulResource`) runs HeadBucket and then PutBucket. It is exposed through `StorageSystemPart`, a Wolverine `ISystemPart`, because `AddResourceSetupOnStartup()` and `resources setup` only enumerate resources reported by system parts. A bare `IStatefulResource` in DI is never provisioned. The API and the Worker call `AddResourceSetupOnStartup()` in every environment. In deployment, the compose `migrator` runs `resources setup` before they start.
- **Domain.** `User.Avatar` (an `AvatarReference`, nullable) and `User.AvatarVersion`, which only grows, so a version is never reused after a removal. `ChangeAvatar` and `RemoveAvatar` return the superseded reference. `RemoveAvatar` is idempotent. Persisted as `avatar_key` and `avatar_version`. The `users` table maps PostgreSQL `xmin` as a row version, so concurrent writes are detected.
- **Upload: `PUT /api/identity/account/avatar`** (multipart field `file`, at most 5 MiB). The endpoint carries a `RequestSizeLimit` of 6 MiB, which overrides the 32 KB group limit. The rate policy is `avatar-upload` (10 requests per 10 minutes). The form is read manually, not bound as `IFormFile`, so the antiforgery filter runs before any binding. Antiforgery still applies, with the token from the form or the `X-XSRF-TOKEN` header. Steps:
  1. Check the size and take a limiter slot, then process in memory.
  2. Commit a delayed `AvatarObjectOrphanCheck` (`Avatars:OrphanCheckDelay`, default 1 h) in its own transaction, before the write. The Worker deletes that key unless it is the user's current avatar, which cleans up after a crash.
  3. Put the object. A failure returns `503 avatar.storageUnavailable` and leaves the profile untouched.
  4. Run the commit loop, at most 2 attempts. Each attempt opens a new DI scope, so it gets a fresh `AppDbContext`, outbox and transaction. The attempt calls `ChangeAvatar`, publishes `AvatarObjectSuperseded` for the old key, and calls `SaveChangesAndFlushMessagesAsync`. A `DbUpdateConcurrencyException` from the `xmin` check retries. If both attempts fail, the new object is deleted on a best-effort basis and the response is `503 avatar.busy`.
  - Response `200 { avatarUrl }`, where `avatarUrl` is `/api/identity/account/avatar/{version}`.
- **Remove: `DELETE /api/identity/account/avatar`.** Always `204` (idempotent). It publishes `AvatarObjectSuperseded` only when an avatar existed, and it records `aspireboot.identity.avatar.removals`.
- **Serve: `GET /api/identity/account/avatar/{version}`.** The object key comes from the caller's own row, never from the URL, which only carries the version. A version that does not match the current one, or a missing avatar, returns `404`. The response is `image/webp` with `Cache-Control: private, max-age=31536000, immutable` and `ETag: "v{version}"`, and it answers `304` to a matching `If-None-Match`. The policy is `authenticated`. The endpoint streams the bytes instead of issuing a presigned URL, so RustFS stays on the private network and the session, authorization and audit stay in the API.
- **Profile.** `GET /api/identity/account/me` returns `avatarUrl` (or `null`), built from the same row.
- **Messages.** `AvatarObjectSuperseded(UserId, Key)` and `AvatarObjectOrphanCheck(UserId, Key)` (`Core/Messaging/Identity/AvatarMessages.cs`) go to the RabbitMQ queue `identity-avatars`. The Worker slices `Identity/Avatars/DeleteAvatarObject` and `Identity/Avatars/CheckAvatarOrphan` log only the user id and the object id.
- **Metrics.** `aspireboot.identity.avatar.uploads` (tag `outcome`: `stored`, `rejected_type`, `rejected_size`, `rejected_dimensions`, `corrupt`, `storage_error` for a storage failure, and `conflict` when the profile changed on every retry) and `aspireboot.identity.avatar.removals`.
- **Problem Details codes:** `avatar.unsupportedType` (415), `avatar.tooLarge` (413), `avatar.missing`, `avatar.corrupt`, `avatar.dimensionsTooLarge` (400), `avatar.busy` and `avatar.storageUnavailable` (503).
- **Configuration:** `Avatars:MaxConcurrentProcessing`, `Avatars:ProcessingWaitSeconds`, `Avatars:OrphanCheckDelay` (see `deploy/.env.example`), and `Storage:ServiceUrl`, `Storage:AccessKey`, `Storage:SecretKey`, `Storage:Bucket`, which compose fills from `STORAGE_*`.

## Administration, session revocation and audit (feature 003)
Files: `Core/Identity/Domain/{Role,RoleAssignment}.cs`, `Core/Persistence/Auditing/*`, `Api/Identity/Shared/{SecurityStampCache,SecurityStampPrincipalValidator,BootstrapOptions,HttpAuditActor}.cs`, `Api/Identity/Admin/{ListUsers,GetUser,GetUserAvatar,ChangeUserRole}`, `Worker/Identity/Notifications/SendRoleChangedEmail`.

- **Roles.** Two built-in roles, `Admin` and `User`, seeded by migration `SeedRoles` with fixed identifiers (`Roles.AdminId`, `Roles.UserId`). Each user has exactly one role: a promotion removes `User` and adds `Admin`, and a demotion does the reverse. `Roles.All` and `Roles.IsKnown` validate names.
- **Role rules (`RoleAssignment`, pure functions).** A user cannot demote themselves (`identity.selfDemotion`, 409). The last Admin cannot be demoted (`identity.lastAdmin`, 409). An unknown role is `identity.invalidRole` (400). Setting the current role is a no-op.
- **Keeping at least one Admin.** `PUT /api/identity/admin/users/{id}/role` runs in a transaction that first locks the Admin role row (`SELECT ... FOR UPDATE`), then counts the Admins and applies the rules above. Parallel demotions serialize on that lock. The change ends the target's sessions (see revocation below), and `RoleChangedNotification` is sent through the outbox.
- **Bootstrap Admin.** `Bootstrap:AdminEmail` (env `Bootstrap__AdminEmail`, optional, validated as an email address) names the one address that becomes Admin. It applies only in the confirmation step of `CompleteRegistration`, after the email is confirmed, and only once: the rule (`RoleAssignment.ShouldBootstrapAdmin`) compares trimmed, case-insensitively, and does nothing when no address is configured, the email is unconfirmed, or the user is already an Admin. The grant runs without the Admin row lock, which is safe because a grant never lowers the Admin count. Recovery never grants Admin. Logs record the user id only.
- **Admin group.** `/api/identity/admin` is mapped with `RequireAuthorization(AuthorizationPolicies.Admin)` and `RequireRateLimiting(RateLimitPolicies.Admin)` (60 requests per 60 s, per user). The authorization middleware runs before the endpoint antiforgery filter, so a non-admin unsafe request gets 403 before the token is checked.
- **Listing.** `GET /users` takes `search`, `role`, `page` and `pageSize` (1 to 100). The search is a case-insensitive `ILIKE` on the display name and the email, with `%` and `_` escaped and bound as a parameter. Results are ordered by `created_at desc, id`. The detail (`GET /users/{id}`) adds passkey count and culture. The avatar of another user is served only to Admins, through the same streaming code as the owner's avatar.
- **Immediate session revocation (ADR 0005).** Every authenticated request checks the cookie's security stamp against the current stamp, read through HybridCache (key `identity:stamp:{id}`, 60 s maximum lifetime, local cache disabled, Redis as the single source). Stamp changes call `StampChangedAsync` after the commit. If the cache fails, the database is read; if the database fails too, the request is rejected.
- **Audit trail (ADR 0006).** A singleton SaveChanges interceptor writes `audit_entries` rows in the same SaveChanges and transaction, including Wolverine flushes. Changes are recorded as jsonb with old and new values. Secrets are masked, and passkey data is excluded by type. The table is append-only through triggers. Audited entities use client-generated keys, and the actor comes from a singleton `IAuditActor`. `AddCoreInfrastructure` must run before `AddNpgsqlDbContext`.

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
- **Sessions:** cookie `__Host-aspireboot`, HttpOnly, Secure, SameSite=Strict, 8 hours with sliding expiration. "Keep me signed in" is a persistent cookie only by explicit choice. Every request checks the security stamp against the cached current stamp, so recovery, passkey removal and role changes end the other sessions on their next request (ADR 0005).
- Antiforgery (CSRF) with the `X-XSRF-TOKEN` header on every unsafe `/api` request, and role-based authorization policies (RBAC: Admin, User), enforced on the server. The fallback policy requires an authenticated user.
- **Enumeration:** sign-up, resend and recovery return identical `202` responses. Sign-in failures return one generic `401`. Residual timing risk is accepted (ADR 0003).
- **Rate limiting (ADR 0002):** named policies on every endpoint, partitioned by user or client IP, with only the Caddy network trusted for forwarded headers. In-memory counters are accepted for a single VPS.
- **Configuration keys** (documented in `deploy/.env.example`):
  - `Passkeys__ServerDomain`, `Passkeys__AllowedOrigins__0`: relying party and exact origins.
  - `App__PublicBaseUrl`: base of the links in emails (`/confirm`, `/recover/complete`).
  - `Avatars__MaxConcurrentProcessing` (default 2, about 96 MB of peak memory per slot), `Avatars__ProcessingWaitSeconds` (default 10) and `Avatars__OrphanCheckDelay` (default 1 hour): avatar processing limits and the orphan check delay.
  - `Storage__ServiceUrl`, `Storage__AccessKey`, `Storage__SecretKey`, `Storage__Bucket`: the private RustFS bucket for avatars.
  - `Identity__EmailCooldownSeconds` (default 60).
  - `Bootstrap__AdminEmail`: the address that becomes Admin when it confirms its email. Empty means no bootstrap Admin (compose fills it from `BOOTSTRAP_ADMIN_EMAIL`).
  - `RateLimiting__<policy>__PermitLimit` and `RateLimiting__<policy>__WindowSeconds`, with the defaults in `RateLimitingExtensions`.
  - `ReverseProxy__KnownNetworks__0`: the proxy network whose forwarded headers are trusted.
- Input validation in every slice. EF parameterized queries only. Credential JSON is capped in size and passkey names are 1 to 50 characters.
- Security headers (CSP, HSTS and others) set by Caddy and the API.
- Data Protection keys persisted in PostgreSQL and protected with a certificate in production.
- Uploads: content is checked by magic bytes, re-encoded from pixels, and capped in size and dimensions; object keys are server-generated. See [Avatars and private object storage](#avatars-and-private-object-storage).
- Dependency audit (osv-scanner, `dotnet list package --vulnerable`, `npm audit`), secret scanning (gitleaks), container scanning (trivy) and SAST (semgrep) in CI.

## Deployment
- **Local:** the AppHost runs every container plus the Api, the Worker and the Angular dev server. Caddy serves both on `https://localhost:8443` (same origin as production, so cookies and antiforgery behave identically).
- **Default:** a single VPS running `deploy/compose.yaml` (Podman or Docker) with Caddy (public HTTPS through Let's Encrypt), the API, the Worker, the Angular static build served by Caddy, PostgreSQL, RabbitMQ, Redis and RustFS on a private network. All containers run as non-root.
- Migrations run as an explicit, controlled step: the one-shot `migrator` service runs the EF Core migration bundle and `resources setup` (Wolverine tables and RabbitMQ topology) before the API and Worker start. Production never migrates on startup. The API migrates automatically only in Development.
- Backups: scheduled PostgreSQL dumps and RustFS bucket backups, documented in the README.
- Telemetry: an OTLP endpoint is configurable (the Aspire dashboard in standalone mode by default).
- **Optional:** managed cloud (Azure/AWS) by swapping container resources for managed services through Aspire.

## Decision records
Significant decisions live in [`adr/`](adr/README.md): the initial stack and architecture (ADR 0000), runtime i18n (ADR 0001), rate limiting (ADR 0002), passwordless passkeys (ADR 0003), avatar image processing with SkiaSharp (ADR 0004), immediate session revocation (ADR 0005) and the audit trail (ADR 0006).
