using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using AspireBoot.Api.Identity.Shared;

namespace AspireBoot.Tests.Integration.Identity.Account.Sessions;

/// <summary>Captures the sessions.ended counter (reason and value). Other tests may add measurements, so assert with Contains.</summary>
internal sealed class SessionsEndedListener : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentBag<(string Reason, long Value)> _measurements = [];

    public SessionsEndedListener()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == IdentityMetrics.MeterName && instrument.Name == "aspireboot.identity.sessions.ended")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "reason")
                {
                    _measurements.Add((tag.Value?.ToString() ?? string.Empty, value));
                }
            }
        });
        _listener.Start();
    }

    public bool Has(string reason, long value) => _measurements.Contains((reason, value));

    public void Dispose() => _listener.Dispose();
}