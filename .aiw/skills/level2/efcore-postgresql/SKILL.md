---
name: efcore-postgresql
description: Load when touching AppDbContext, entity configurations, queries, migrations or PostgreSQL in AspireBoot.
level: 2
kind: technology
version: EF Core 10 + Npgsql 10 + PostgreSQL 19
sources:
  - https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/ — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/modeling/indexes — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/saving/concurrency — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/modeling/complex-types — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries — Microsoft
  - https://learn.microsoft.com/en-us/ef/core/testing/ — Microsoft
  - https://www.npgsql.org/efcore/ — Npgsql
  - https://aspire.dev/integrations/databases/efcore/postgres/postgresql-connect/ — Microsoft
  - model-knowledge
fetched: 2026-11-25
---

# EF Core 10 + PostgreSQL

## Model
- One `AppDbContext` (sealed) in `Core/Persistence`, implementing `IDataProtectionKeyContext` and the Identity stores. It also maps Wolverine envelope storage.
- One `IEntityTypeConfiguration<T>` per aggregate in `Core/Persistence/Configurations`, applied with `ApplyConfigurationsFromAssembly`. **No data annotations on domain types.**
- Value objects map as **complex types** (`ComplexProperty`) by default. Use owned types only when the VO needs navigations. Use strongly typed IDs via value converters.
- Map private fields/backing collections so aggregates keep encapsulation (`Navigation(...).UsePropertyAccessMode(PropertyAccessMode.Field)`).
- Explicit max lengths, required flags and snake_case naming (EFCore.NamingConventions or explicit configuration). Store timestamps as `timestamptz` (UTC `DateTimeOffset`).
- Indexes: declare every query-driven index in the configuration (unique normalized email, filters and sort columns used by lists). Remove indexes nothing uses.
- Concurrency: optimistic concurrency token on aggregates edited concurrently (Npgsql `xmin` system column as a row version; verify the mapping API against Npgsql docs). Handle `DbUpdateConcurrencyException` → 409 Problem Details.

## Querying
- Reads: `AsNoTracking()` + `Select` projection into the response record. Never load full entities to return DTOs.
- Writes: load the aggregate (tracked), call the domain method, then `SaveChangesAsync(ct)`.
- No lazy loading (N+1). Use `Include` only for aggregate loading. Use `AsSplitQuery()` when including two or more collections.
- Pagination: `OrderBy` on a unique key (add the PK as a tiebreaker) + `Take`, and keyset for deep lists. Never `ToList()` and then filter in memory.
- Bulk changes: `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (these bypass the change tracker and domain events, so use them only for maintenance jobs).
- Parameterized queries only. Never concatenate SQL. `FromSql` with interpolation (parameterized) as a last resort.
- `AddDbContextPool` where Wolverine integration allows. Avoid per-request state in `OnConfiguring`.

## Migrations
- Location: `Core/Persistence/Migrations`. Commands (from repo root):
  - `dotnet ef migrations add <Name> -p src/AspireBoot.Core -s src/AspireBoot.Api`
  - `dotnet ef migrations remove -p ... -s ...`
  - `dotnet ef migrations has-pending-model-changes -p ... -s ...` (CI gate)
  - `dotnet ef migrations bundle --self-contained -r linux-x64 -o artifacts/efbundle`
  - `dotnet ef migrations script --idempotent -o artifacts/migrations.sql` (review)
- Always read generated migrations. A rename can become drop+add (data loss), so use `RenameColumn`. Column replacement: add nullable → backfill → make required → drop the old column.
- Never edit or delete an applied migration. Add a corrective one. Never hand-edit the ModelSnapshot.
- Never use DbContext or entity types inside migrations. Use `InsertData`/`Sql`.
- **Production:** run the migration bundle as a one-shot compose job with a schema-owner credential before starting the app. The app's DB user has only DML rights. No `MigrateAsync()` on startup in production, and never `EnsureCreated`.
- Dev: the Aspire migration resource / `MigrateAsync` on startup is allowed only in Development.

## Testing
- Integration tests use real PostgreSQL (Testcontainers), apply migrations once and reset with Respawn. Never use the InMemory provider.
- A test asserts `HasPendingModelChanges()` is false.

## Pitfalls
- Forgetting `ct` on async calls, or running parallel operations on one DbContext.
- Cartesian explosion from multiple collection Includes.
- Unbounded queries in list endpoints.
- `ExecuteUpdate` inside a Wolverine handler without an Eager transaction escapes the outbox.
