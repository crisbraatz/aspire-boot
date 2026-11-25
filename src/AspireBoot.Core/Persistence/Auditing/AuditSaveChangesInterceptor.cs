using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AspireBoot.Core.Persistence.Auditing;

/// <summary>
/// Adds <see cref="AuditEntry"/> rows to the context being saved, so they commit in the same SaveChanges batch and
/// transaction as the audited changes. It never calls SaveChanges and ignores <see cref="AuditEntry"/> itself.
/// A singleton: pooled contexts resolve it from the root provider, so the actor must read ambient state on each call.
/// </summary>
public sealed class AuditSaveChangesInterceptor(IAuditActor actor, TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AddAuditEntries(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AddAuditEntries(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        DetachAuditEntries(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        DetachAuditEntries(eventData.Context);
        return Task.CompletedTask;
    }

    /// <summary>A failed save leaves the rows added by this interceptor tracked; a retry on the same context would add them twice.</summary>
    private static void DetachAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<AuditEntry>().Where(entry => entry.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();

        var rows = AuditEntryFactory.Create(
            context.ChangeTracker.Entries().ToList(),
            actor.UserId,
            timeProvider.GetUtcNow(),
            Activity.Current?.TraceId.ToString());

        if (rows.Count > 0)
        {
            context.Set<AuditEntry>().AddRange(rows);
        }
    }
}