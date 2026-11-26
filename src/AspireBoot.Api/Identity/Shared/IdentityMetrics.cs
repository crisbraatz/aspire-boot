using System.Diagnostics.Metrics;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// Business counters for the identity slices. Every tag is low-cardinality (an outcome or a rate-limit policy name);
/// no email, user id or passkey name is ever recorded. Exported through the <c>AspireBoot.*</c> meter filter in ServiceDefaults.
/// </summary>
internal static class IdentityMetrics
{
    public const string MeterName = "AspireBoot.Identity";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> SignUps = Meter.CreateCounter<long>(
        "aspireboot.identity.sign_ups", "{account}", "Accounts created by sign-up.");

    private static readonly Counter<long> EmailConfirmations = Meter.CreateCounter<long>(
        "aspireboot.identity.email_confirmations", "{confirmation}", "Email addresses confirmed through a link.");

    private static readonly Counter<long> AdminBootstraps = Meter.CreateCounter<long>(
        "aspireboot.identity.admin_bootstrapped", "{account}", "Accounts granted Admin by the bootstrap email on confirmation.");

    private static readonly Counter<long> SignIns = Meter.CreateCounter<long>(
        "aspireboot.identity.sign_ins", "{attempt}", "Passkey sign-in attempts, tagged by outcome.");

    private static readonly Counter<long> PasskeysAdded = Meter.CreateCounter<long>(
        "aspireboot.identity.passkeys_added", "{passkey}", "Passkeys registered, including the first one.");

    private static readonly Counter<long> PasskeysRemoved = Meter.CreateCounter<long>(
        "aspireboot.identity.passkeys_removed", "{passkey}", "Passkeys removed.");

    private static readonly Counter<long> RecoveriesRequested = Meter.CreateCounter<long>(
        "aspireboot.identity.recoveries_requested", "{request}", "Recovery links sent to confirmed accounts.");

    private static readonly Counter<long> RecoveriesCompleted = Meter.CreateCounter<long>(
        "aspireboot.identity.recoveries_completed", "{recovery}", "Recovery links used to register a new passkey.");

    private static readonly Counter<long> AvatarUploads = Meter.CreateCounter<long>(
        "aspireboot.identity.avatar.uploads", "{upload}", "Avatar uploads, tagged by outcome.");

    private static readonly Counter<long> AvatarRemovals = Meter.CreateCounter<long>(
        "aspireboot.identity.avatar.removals", "{avatar}", "Avatars removed.");

    private static readonly Counter<long> RolesChanged = Meter.CreateCounter<long>(
        "aspireboot.identity.roles_changed", "{change}", "Role changes made by administrators, tagged by the new role.");

    private static readonly Counter<long> SessionsEnded = Meter.CreateCounter<long>(
        "aspireboot.identity.sessions.ended", "{session}", "Sessions ended, tagged by reason (user, others, signout or stamp).");

    private static readonly Counter<long> RateLimited = Meter.CreateCounter<long>(
        "aspireboot.identity.rate_limited", "{request}", "Requests rejected with 429, tagged by rate-limit policy.");

    public static void RecordSignUp() => SignUps.Add(1);

    public static void RecordEmailConfirmation() => EmailConfirmations.Add(1);

    public static void RecordAdminBootstrapped() => AdminBootstraps.Add(1);

    public static void RecordSignIn(bool success) =>
        SignIns.Add(1, new KeyValuePair<string, object?>("outcome", success ? "success" : "failure"));

    public static void RecordPasskeyAdded() => PasskeysAdded.Add(1);

    public static void RecordPasskeyRemoved() => PasskeysRemoved.Add(1);

    public static void RecordRecoveryRequested() => RecoveriesRequested.Add(1);

    public static void RecordRecoveryCompleted() => RecoveriesCompleted.Add(1);

    /// <param name="outcome">stored, rejected_type, rejected_size, rejected_dimensions, corrupt, storage_error or conflict (retries ran out).</param>
    public static void RecordAvatarUpload(string outcome) =>
        AvatarUploads.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordAvatarRemoval() => AvatarRemovals.Add(1);

    /// <param name="role">The new role name (Admin or User): a fixed, low-cardinality set.</param>
    public static void RecordRoleChanged(string role) =>
        RolesChanged.Add(1, new KeyValuePair<string, object?>("role", role));

    /// <param name="reason">user, others, signout or stamp: a fixed, low-cardinality set.</param>
    public static void RecordSessionsEnded(string reason, int count) =>
        SessionsEnded.Add(count, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordRateLimited(string policy) =>
        RateLimited.Add(1, new KeyValuePair<string, object?>("policy", policy));
}