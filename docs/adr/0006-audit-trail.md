# ADR 0006: Audit trail written by a SaveChanges interceptor

- Status: accepted
- Date: 2026-11-25
- Feature: 003 (user administration, immediate revocation and audit trail)
- Related: ADR 0005 (session revocation), ADR 0003 (passkeys and personal data)

## Context
Administrators change roles, and account owners add, rename and remove passkeys. The product needs a record of who changed what and when, and the record must never be missing for a committed change or present for a rolled-back one. Writing audit rows by hand in each slice is easy to forget, and an audit written in a separate transaction can disagree with the data. The record also must not leak secrets such as security stamps, password hashes or provider keys.

## Decision
### What is recorded
- Table `audit_entries` (entity `AuditEntry`, `Core/Persistence/Auditing`): `id` (Guid v7), `occurred_at`, `actor_user_id` (nullable), `actor_type` (`user` or `system`), `table_name`, `entity_type`, `entity_key` (text; composite keys as JSON), `action` (`insert`, `update` or `delete`), `changes` (jsonb, `{ "field": { "old": ..., "new": ... } }`), and `trace_id` from the current activity. Indexes on `(actor_user_id, occurred_at)` and `(entity_type, entity_key, occurred_at)` serve both "what did this user do" and "what happened to this entity".
- On update only modified properties are recorded. On insert and delete, all non-masked properties are recorded.

### How it is written
- `AuditSaveChangesInterceptor` (`SaveChangesInterceptor`, registered once in `AddCoreInfrastructure` through `ConfigureDbContext`) runs in `SavingChanges` and `SavingChangesAsync`. It calls `DetectChanges`, snapshots the added, modified and deleted entries that are audited, and adds the `AuditEntry` rows to the same context.
- The rows therefore go into the same SaveChanges batch and the same transaction as the change. A rollback removes both. Wolverine's `SaveChangesAndFlushMessagesAsync` calls `SaveChangesAsync`, so outbox flushes are covered too.
- The interceptor never calls SaveChanges itself and ignores `AuditEntry`, so it cannot recurse. Writing in `SavedChanges` was rejected, because it would need a second save inside or after the committed transaction.
- Store-generated keys are not allowed on audited types. Every audited entity uses a client-generated key (Guid v7 or a composite natural key), so the key is known before save. `AuditEntryFactory` throws `InvalidOperationException` when it meets a temporary key. An architecture test fails when an audited type has a store-generated key. An entity type without a primary key is skipped, not thrown on.

### Owned types
- Owned and complex types are classified explicitly in `AuditPolicy.OwnedTypes`. An architecture test fails when a new owned or complex type is not classified.
- `IdentityPasskeyData` (passkey data, a JSON owned entity) is an **excluded owned type**. Its values are never recorded. The owner row records only `{"data":{"changed":true}}` when it changes.
- Included owned types are folded into the owner's row, so the owner's audit row shows their values.

### Excluded entities and masking
- Excluded entity types: `AuditEntry` (no recursion), `DataProtectionKey` (key material), `IdentityUserClaim` and `IdentityRoleClaim`. The claim tables are not used (roles are the RBAC model) and have store-generated integer keys. If claims are adopted later, they need client-generated keys and an entry here.
- Masked properties on every type, by name: `SecurityStamp`, `ConcurrencyStamp`, `PasswordHash`, `xmin`, `NormalizedEmail`, `NormalizedUserName`.
- Masked properties on one type: `IdentityUserToken.Value` and `IdentityUserLogin.ProviderKey`.
- A masked property records that it changed, with the value `"***"`, and never its value.

### Actor
- `IAuditActor` (`Guid? UserId`) is a **singleton**. Aspire pools the `AppDbContext` options, which are built once from the root provider, so the interceptor and its actor must be singletons that read ambient state on each call.
- The API implements `HttpAuditActor`, which reads the user id claim (`IdentityOptions.ClaimsIdentity.UserIdClaimType`) from `IHttpContextAccessor` on each access. Anonymous self-service flows (complete registration and recovery) call `IAuditActorOverride.ActAs(userId)`, an `AsyncLocal` scope inside the same singleton, so their rows (passkey, email confirmation, bootstrap role grant) name the owner of the link as the actor instead of `system`. The scope ends when disposed and never leaks to other requests.
- When SaveChanges fails, the interceptor detaches the `AuditEntry` rows it added (`SaveChangesFailed`), so a retry on the same context does not duplicate them. The Worker and design-time tooling use `SystemAuditActor`, and a null user id means `system`.
- Registration order matters: `AddCoreInfrastructure` (which adds the interceptor through `ConfigureDbContext`) must run before `AddNpgsqlDbContext`. A concurrency test (`AuditActorConcurrencyTests`) proves that parallel requests each record their own actor.

### Append-only storage
- Migration `AddAuditEntries` creates a function `audit_entries_append_only()` that raises `audit_entries is append-only`, a `BEFORE UPDATE OR DELETE ... FOR EACH ROW` trigger, and a `BEFORE TRUNCATE ... FOR EACH STATEMENT` trigger. The migration's `Down` drops them.
- The only bypass is a superuser session with `SET session_replication_role = replica`. The integration test fixture's Respawn reset uses it so that tests can clear data. The application never uses it.

## Sources
- Microsoft Learn, EF Core interceptors (`SaveChangesInterceptor`, `SavingChangesAsync`, audit example): https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors
- Microsoft Learn, EF Core transactions (one transaction per SaveChanges): https://learn.microsoft.com/en-us/ef/core/saving/transactions
- Microsoft Learn, `ConfigureDbContext`: https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/
- Npgsql, jsonb mapping: https://www.npgsql.org/efcore/mapping/json.html
- PostgreSQL, `CREATE TRIGGER` and `session_replication_role`: https://www.postgresql.org/docs/current/sql-createtrigger.html and https://www.postgresql.org/docs/current/runtime-config-client.html
- Wolverine, EF Core outbox (`SaveChangesAndFlushMessagesAsync`): https://wolverinefx.net/guide/durability/efcore/outbox-and-inbox

## Consequences
- Every committed change to an audited entity has exactly one audit row per changed entity in the same transaction, and a rolled-back change has none. The integration tests cover both cases.
- Every SaveChanges adds rows. Sign-in counter updates and passkey sign-ins are audited, so the table grows with activity. This is accepted for the template's scale.
- The table cannot be edited through the application or by an ordinary database role. Corrections are made by adding new data, not by changing history.
- Adding an entity or an owned type forces a decision: client-generated key, and an `AuditPolicy` classification. Architecture tests enforce this. `AGENTS.md` states the rule.

## Residual risks (accepted)
1. **Personal data in history.** `changes` stores emails and display names, because the history needs them to be useful. Erasure on request (LGPD/GDPR) is not implemented. It is a future concern, tracked in the feature report.
2. **No retention.** Rows are kept forever. No pruning or archiving exists yet.
3. **Superuser bypass.** A database superuser can disable the triggers with `session_replication_role = replica`, or drop them. Triggers protect against the application and ordinary roles only. Protect the owner account (`deploy/.env.example` separates the owner from the application account).
4. **Only tracked changes are audited.** Changes made with `ExecuteUpdate`, `ExecuteDelete` or raw SQL do not pass through the change tracker, so they are not recorded. Slices that need them must write the audit through a tracked change or document the gap in their ADR. The rule is in `AGENTS.md`.
5. **Manual audit writes are forbidden.** Nothing else writes `audit_entries`, so the record depends on the interceptor being registered. The architecture and integration tests guard that registration.
