using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;

namespace Kaleidoscope.Services.Resources.Sources;

/// <summary>
/// Watches the Trade addon's PreFinalize event. Stamps SourceKind.Trade on the next ~3s
/// of observations. Detail is left null for now — partner-name extraction requires reading
/// specific UI nodes; can be added when the in-app TestsCategory smoke test surfaces the need.
/// </summary>
public sealed class TradeSource : IObservationSource
{
    private readonly IAddonLifecycle _lifecycle;
    private readonly SourceTagSink _sink;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(3);

    public TradeSource(IAddonLifecycle lifecycle, ResourceObservationService obsSvc)
    {
        _lifecycle = lifecycle;
        _sink = obsSvc.Sink;
        _lifecycle.RegisterListener(AddonEvent.PreFinalize, "Trade", OnTradeFinalize);
    }

    private void OnTradeFinalize(AddonEvent type, AddonArgs args)
    {
        _sink.Stamp(new SourceTag
        {
            Kind      = SourceKind.Trade,
            Detail    = null,
            StampedAt = DateTime.UtcNow,
        }, Ttl);
    }

    public void Dispose() => _lifecycle.UnregisterListener(AddonEvent.PreFinalize, "Trade", OnTradeFinalize);
}
