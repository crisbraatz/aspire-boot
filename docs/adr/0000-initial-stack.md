# ADR 0000: Initial stack and architecture

- Status: accepted
- Context: an open-source template so that technical and non-technical builders can start secure web products with authentication, RBAC and production-grade infrastructure ready, cheap to host on one VPS, and easy for AI assistants to extend without architectural drift.
- Decision: .NET 10 LTS with Aspire. The solution has AppHost, ServiceDefaults, Api, Worker, Core and a single Tests project. The infrastructure is PostgreSQL + EF Core migrations, Wolverine (outbox/inbox) over RabbitMQ, Redis, RustFS, MailKit with Mailpit for dev/tests, Caddy, ASP.NET Core Identity, Scalar and OpenTelemetry. The frontend is Angular 22 + Tailwind 4. The architecture is DDD + Vertical Slices in both backend and frontend.
- Consequences: a solid, secure base with more moving parts than a minimal template. Aspire hides that complexity locally, and production needs a documented compose deployment. Strict slice and dependency rules plus Level 2/3 AI skills are required to keep contributions consistent.
