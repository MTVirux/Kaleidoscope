using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources.Sources;

/// <summary>
/// Watches IDutyState.DutyCompleted and stamps the next observation(s) with SourceKind.DutyReward.
/// TTL is generous (5 s) because reward observations can lag the completion event by
/// several frames as the game writes inventory.
/// </summary>
public sealed class DutyRewardSource : IObservationSource
{
    private readonly IDutyState _dutyState;
    private readonly SourceTagSink _sink;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    public DutyRewardSource(IDutyState dutyState, ResourceObservationService obsSvc)
    {
        _dutyState = dutyState;
        _sink = obsSvc.Sink;
        _dutyState.DutyCompleted += OnDutyCompleted;
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        string? name = null;
        try
        {
            var row = args.ContentFinderCondition.Value;
            name = row.Name.ExtractText();
        }
        catch { /* best-effort */ }

        _sink.Stamp(new SourceTag
        {
            Kind      = SourceKind.DutyReward,
            Detail    = string.IsNullOrWhiteSpace(name) ? null : name,
            StampedAt = DateTime.UtcNow,
        }, Ttl);
    }

    public void Dispose() => _dutyState.DutyCompleted -= OnDutyCompleted;
}
