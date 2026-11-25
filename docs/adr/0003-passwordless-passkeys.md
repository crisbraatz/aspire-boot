# ADR 0003: Passwordless accounts with passkeys on ASP.NET Core Identity

- Status: accepted
- Date: 2026-11-25
- Feature: 001 (passwordless account authentication with passkeys)

## Context
The template gives every product a secure account system. Passwords are the weakest part of that system: they are reused, phished and leaked, and they bring reset flows, lockout rules and password policies that every builder must get right. The product owner chose passwordless by design from the start. Accounts use passkeys (WebAuthn / FIDO2) only, and no password is accepted or stored at any point.

## Decision
### Identity with passkeys only
- ASP.NET Core Identity 10 (10.0.15 at the time of writing) with the built-in passkey support. No third-party WebAuthn library is added.
- `AddIdentityCore<User>` with `Stores.SchemaVersion = Version3`, which adds the `AspNetUserPasskeys` table (`IdentityUserPasskey<Guid>`). The schema version is set in `AddCoreInfrastructure` (Core) and in the API and Worker hosts, so both see the same model.
- No password is ever written. `PasswordHash` stays null for every account created by the flows. There is no password sign-in, no password add, and no password check.
- `IdentityPasskeyOptions` pins the relying party: `ServerDomain` comes from `Passkeys:ServerDomain`, and `ValidateOrigin` allows only the exact origins in `Passkeys:AllowedOrigins`. `ResidentKeyRequirement` is `required`, so credentials are discoverable and sign-in works without typing an email. User verification keeps the framework default (`preferred`).
- Sign-in uses discoverable credentials, and the sign-in page also offers conditional UI when the browser supports it.
- Passkeys are never removed without a replacement. `User.EnsureCanRemovePasskey` rejects the removal of the last passkey (`identity.lastPasskey`).

### Account flows
- **Sign-up:** the account is created unconfirmed. A confirmation email carries a link with an Identity email confirmation token. The SPA sends the token back, and the first passkey is registered in the same step.
- **Confirmation and first passkey:** `CompleteRegistration` saves the passkey, confirms the email and updates the security stamp in one `AppDbContext` transaction. If the attestation fails, the account stays unchanged (tested).
- **Sign-in:** a passkey assertion, followed by `SignInAsync`. "Keep me signed in" is implemented in the slice, because `PasskeySignInAsync` has no persistence flag in Identity 10. It creates a persistent cookie only when the user chooses it.
- **Recovery:** a custom `PasskeyRecoveryTokenProvider` issues the recovery token. Recovery is offered only to confirmed accounts. Completing recovery registers a new passkey and updates the security stamp, which ends every other session.
- **Passkey management:** list, add, rename and remove. Removal runs in a transaction that locks the account row (`SELECT ... FOR UPDATE`) before counting passkeys, so two parallel removals cannot both succeed when only two passkeys remain (tested).

### Tokens
- Confirmation and recovery tokens live for 2 hours (`DataProtectionTokenProviderOptions.TokenLifespan`).
- A token is single use. Completing a flow updates the security stamp, and stamp-bound tokens stop validating. Expiry is tested with a dedicated factory that sets a 1 s token lifespan, not with `FakeTimeProvider`.
- Tokens travel in links to the SPA (`/confirm`, `/recover/complete`), which strips them from the address bar with `history.replaceState` and posts them to the API in the body.
- Tokens are never logged. Logs record the user id only.

### Sessions
- The cookie is `__Host-aspireboot`: HttpOnly, Secure, SameSite=Strict, path `/`, 8 hours with sliding expiration.
- `SecurityStampValidatorOptions.ValidationInterval` comes from `Identity:SecurityStampValidationSeconds` (default 60). A session that outlives a recovery or a passkey removal ends within this interval. The framework default of 30 minutes is too long for this.

### Enumeration and abuse
- Sign-up, resend confirmation and recovery return the same `202` body whether or not the email exists. Sign-up for an existing email sends nothing.
- Resend and recovery use a 60 s cooldown per normalized email, stored as a SHA-256 hash in the distributed cache (`Identity:EmailCooldownSeconds`).
- Sign-in failures return one generic `401` Problem Details body.
- All identity endpoints are rate limited (ADR 0002) and protected by the antiforgery filter.

## Sources
- Microsoft Learn, ASP.NET Core passkeys (ASP.NET Core 10): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0
- Microsoft Learn, `IdentityPasskeyOptions` (ASP.NET Core 10): https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.identitypasskeyoptions?view=aspnetcore-10.0
- Verified in the Microsoft.AspNetCore.Identity 10.0.15 assemblies by reflection (task T2): the `SignInManager` passkey methods, `UserManager` passkey methods, `UserPasskeyInfo` members (`CreatedAt` exists, there is no last-used timestamp) and the schema v3 mapping.
- W3C Web Authentication Level 3 and FIDO Alliance passkey guidance (model knowledge, not re-fetched).

## Residual risks (accepted)
1. **Signup timing side channel.** A new email creates an account and enqueues an email, while an existing email only checks the cooldown. The response body and status are identical, but timing can reveal whether an address is registered. Rate limits (ADR 0002) and the cooldown bound the attacker's throughput. Equalizing the paths is not done because it would add work to every signup for a small gain.
2. **Cooldown is check-then-set, not atomic.** `EmailCooldown` reads the cache key and then writes it. Two concurrent requests for the same email can both send one email. The result is at most one extra email in a race window, which the rate limit also bounds. An atomic set-if-absent would need a Redis-specific command, and it is not justified yet.
3. **No last-used timestamp on passkeys.** Identity 10 stores `CreatedAt` and `SignCount` but no last-used time, so the UI cannot show when a passkey was last used. Adding it needs a column updated on each assertion, which is deferred until the UI needs it.

## Consequences
- No password reset, password change, lockout or password policy exists. Documentation, templates and skills must not describe them. An architecture test (`NoPasswordApisTests`) fails when the API or Core sources reference `PasswordSignInAsync`, `CheckPasswordSignInAsync`, `AddPasswordAsync` or `PasswordHash`.
- Users need a passkey-capable device or password manager. Browsers without WebAuthn support cannot sign up, and the UI says so.
- Losing every passkey and the email account means the account cannot be recovered in the product. Recovery depends on the email account remaining accessible.
- Test ceremonies use `SoftwareAuthenticator` (an ES256 key, `none` attestation and CBOR encoding) against origin `https://localhost`.
