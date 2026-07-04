using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Sources;

/// <summary>
/// Watches IDutyState.DutyCompleted and stamps the next observation(s) with SourceKind.DutyReward.
/// TTL is generous (5 s) because reward observations can lag the completion event by
/// several frames as the game writes inventory.
/// </summary>
public sealed class DutyRewardSource : ObservationSourceBase, IRequiredService
{
    private readonly IDutyState _dutyState;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    public DutyRewardSource(IDutyState dutyState, ResourceObservationService obsSvc) : base(obsSvc)
    {
        _dutyState = dutyState;
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

        Stamp(SourceKind.DutyReward, name, Ttl);
    }

    public override void Dispose() => _dutyState.DutyCompleted -= OnDutyCompleted;
}
