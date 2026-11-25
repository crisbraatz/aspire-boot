---
name: opentelemetry
description: Load when adding logs, metrics, traces, or touching ServiceDefaults telemetry in API or Worker. Every new slice must follow this.
level: 2
kind: technology
version: OpenTelemetry .NET 1.21 / .NET 10
sources:
  - https://opentelemetry.io/docs/languages/dotnet/ — OpenTelemetry
  - https://opentelemetry.io/docs/languages/dotnet/sampling/ — OpenTelemetry
  - https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/docs/metrics — OpenTelemetry
  - https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/docs/logs — OpenTelemetry
  - https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/docs/trace — OpenTelemetry
  - https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs — Microsoft
  - https://learn.microsoft.com/en-us/dotnet/core/extensions/logger-message-generator — Microsoft
  - https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging — Microsoft
  - https://opentelemetry.io/docs/specs/semconv/general/naming/ — OpenTelemetry
  - https://opentelemetry.io/docs/specs/semconv/general/metrics/ — OpenTelemetry
  - model-knowledge
fetched: 2026-11-25
---

# Observability (logs, metrics, traces)

## Setup
- All wiring is in `ServiceDefaults` (`AddOpenTelemetry().ConfigureResource(...).WithLogging().WithMetrics().WithTracing()`), exported via OTLP. Resource attributes: `service.name`, `service.version`, `deployment.environment.name`.
- Instrumentation: ASP.NET Core, HttpClient, runtime, Npgsql (`AddNpgsql()`), Redis, Wolverine (`AddSource("Wolverine")`, `AddMeter("Wolverine*")`), and AspireBoot sources (`AddSource("AspireBoot.*")`, `AddMeter("AspireBoot.*")`).
- Exclude `/health` and `/alive` from traces.
- Sampling: Development AlwaysOn. Production `parentbased_traceidratio` via `OTEL_TRACES_SAMPLER` / `OTEL_TRACES_SAMPLER_ARG` (default 1.0 for small apps; lower when volume grows).

## Naming
- One `ActivitySource` and one `Meter` per bounded context, held as static readonly fields in `Core/<Context>/<Context>Telemetry.cs` (for example `AspireBoot.Identity`). Never create them per call.
- Attributes and metrics: lowercase dot-namespaced snake_case, reusing semconv names first. Custom ones are prefixed `aspireboot.` (for example `aspireboot.user.role`).
- Metric names are not pluralized and have no `_total`. Units in UCUM (`s`, `By`, `{request}`). Durations as histograms in seconds.

## Per slice (mandatory)
- HTTP and messaging spans come from instrumentation. **Enrich the current Activity** with business attributes (low cardinality) rather than creating new spans.
- Create a child Activity only around meaningful units (external SMTP call, storage upload, heavy query). Do not wrap every method.
- Business metrics for key events: `aspireboot.identity.registrations` (Counter), `aspireboot.identity.sign_ins` (Counter with `outcome`=success|failure|locked), `aspireboot.email.sent` (Counter with `outcome`), `aspireboot.email.delivery.duration` (Histogram).
- Errors: `activity?.SetStatus(ActivityStatusCode.Error)` + a structured error log with the exception parameter.
- Always use `?.` on Activity (it is null when unsampled). Guard expensive tag building with `IsAllDataRequested`.

## Logging
- `[LoggerMessage]` source-generated, `static partial` methods in a `<Slice>Log` or `<Context>Log` class. Templates with PascalCase placeholders. **Never string interpolation.**
- Pass exceptions in the exception parameter, never `ex.Message` in the template.
- Levels: Information for business events, Warning for expected-but-notable (lockout, validation spike), Error for failures needing attention. Debug is off in production.

## Cardinality and privacy
- **Never** put emails, names, user IDs, tokens, raw URLs/paths or trace IDs in metric attributes. Default limit is 2000 series per metric.
- No PII or secrets in logs or span attributes. Use user ID only in logs/traces when needed (opaque GUID), never email or password. Use `Microsoft.Extensions.Compliance.Redaction` classification attributes for sensitive LoggerMessage parameters.
- Wolverine `RecordExceptionsOnSpans=false` if messages may carry sensitive data.

## Cross-process
- API → RabbitMQ → Worker traces must form one trace. Wolverine propagates context. Verify in tests or the dashboard after adding a new message.

## Pitfalls
- Forgetting `AddSource`/`AddMeter`, so telemetry silently disappears.
- High-cardinality tags.
- Disposing providers early, which loses buffered telemetry.
