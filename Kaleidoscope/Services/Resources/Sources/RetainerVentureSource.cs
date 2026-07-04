using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Inventory;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Sources;

/// <summary>
/// Watches OnRetainerInventoryReady and stamps SourceKind.RetainerVenture for observations
/// in the few seconds following retainer summon. Detail field holds the active retainer id
/// as a hex string — venture-id resolution can be added later via RetainerManager.
/// </summary>
public sealed class RetainerVentureSource : ObservationSourceBase, IRequiredService
{
    private readonly InventoryChangeService _changes;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(3);

    public RetainerVentureSource(InventoryChangeService changes, ResourceObservationService obsSvc) : base(obsSvc)
    {
        _changes = changes;
        _changes.OnRetainerInventoryReady += OnReady;
    }

    private void OnReady()
    {
        var rid = GameStateService.GetActiveRetainerId();
        Stamp(SourceKind.RetainerVenture, rid == 0 ? null : rid.ToString("X16"), Ttl);
    }

    public override void Dispose() => _changes.OnRetainerInventoryReady -= OnReady;
}
