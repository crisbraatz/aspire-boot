namespace AspireBoot.Core.Persistence.Auditing;

/// <summary>One append-only row describing a change to an audited entity. Not an aggregate; written by the audit interceptor.</summary>
public sealed class AuditEntry
{
    public const string ActorUser = "user";
    public const string ActorSystem = "system";
    public const string ActionInsert = "insert";
    public const string ActionUpdate = "update";
    public const string ActionDelete = "delete";

    private AuditEntry()
    {
    }

    public Guid Id { get; private init; }

    public DateTimeOffset OccurredAt { get; private init; }

    public Guid? ActorUserId { get; private init; }

    public string ActorType { get; private init; } = null!;

    public string TableName { get; private init; } = null!;

    public string EntityType { get; private init; } = null!;

    public string EntityKey { get; private init; } = null!;

    public string Action { get; private init; } = null!;

    /// <summary>JSON document: <c>{ "field": { "old": ..., "new": ... } }</c>. Masked values are <c>"***"</c>.</summary>
    public string Changes { get; private init; } = null!;

    public string? TraceId { get; private init; }

    public static AuditEntry Create(
        DateTimeOffset occurredAt,
        Guid? actorUserId,
        string tableName,
        string entityType,
        string entityKey,
        string action,
        string changes,
        string? traceId) => new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = occurredAt,
            ActorUserId = actorUserId,
            ActorType = actorUserId is null ? ActorSystem : ActorUser,
            TableName = tableName,
            EntityType = entityType,
            EntityKey = entityKey,
            Action = action,
            Changes = changes,
            TraceId = traceId,
        };
}