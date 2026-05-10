using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Inventory;
using Kaleidoscope.Services.Resources;

namespace Kaleidoscope.Services.Resources.Sources;

/// <summary>
/// Watches OnRetainerInventoryReady and stamps SourceKind.RetainerVenture for observations
/// in the few seconds following retainer summon. Detail field holds the active retainer id
/// as a hex string — venture-id resolution can be added later via RetainerManager.
/// </summary>
public sealed class RetainerVentureSource : IObservationSource
{
    private readonly InventoryChangeService _changes;
    private readonly SourceTagSink _sink;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(3);

    public RetainerVentureSource(InventoryChangeService changes, ResourceObservationService obsSvc)
    {
        _changes = changes;
        _sink = obsSvc.Sink;
        _changes.OnRetainerInventoryReady += OnReady;
    }

    private void OnReady()
    {
        var rid = GameStateService.GetActiveRetainerId();
        _sink.Stamp(new SourceTag
        {
            Kind      = SourceKind.RetainerVenture,
            Detail    = rid == 0 ? null : rid.ToString("X16"),
            StampedAt = DateTime.UtcNow,
        }, Ttl);
    }

    public void Dispose() => _changes.OnRetainerInventoryReady -= OnReady;
}
