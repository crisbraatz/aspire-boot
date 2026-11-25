---
name: aspire
description: Load when editing AspireBoot.AppHost, ServiceDefaults, adding infrastructure resources, wiring client integrations, or publishing.
level: 2
kind: technology
version: Aspire 17.0 on .NET 10
sources:
  - https://aspire.dev/llms.txt — Microsoft
  - https://aspire.dev/get-started/csharp-service-defaults/ — Microsoft
  - https://aspire.dev/integrations/databases/postgres/postgres-get-started/ — Microsoft
  - https://aspire.dev/integrations/frameworks/javascript/ — Microsoft
  - https://aspire.dev/integrations/messaging/rabbitmq/rabbitmq-host/ — Microsoft
  - https://aspire.dev/fundamentals/persist-data-volumes/ — Microsoft
  - https://aspire.dev/testing/overview/ — Microsoft
  - https://aspire.dev/deployment/ — Microsoft
  - https://aspire.dev/integrations/databases/efcore/migrations/ — Microsoft
  - model-knowledge
fetched: 2026-11-25
---

# Aspire

## AppHost rules
- The AppHost is the **dev-time resource graph only**. No business code, and no config values that belong to services.
- Model every dependency as a resource and wire it with `.WithReference(x).WaitFor(x)`:
  - `AddPostgres("postgres").WithDataVolume().AddDatabase("aspireboot")`
  - `AddRabbitMQ("rabbitmq").WithManagementPlugin().WithDataVolume()`
  - `AddRedis("redis").WithDataVolume()`
  - `AddContainer("rustfs", "rustfs/rustfs", "<pinned>")` with S3 port 9000 and console 9001, credentials from parameters
  - Mailpit via `CommunityToolkit.Aspire.Hosting.Mailpit`
  - Caddy via `AddContainer` with a bind-mounted Caddyfile for local HTTPS
  - Angular via `Aspire.Hosting.JavaScript` (`AddViteApp`/`AddJavaScriptApp` with `runScriptName`). Never add `WithHttpEndpoint` to a Vite resource (duplicate endpoint error).
- Secrets: `AddParameter("name", secret: true)`, overridden with `aspire secret set Parameters:<name> <value>`. Data volumes **need stable passwords** (parameters), or the generated password changes and breaks the volume.
- Pin container image tags. Never use `latest`.
- Prefer named volumes over bind mounts (portable). Bind only config files.
- Migrations: `AddEFMigrations(...)` + `RunDatabaseUpdateOnStart()` is **local only**. Production uses `PublishAsMigrationBundle()` or the compose one-shot job.
- Container runtime: set `ASPIRE_CONTAINER_RUNTIME=podman` when Podman is the engine. Scripts auto-detect docker, else podman.

## ServiceDefaults
- Contains only extensions: `AddServiceDefaults()` (OpenTelemetry logs/metrics/traces, default health checks, service discovery, standard resilience on HttpClient) and `MapDefaultEndpoints()`.
- `/health` (ready: all checks) and `/alive` (live tag). The template maps them only in Development. AspireBoot maps them in all environments **on the internal network only**, and Caddy never proxies them publicly.
- Register every custom `ActivitySource`/`Meter` (`AddSource("AspireBoot.*")`, `AddMeter("AspireBoot.*")`, `AddSource("Wolverine")`, `AddMeter("Wolverine*")`). Missing registrations fail silently.
- The OTLP exporter activates when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (the AppHost sets it in dev).
- Worker uses the same ServiceDefaults (generic host) and must not need ASP.NET-only types beyond health endpoints.

## Client integrations (service side)
- `builder.AddNpgsqlDbContext<AppDbContext>("aspireboot")` (or `EnrichNpgsqlDbContext` when Wolverine registers the context). The name must match the AppHost resource.
- `builder.AddRedisClient("redis")`, plus a `HybridCache` + Redis distributed cache.
- RabbitMQ through Wolverine `UseRabbitMqUsingNamedConnection("rabbitmq")`.
- Connection info arrives as `ConnectionStrings__<name>`. Never hard-code hosts or ports.

## Testing
- Prefer `WebApplicationFactory` + Testcontainers for slice tests (in-process, DI overrides).
- `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireBoot_AppHost>()`) is closed-box smoke testing only (no DI substitution). Use it sparingly.

## Publish/deploy
- `aspire run` for dev. `aspire publish` generates artifacts (Docker Compose) with secret placeholders. AspireBoot keeps a reviewed, hand-maintained `deploy/compose.yaml` as the source of truth for VPS deployment.
- Vite `import.meta.env` is build-time. Runtime config for the SPA comes from `/config.json` or same-origin `/api`.

## Pitfalls
- RabbitMQ 4.3+ rejects non-durable non-exclusive queues. Wolverine queues must be durable.
- Do not put non-service code in ServiceDefaults.
- `ASPIRE_ALLOW_UNSECURED_TRANSPORT` stays false. The dashboard uses HTTPS.
- The `microsoft/aspire` repo AGENTS.md is for Aspire contributors, so do not apply it here.
