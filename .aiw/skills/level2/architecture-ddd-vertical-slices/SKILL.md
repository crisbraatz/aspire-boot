---
name: architecture-ddd-vertical-slices
description: Load before creating or moving ANY file, adding a feature, or deciding where code lives (backend or frontend). Defines DDD + Vertical Slices rules for AspireBoot.
level: 2
kind: architecture
version: DDD (Evans/Fowler/DDD Crew) + Vertical Slice Architecture (Bogard 2018), adapted 2026-11
sources:
  - https://martinfowler.com/bliki/DDD_Aggregate.html — Martin Fowler
  - https://martinfowler.com/bliki/BoundedContext.html — Martin Fowler
  - https://martinfowler.com/bliki/ValueObject.html — Martin Fowler
  - https://martinfowler.com/bliki/AnemicDomainModel.html — Martin Fowler
  - https://github.com/ddd-crew/aggregate-design-canvas — DDD Crew
  - https://github.com/ddd-crew/bounded-context-canvas — DDD Crew
  - https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice — Microsoft
  - https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/implement-value-objects — Microsoft
  - https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation — Microsoft
  - https://www.jimmybogard.com/vertical-slice-architecture/ — Jimmy Bogard
  - model-knowledge
fetched: 2026-11-25
---

# DDD + Vertical Slices for AspireBoot

## The one-paragraph rule
Organize code by **bounded context → use case (slice)**, never by technical type. **Business rules and invariants live in the domain (aggregates, value objects) in `AspireBoot.Core`**. Slices are thin orchestration: they validate input, load the aggregate, call a domain method, persist and return. Minimize coupling between slices and maximize cohesion inside a slice.

## Projects and dependency rule (what may reference what)
| Project | Contains | May reference |
| :--- | :--- | :--- |
| `AspireBoot.Core` | Domain (aggregates, VOs, domain events, errors), persistence (`AppDbContext`, configurations, migrations), message contracts, infrastructure adapters (email, storage, cache), use-case logic needed by BOTH Api and Worker | BCL, EF Core, Identity stores, Wolverine abstractions, SDK clients. NEVER Api/Worker. |
| `AspireBoot.Api` | HTTP slices (endpoint + request/response + validation + handler), auth/Identity wiring, Problem Details, rate limits, OpenAPI | Core, ServiceDefaults |
| `AspireBoot.Worker` | Message handlers and scheduled jobs, one slice per message | Core, ServiceDefaults |
| `AspireBoot.ServiceDefaults` | OTel, health, resilience, service discovery ONLY | framework packages |
| `AspireBoot.AppHost` | Resource graph only, no business code | all projects (as Aspire references) |
| `AspireBoot.Tests` | `Unit/` and `Integration/` mirroring src paths | everything |

Inside Core, `*/Domain/` has **no dependency on EF Core, ASP.NET, Wolverine or any infrastructure** (persistence ignorance). Mapping lives in `Persistence/Configurations`.

## Backend folder map
```
Core/Identity/Domain/User.cs                    aggregate root
Core/Identity/Domain/EmailAddress.cs            value object
Core/Identity/Domain/Events/UserRegistered.cs   domain event (past tense)
Core/Persistence/Configurations/UserConfiguration.cs
Core/Messaging/Identity/SendEmailMessage.cs     integration message contract
Api/Identity/Users/ListUsers/ListUsersEndpoint.cs | ListUsersRequest.cs | ListUsersResponse.cs
Worker/Identity/SendEmail/SendEmailHandler.cs
Tests/Integration/Identity/Users/ListUsers/ListUsersTests.cs
Tests/Unit/Identity/Domain/UserTests.cs
```
A slice folder is `<Verb><Noun>`. Each file in it carries the slice prefix. One public type per file.

## DDD tactical rules
- **Aggregate** = consistency boundary loaded and saved as a whole. External code references only the root, by ID. Keep aggregates small. Changes go through intention-revealing methods (`user.ChangeEmail(...)`), never public setters.
- **Invariants are enforced inside the aggregate** and checked by unit tests. No `if (user.Role == ...)` business logic in endpoints or handlers.
- **No anemic models:** an entity with only getters/setters plus a "service" holding the logic is an anti-pattern.
- **Value objects:** immutable, equality by value, self-validating factory (`EmailAddress.Create(string)` returns a result). Replace primitives that carry rules (email, display name, role name). Map them as EF complex types (prefer) or owned types.
- **Domain events:** past-tense, immutable records raised by the aggregate into an in-memory list and dispatched at SaveChanges. Side effects inside the same context run in the same transaction. Cross-process effects (email, audit) become **integration messages via the Wolverine outbox**, published only after a successful commit.
- **One aggregate per transaction** by default. Rules across aggregates are eventually consistent (messages). Exception: side effects of the same command may share the transaction (document the reason).
- **Expected failures** return a `Result`/error type mapped to Problem Details. Exceptions only for bugs and infrastructure faults.
- **Ubiquitous language:** type and method names use the business terms from `.aiw/skills/level3/domain`.

## Vertical slice rules
- **Each request/message is a use case.** New features add new slice folders. They rarely modify other slices.
- **A slice never calls another slice.** For shared behavior, push it into the domain (rules) or `<Context>/Shared/` (technical helpers used by 3+ slices). Do not create generic `Services/`, `Helpers/`, `Utils/`, `Managers/` or repository-per-entity layers.
- **No repository abstraction over EF Core.** Slices use `AppDbContext` directly. Reads project with `Select` + `AsNoTracking`. Writes load the aggregate, call a domain method and `SaveChangesAsync`.
- **CQRS falls out naturally:** query slices never mutate, and command slices return minimal data (ID or 204).
- **Start simple, refactor toward the domain** when logic grows. But in AspireBoot, any rule about valid state goes in the domain from day one.

## Frontend (Angular) mapping, same rules
```
src/app/identity/users/list-users/list-users.page.ts (+ .html, .spec.ts)
src/app/identity/users/get-user/get-user.page.ts
src/app/identity/shared/users-api.ts             context-level API client + models
src/app/core/                                    app-wide singletons (auth state, i18n, http interceptors, layout)
src/app/shared/ui/                               dumb, reusable, accessible components (no API calls)
```
- A slice folder holds its page/component, local state and tests. Slices import from `<context>/shared`, `core` and `shared/ui`, never from another slice.
- Domain-ish client rules (permission checks for UI, display formatting) live in `<context>/shared`. **The server is always the authority for authorization.**

## Testing per layer
- Domain → `Tests/Unit` (pure, fast, no mocks needed).
- Slices (endpoint and handler) → `Tests/Integration` through HTTP or Wolverine tracked sessions against real containers.
- Frontend slice → `*.spec.ts` beside the component.

## Worked example: "Admin changes a user's role"
1. `Core/Identity/Domain/User.cs`: `public Result AssignRole(Role role, UserId actor)` enforces "cannot remove your own Admin role" and raises `UserRoleChanged`.
2. `Api/Identity/Users/AssignRole/AssignRoleEndpoint.cs`: `PUT /api/users/{id}/role`, `RequireAuthorization(Policies.ManageUsers)`, validates the body, loads the user, calls `AssignRole`, saves and returns 204 or Problem Details.
3. `UserRoleChanged` is published as `AuditUserMessage` via the outbox, and `Worker/Identity/AuditUser/AuditUserHandler.cs` records it.
4. Tests: a unit test for the invariant, an integration test for 204/403/404/validation, and one for the audit message.

## Anti-patterns (reject in review)
- Business rules in endpoints, handlers, Angular components or EF configurations.
- Cross-slice calls, or "shared" god services.
- Generic repositories/unit-of-work wrappers over EF Core.
- Domain types referencing EF/ASP.NET/Wolverine.
- Folders by type (`Controllers/`, `Services/`, `DTOs/`, `components/`).
- Publishing broker messages directly (bypassing the outbox).
- Code placed "wherever it compiles". When unsure, follow this map exactly or ask.

## Where sources disagree
Bogard rejects shared layers and lets each slice choose. Evans/Fowler/Microsoft keep invariants in a shared domain model. **AspireBoot follows DDD for state and rules, and VSA for orchestration and organization.**
