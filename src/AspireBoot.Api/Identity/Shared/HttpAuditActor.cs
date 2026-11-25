using AspireBoot.Core.Persistence.Auditing;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// Attributes changes to the signed-in user of the current request, or to the user set by <see cref="ActAs"/> for
/// anonymous self-service flows (registration and recovery links). A singleton (pooled contexts resolve the audit
/// interceptor from the root provider), so it reads the ambient <see cref="IHttpContextAccessor"/> and the
/// <see cref="AsyncLocal{T}"/> override on every call.
/// </summary>
internal sealed class HttpAuditActor(IHttpContextAccessor accessor, IOptions<IdentityOptions> identityOptions) : IAuditActor, IAuditActorOverride
{
    private readonly AsyncLocal<Guid?> _override = new();

    public Guid? UserId =>
        _override.Value
        ?? (accessor.HttpContext?.User.FindFirst(identityOptions.Value.ClaimsIdentity.UserIdClaimType)?.Value is { } value
            && Guid.TryParse(value, out var id)
            ? id
            : null);

    public IDisposable ActAs(Guid userId)
    {
        var previous = _override.Value;
        _override.Value = userId;
        return new Restore(_override, previous);
    }

    private sealed class Restore(AsyncLocal<Guid?> target, Guid? previous) : IDisposable
    {
        public void Dispose() => target.Value = previous;
    }
}