# AspireBoot

**Start your product secure, focus on your business.**

AspireBoot is an open-source .NET Aspire template for builders, from experienced developers to people who have never programmed and work with AI assistants. Clone it and you get a production-grade foundation: accounts with email confirmation, sessions, user management, role-based access control, background jobs, file storage, email, HTTPS and full observability. Your time goes into what makes your idea unique.

> Status: in development. The infrastructure, architecture and AI guidance are in place. Passwordless accounts with passkeys are being built on top of them.

## What's inside

| Concern | Technology |
| :--- | :--- |
| Orchestration (local) | .NET Aspire 17 |
| API and Worker | .NET 10 LTS, ASP.NET Core Minimal APIs, Scalar API docs |
| Accounts and RBAC | ASP.NET Core Identity 10, passwordless (passkeys only), secure cookies, antiforgery, rate limiting |
| Database | PostgreSQL 19 + EF Core 10 migrations |
| Messaging | RabbitMQ 4 + Wolverine (transactional outbox and durable inbox) |
| Cache | Redis 8 + HybridCache |
| File storage | RustFS (S3-compatible, private bucket) |
| Profile avatars | SkiaSharp (MIT): every upload is re-encoded to a 256x256 WebP without metadata |
| Email | MailKit, with Mailpit capturing every email locally |
| HTTPS / reverse proxy | Caddy (automatic Let's Encrypt in production) |
| Observability | OpenTelemetry logs, metrics and traces across API → queue → Worker |
| Frontend | Angular 22 + Tailwind CSS 4, responsive and accessible (WCAG 2.2 AA), runtime language switch in pt-BR / en-US / es-ES |
| Tests | xUnit v3 + Testcontainers (real infrastructure), Vitest |

**Architecture:** Domain-Driven Design + Vertical Slices, in both backend and frontend. See [docs/architecture.md](docs/architecture.md) and the decision records in [docs/adr/](docs/adr/README.md) (passwordless passkeys, rate limiting, runtime i18n).

**Passwordless accounts:** users sign up with an email, confirm it and register a passkey. There are no passwords anywhere in the product. Recovery is also by email link.

**Built for AI-assisted development:** [AGENTS.md](AGENTS.md) and the skills in `.aiw/skills/` teach any assistant (Claude, Codex, Gemini, Copilot) where code belongs and how to keep it secure, tested and fast.

## Prerequisites

- [.NET SDK 10.0.404+](https://dotnet.microsoft.com/download) (see `global.json`)
- [Node.js 26 LTS](https://nodejs.org/) (see `.nvmrc`)
- [Docker](https://docs.docker.com/get-docker/) **or** [Podman](https://podman.io/) running
- Optional: the [Aspire CLI](https://aspire.dev) (`aspire run`)

With Podman, tell Aspire to use it:

```bash
export ASPIRE_CONTAINER_RUNTIME=podman
```

## Run locally

```bash
dotnet tool restore
npm --prefix src/AspireBoot.Web ci
dotnet run --project src/AspireBoot.AppHost
```

- App (SPA + API on one HTTPS origin): https://localhost:8443
- Aspire dashboard (logs, traces, metrics): the URL printed in the terminal
- Mailpit (captured emails), RabbitMQ management, RustFS console: links in the dashboard
- API reference (Scalar, Development only): `/scalar` on the API resource

Trust the local HTTPS certificate once (after the first run):

```bash
./scripts/dev-trust-cert.sh
```

Secrets for local containers are Aspire parameters. Set stable values once, so data volumes keep working:

```bash
dotnet user-secrets --project src/AspireBoot.AppHost set "Parameters:postgres-password" "<strong-password>"
dotnet user-secrets --project src/AspireBoot.AppHost set "Parameters:rabbitmq-password" "<strong-password>"
dotnet user-secrets --project src/AspireBoot.AppHost set "Parameters:storage-secret-key" "<strong-password>"
```

## Test

```bash
./scripts/test.sh
```

This runs unit tests, integration tests against real PostgreSQL, RabbitMQ, Redis and Mailpit containers, and the Angular tests. Pass xUnit filter options to run a subset of the backend tests, for example `./scripts/test.sh --filter-namespace 'AspireBoot.Tests.Unit*'`. Full quality gate (format, lint, build, migrations check, tests):

```bash
./scripts/verify.sh
```

## Deploy to a VPS

1. Point your domain's DNS (A/AAAA) to the server and open ports 80 and 443.
2. Install Docker (or Podman with `podman compose`) and clone the repository.
3. Configure the environment:
   ```bash
   cp deploy/.env.example deploy/.env   # fill every value with strong secrets
   ```
4. Start:
   ```bash
   docker compose -f deploy/compose.yaml --env-file deploy/.env up -d --build
   ```
   - The one-shot `migrator` service applies database migrations and messaging setup before the API and Worker start.
   - Caddy obtains HTTPS certificates automatically.
   - Only ports 80/443 are public. Databases, queues, cache and storage stay on a private network.
5. Back up the `postgres_data` and `rustfs_data` volumes regularly (for example a nightly `pg_dump` plus a volume snapshot).

On redeploys, always rebuild (`--build`) so migrations come from the new code.

## Project structure

```
src/
  AspireBoot.AppHost/          Local orchestration (Aspire)
  AspireBoot.ServiceDefaults/  OpenTelemetry, health checks, resilience
  AspireBoot.Core/             Domain, persistence, messaging contracts, adapters
  AspireBoot.Api/              HTTP slices, Identity, security
  AspireBoot.Worker/           Message handlers and scheduled jobs
  AspireBoot.Web/              Angular app
tests/AspireBoot.Tests/        Unit/ and Integration/
deploy/                        compose.yaml, Caddyfile, .env.example
docs/                          Business overview, architecture, ADRs
```

## License

MIT
