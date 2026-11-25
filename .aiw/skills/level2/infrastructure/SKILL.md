---
name: infrastructure
description: Load when touching Caddy, Redis/HybridCache, RustFS/S3 storage, MailKit/Mailpit email, compose.yaml, Dockerfiles or container runtime scripts.
level: 2
kind: technology
version: Caddy 2.x, Redis 8.x, RustFS 1.0.6, MailKit 5.0, Mailpit 1.34, Compose Specification
sources:
  - https://caddyserver.com/docs/caddyfile/directives/reverse_proxy — Caddy
  - https://caddyserver.com/docs/automatic-https — Caddy
  - https://caddyserver.com/docs/caddyfile/directives/tls — Caddy
  - https://caddyserver.com/docs/caddyfile/directives/header — Caddy
  - https://caddyserver.com/docs/caddyfile/directives/file_server — Caddy
  - https://caddyserver.com/docs/caddyfile/directives/try_files — Caddy
  - https://caddyserver.com/docs/running — Caddy
  - https://redis.io/docs/latest/operate/oss_and_stack/management/security/ — Redis
  - https://redis.io/docs/latest/operate/oss_and_stack/management/security/acl/ — Redis
  - https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/ — Redis
  - https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid — Microsoft
  - https://github.com/rustfs/rustfs — RustFS
  - https://docs.rustfs.com/en/installation — RustFS
  - https://github.com/jstedfast/MailKit — Jeffrey Stedfast (MailKit author)
  - https://mailpit.axllent.org/docs/ — Mailpit
  - https://mailpit.axllent.org/docs/configuration/smtp/ — Mailpit
  - https://mailpit.axllent.org/docs/integration/ — Mailpit
  - https://docs.docker.com/reference/compose-file/services/ — Docker
  - model-knowledge
fetched: 2026-11-25
---

# Infrastructure

## Caddy (reverse proxy, HTTPS)
- One Caddyfile per environment (`deploy/Caddyfile`, `src/AspireBoot.AppHost/Caddyfile.dev`). Dev uses `tls internal` (local CA). Prod uses automatic Let's Encrypt (needs DNS A/AAAA + ports 80/443 + a persistent `caddy_data` volume).
- Routes: `handle /api/*` → `reverse_proxy api:8080` (service name and internal port, never localhost). Everything else → `root * /srv` + `try_files {path} /index.html` + `file_server` (SPA fallback, `precompressed`).
- Never proxy `/health`, `/alive`, Scalar or `/openapi` publicly. Admin API stays on localhost.
- Security headers on the site: `Strict-Transport-Security "max-age=31536000; includeSubDomains"`, `X-Content-Type-Options nosniff`, `X-Frame-Options DENY`, `Referrer-Policy strict-origin-when-cross-origin`, `Permissions-Policy`, a CSP matching the Angular build, and `-Server`.
- Caddy sets `X-Forwarded-*`. The API trusts only the Caddy network.
- Dev: export the local root (`/data/caddy/pki/authorities/local/root.crt`) and trust it once (documented script).

## Redis + HybridCache
- Redis is for trusted clients only: private network, never published ports in prod, ACL user with a password (`+@all -@dangerous -flushall -flushdb`), and the default user disabled.
- Persistence: AOF `everysec` (session tickets survive restarts).
- App usage: `HybridCache` (`AddHybridCache` + Redis as L2) with keys built from trusted IDs (`user:{id}:profile`), tags for invalidation, and explicit expirations. Session ticket store and rate-limit counters use the shared `IConnectionMultiplexer` (singleton, never per request).

## RustFS (S3-compatible storage)
- Image pinned (`rustfs/rustfs:1.0.6`), runs as UID 10001 (volumes must be writable by it; Podman uses `:Z,U`). Port 9000 = S3, 9001 = console (console never public in prod).
- `RUSTFS_ACCESS_KEY`/`RUSTFS_SECRET_KEY` come from `STORAGE_ACCESS_KEY`/`STORAGE_SECRET_KEY`. **Never** keep the default `rustfsadmin`.
- Health: `curl -fsS http://127.0.0.1:9000/health` (the image ships curl; bash and wget are absent). migrator, api and worker wait for `rustfs: service_healthy`.
- App client: `AWSSDK.S3` (`AmazonS3Client`) with `ServiceURL` from `Storage:ServiceUrl` (`http://rustfs:9000` in compose) and `ForcePathStyle = true`. Settings are `Storage:ServiceUrl`, `Storage:AccessKey`, `Storage:SecretKey` and `Storage:Bucket` (`StorageOptions`, validated on start).
- **The bucket is private.** The code sets no bucket policy and no ACL, and there is no presigned URL. Avatars are streamed by `GET /api/identity/account/avatar/{version}`, so RustFS never needs a public route.
- Object keys are server-generated: `avatars/{userId:N}/{objectId:N}.webp`, always written with `ContentType image/webp`. Client file names are never used as paths. Uploads are checked and re-encoded before they reach storage (see `docs/architecture.md`, "Avatars and private object storage").
- **Bucket provisioning** is a Wolverine system part, not a bare DI registration. `StorageBucketResource` (`IStatefulResource`: HeadBucket, then PutBucket, so it is idempotent) is reported by `StorageSystemPart` (`ISystemPart`). `resources setup` and `AddResourceSetupOnStartup()` only enumerate resources from system parts, so a bare `IStatefulResource` in DI is never provisioned.
  - Compose: the one-shot `migrator` runs `resources setup` before the API and the Worker start. Both hosts also call `AddResourceSetupOnStartup()` in every environment, so the bucket exists after a start in dev too.
  - Tests: `InfrastructureFixture` starts `rustfs/rustfs:1.0.6` (Testcontainers, port 9000, its own access keys), and the API and Worker factories receive the `Storage__*` settings.
- Avatar object writes and deletes are owned by the Worker (`identity-avatars` queue). Deleting a missing key is a success, so redelivery is safe.

## Image processing: SkiaSharp (native assets)
- Package pair (pinned in `Directory.Packages.props`, MIT): `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies`, both 4.157.0 (ADR 0004).
- The NoDependencies native build excludes fontconfig and needs only glibc (libc, libm, libdl, libpthread). `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian) already provides them, so the Dockerfiles have **no** apt step for SkiaSharp.
- Runtime limits in code: 8000 px per side, 24 MP, and a process-wide decode limiter (`Avatars:MaxConcurrentProcessing`). Adding a native dependency here needs a new ADR.

## Email: MailKit + Mailpit
- `IEmailSender` adapter in `Core/Infrastructure/Email` built on MailKit `SmtpClient`: `ConnectAsync(host, port, SecureSocketOptions.StartTls)` (or `SslOnConnect` on 465), then `AuthenticateAsync` and `SendAsync`. One client per send, or a pooled client with care (not thread-safe). Never return `true` from certificate validation.
- Emails are sent **only by the Worker** from outbox messages, with localized templates (pt-BR/en-US/es-ES), plain-text + HTML parts, and links pointing to the SPA.
- Dev and tests use Mailpit (SMTP 1025, UI/API 8025) with `SecureSocketOptions.None` **only** when `Email:UseTls=false` in Development/Testing. Production requires TLS.
- Tests read messages via the Mailpit REST API (`/api/v1/...`).

## Compose (deploy/compose.yaml) and containers
- Services: caddy, api, worker, migrator (one-shot, `restart: "no"`), postgres, rabbitmq, redis, rustfs. Private `backend` network. The migrator, api and worker all depend on `rustfs: service_healthy`, so the bucket is reachable before `resources setup` runs. Only Caddy publishes 80/443 (+443/udp).
- Every service has a pinned image, a `healthcheck`, `depends_on` with `condition: service_healthy`, `restart: unless-stopped`, `read_only: true` where possible, `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`, and a non-root `user`.
- Secrets via `.env` (never committed; `.env.example` documents every key) or compose secrets.
- Dockerfiles: multi-stage, `mcr.microsoft.com/dotnet/aspnet:10.0` (or chiseled) runtime, `USER app`, Wolverine `codegen write` in the build stage, and no SDK in the runtime image.
- Scripts detect the engine: `command -v docker || command -v podman`.
