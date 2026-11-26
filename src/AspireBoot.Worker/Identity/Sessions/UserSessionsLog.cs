using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Sessions;

/// <summary>Source-generated logs and the metric for session cleanup. Only counts are recorded, never a session or user value.</summary>
internal static partial class UserSessionsLog
{
    public const string MeterName = "AspireBoot.Worker.Identity";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Cleaned = Meter.CreateCounter<long>(
        "aspireboot.identity.sessions.cleaned", "{session}", "Ended or expired session rows deleted by the Worker cleanup.");

    public static void RecordCleaned(int count) => Cleaned.Add(count);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Session cleanup deleted {Count} rows")]
    public static partial void SessionsCleaned(ILogger logger, int count);
}