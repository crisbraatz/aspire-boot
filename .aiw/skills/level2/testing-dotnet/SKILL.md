---
name: testing-dotnet
description: Load when writing or running backend tests (unit or integration) in tests/AspireBoot.Tests.
level: 2
kind: technology
version: xUnit v3 4.0 + Testcontainers 4.17 + Respawn 7 on .NET 10 (Microsoft.Testing.Platform)
sources:
  - https://xunit.net/docs/shared-context — xUnit
  - https://dotnet.testcontainers.org/ — Testcontainers
  - https://dotnet.testcontainers.org/api/best_practices/ — Testcontainers
  - https://dotnet.testcontainers.org/custom_configuration/ — Testcontainers
  - https://dotnet.testcontainers.org/api/resource_reaper/ — Testcontainers
  - https://dotnet.testcontainers.org/modules/postgres/ — Testcontainers
  - https://dotnet.testcontainers.org/test_frameworks/xunit_net/ — Testcontainers
  - https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests — Microsoft
  - https://github.com/jbogard/Respawn — Jimmy Bogard
  - model-knowledge
fetched: 2026-11-25
---

# Backend testing

## Project layout
- Single project `tests/AspireBoot.Tests` (`Sdk="Microsoft.NET.Sdk.Web"`), with `Unit/` and `Integration/` mirroring `src` slice paths. Folders and namespaces are `AspireBoot.Tests.Unit.Identity.Domain`, etc.
- Run on Microsoft.Testing.Platform: `global.json` has `"test": { "runner": "Microsoft.Testing.Platform" }`. Reference `xunit.v3` only (no `Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`).
- Filter: `dotnet test -- --filter-trait "Category=Unit"` (traits `Category=Unit|Integration` set via assembly/class attributes).
- Test names: `Method_Scenario_ExpectedResult` or a sentence (`Register_WithExistingEmail_ReturnsGenericAccepted`). Use Arrange/Act/Assert. One behavior per test.

## Unit tests
- Domain aggregates and value objects: invariants, state transitions, raised domain events. No DB and no mocks for domain.
- NSubstitute only for true external boundaries (clock via `TimeProvider`, `FakeTimeProvider`).

## Integration tests
- **Real infrastructure via Testcontainers**: PostgreSQL, RabbitMQ, Redis, RustFS (generic container), Mailpit (generic container). Started **once per assembly** in an `[assembly: AssemblyFixture(typeof(InfrastructureFixture))]` implementing `IAsyncLifetime`. Pin image tags to the same versions as deploy.
- `AspireBootApiFactory : WebApplicationFactory<Program>` overrides configuration with container connection strings (`ConfigureAppConfiguration`) and `UseEnvironment("Testing")`. Similar host for the Worker (or Wolverine tracked sessions spanning both).
- Migrations applied once in the fixture. **Respawn** (`DbAdapter.Postgres`, ignore `__EFMigrationsHistory` and Wolverine tables or clear Wolverine storage separately) resets data **before each test**.
- HTTP tests use `CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new("https://localhost") })` and a helper to obtain antiforgery tokens and authenticate (cookie container).
- Email assertions: query the Mailpit API (`/api/v1/messages`, `/api/v1/search?query=to:...`) and extract links from the message.
- Message assertions: Wolverine `TrackActivity().IncludeExternalTransports()`.
- Pass `TestContext.Current.CancellationToken` to async calls.
- Parallelism: assembly fixture is shared, so tests that reset the DB must run in one collection (`[Collection("Integration")]`) or disable parallelization for integration classes.

## Podman
- Testcontainers talks to the Docker API. With Podman set `DOCKER_HOST=unix://<podman socket>` (from `podman machine inspect --format '{{.ConnectionInfo.PodmanSocket.Path}}'`) and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/podman/podman.sock` for Ryuk. The `scripts/test.sh` helper detects docker, else podman, and exports these.
- Never disable Ryuk locally.

## Rules
- No fixed host ports, static container names or `localhost` hard-coding. Use `GetConnectionString()`/`GetMappedPublicPort()` after `StartAsync()`.
- No InMemory EF provider. No sleeps; poll with timeout helpers.
- Coverage gate in CI (target 90% lines for Api/Worker/Core).
- TDD: write the failing test in the slice's mirror folder first.
