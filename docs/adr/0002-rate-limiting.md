# ADR 0002: Rate limiting as an architecture rule

- Status: accepted
- Date: 2026-11-25
- Feature: 001 (passwordless account authentication with passkeys)

## Context
Passwordless sign-in still exposes endpoints that cost money or can be abused: sign-up creates accounts and sends email, resend and recovery send email, and passkey ceremonies consume CPU. Without limits, one client can flood the mail provider, enumerate accounts through timing, or exhaust the database. Account lockout is not an option here because there is no password to lock. Rate limiting therefore becomes a rule of the architecture, not a per-feature decision.

## Decision
Every endpoint under `/api` declares a named rate-limit policy. The policies are constants in `RateLimitPolicies` (`src/AspireBoot.Api/Shared/RateLimiting.cs`):

| Policy | Used by | Default limit |
| :--- | :--- | :--- |
| `signup` | Sign-up | 5 requests per 600 s |
| `email-link` | Resend confirmation, request recovery | 5 requests per 600 s |
| `passkey-ceremony` | Passkey registration and sign-in options and completion | 20 requests per 60 s |
| `authenticated` | Signed-in endpoints (account, passkey management, sign-out) | 60 requests per 60 s |
| `public` | Anonymous read-only endpoints (platform info, antiforgery token) | 120 requests per 60 s |

Rules:
- **Partitioning:** an authenticated caller is limited per user (`user:{NameIdentifier}`). An anonymous caller is limited per client IP (`ip:{address}`). A request from a signed-in user is not counted against the IP partition.
- **Trusted proxy only:** the client IP comes from `X-Forwarded-For` only when the peer belongs to `ReverseProxy:KnownNetworks` (in production, the Caddy network `172.30.0.0/24`), with `ForwardLimit = 1`. Other peers cannot change their partition by sending a forged header. A unit test checks that a spoofed `X-Forwarded-For` does not change the partition key.
- **Response:** a rejected request returns `429` with a Problem Details body (`type` and `code` `rateLimited`) and a `Retry-After` header, in whole seconds, at least 1. The SPA maps the code to a translated message and shows the wait time.
- **Configuration:** each policy reads `RateLimiting:<policy>:PermitLimit` and `RateLimiting:<policy>:WindowSeconds`. Defaults are in code. Tests use high limits in the `Testing` environment, and only the dedicated 429 test lowers them.
- **Enforcement:** `RateLimitCoverageTests` enumerates the real endpoint table and fails when an `/api` endpoint has no `EnableRateLimitingAttribute` metadata. The same test checks that every unsafe endpoint (POST, PUT, PATCH, DELETE) rejects a request without the antiforgery token.
- **Metrics:** `aspireboot.identity.rate_limited` counts rejections, tagged only with the policy name.

### Store: in memory, for a single VPS
The limiter uses ASP.NET Core's built-in `System.Threading.RateLimiting` with the in-memory fixed-window limiter. No new package is added.

This is correct for the default deployment (one API container on one VPS). The trade-offs are accepted:
- Counters reset when the API restarts.
- Each API instance keeps its own counters. With N instances the effective limit is up to N times the configured value.

### When to move to Redis
Move the partitions to a shared store when any of these becomes true:
1. More than one API instance runs behind the proxy (for example a scaled compose service or a cluster).
2. Limits must survive restarts, for example for an abuse-response policy.

The change is local to `RateLimitingExtensions`. Redis is already part of the stack (`AddRedisDistributedCache`). The partition keys and policy names stay the same, so endpoints and tests do not change.

## Consequences
- Adding an endpoint without a policy fails the coverage test, so the rule is enforced by the build pipeline, not by review alone.
- The IP partition depends on the proxy configuration. A deployment that puts the API behind another proxy must update `ReverseProxy:KnownNetworks`, or every client shares the proxy's IP.
- Shared IP addresses (NAT, mobile carriers) can hit anonymous limits together. The limits are set per policy so that a normal user is not affected.
- Limits are a defense against abuse, not a substitute for the antiforgery token, the email cooldown or the uniform responses described in ADR 0003.
