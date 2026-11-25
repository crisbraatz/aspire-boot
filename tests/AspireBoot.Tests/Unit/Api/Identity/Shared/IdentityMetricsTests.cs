using System.Diagnostics.Metrics;

using AspireBoot.Api.Identity.Shared;

namespace AspireBoot.Tests.Unit.Api.Identity.Shared;

[Trait("Category", "Unit")]
public sealed class IdentityMetricsTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, long Value, IReadOnlyDictionary<string, object?> Tags)> _measurements = [];

    public IdentityMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == IdentityMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }

            _measurements.Add((instrument.Name, value, dictionary));
        });
        _listener.Start();
    }

    [Fact]
    public void MeterName_IsAspireBootIdentity()
    {
        Assert.Equal("AspireBoot.Identity", IdentityMetrics.MeterName);
    }

    [Fact]
    public void EachBusinessEvent_IncrementsItsOwnCounterByOne()
    {
        IdentityMetrics.RecordSignUp();
        IdentityMetrics.RecordEmailConfirmation();
        IdentityMetrics.RecordPasskeyAdded();
        IdentityMetrics.RecordPasskeyRemoved();
        IdentityMetrics.RecordRecoveryRequested();
        IdentityMetrics.RecordRecoveryCompleted();

        Assert.Equal(
            [
                "aspireboot.identity.sign_ups",
                "aspireboot.identity.email_confirmations",
                "aspireboot.identity.passkeys_added",
                "aspireboot.identity.passkeys_removed",
                "aspireboot.identity.recoveries_requested",
                "aspireboot.identity.recoveries_completed",
            ],
            _measurements.Select(m => m.Name));
        Assert.All(_measurements, m => Assert.Equal(1, m.Value));
        Assert.All(_measurements, m => Assert.Empty(m.Tags));
    }

    [Fact]
    public void RecordSignIn_TagsOutcomeAsSuccessOrFailure()
    {
        IdentityMetrics.RecordSignIn(success: true);
        IdentityMetrics.RecordSignIn(success: false);

        Assert.Equal(["success", "failure"], _measurements.Select(m => (string)m.Tags["outcome"]!));
        Assert.All(_measurements, m => Assert.Equal("aspireboot.identity.sign_ins", m.Name));
    }

    [Fact]
    public void RecordAvatarUpload_TagsTheOutcome()
    {
        string[] outcomes = ["stored", "rejected_type", "rejected_size", "rejected_dimensions", "corrupt", "storage_error", "conflict"];

        foreach (var outcome in outcomes)
        {
            IdentityMetrics.RecordAvatarUpload(outcome);
        }

        Assert.Equal(outcomes, _measurements.Select(m => (string)m.Tags["outcome"]!));
        Assert.All(_measurements, m => Assert.Equal("aspireboot.identity.avatar.uploads", m.Name));
        Assert.All(_measurements, m => Assert.Equal(1, m.Value));
    }

    [Fact]
    public void RecordAvatarRemoval_IncrementsItsCounterWithoutTags()
    {
        IdentityMetrics.RecordAvatarRemoval();

        var measurement = Assert.Single(_measurements);
        Assert.Equal("aspireboot.identity.avatar.removals", measurement.Name);
        Assert.Equal(1, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    [Fact]
    public void RecordRateLimited_TagsThePolicyName()
    {
        IdentityMetrics.RecordRateLimited("signup");

        var measurement = Assert.Single(_measurements);
        Assert.Equal("aspireboot.identity.rate_limited", measurement.Name);
        Assert.Equal("signup", measurement.Tags["policy"]);
    }

    public void Dispose() => _listener.Dispose();
}