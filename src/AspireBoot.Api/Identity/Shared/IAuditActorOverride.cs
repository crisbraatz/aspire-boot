namespace AspireBoot.Api.Identity.Shared;

/// <summary>Attributes the audit rows written by the current flow to a user who is not signed in (yet), such as the owner of a registration link.</summary>
internal interface IAuditActorOverride
{
    /// <summary>Audit rows written until the returned scope is disposed name <paramref name="userId"/> as the actor.</summary>
    IDisposable ActAs(Guid userId);
}