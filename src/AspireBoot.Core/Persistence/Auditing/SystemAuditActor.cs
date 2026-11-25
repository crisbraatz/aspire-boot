namespace AspireBoot.Core.Persistence.Auditing;

/// <summary>Actor for hosts without a signed-in user: every change is attributed to the system.</summary>
public sealed class SystemAuditActor : IAuditActor
{
    public Guid? UserId => null;
}