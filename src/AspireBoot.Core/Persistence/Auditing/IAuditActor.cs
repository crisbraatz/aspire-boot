namespace AspireBoot.Core.Persistence.Auditing;

/// <summary>Who is performing the current unit of work. A null <see cref="UserId"/> means the system (worker, seeding, tooling).</summary>
public interface IAuditActor
{
    Guid? UserId { get; }
}