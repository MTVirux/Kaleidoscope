using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// Scans all four player inventory bags on Trade finalize to catch completely cleared slots.
/// InventoryChangedRaw does not reliably fire for slots emptied entirely by a trade, so this
/// direct InventoryManager scan reconciles those slots when the trade window closes.
/// This is also the single PreFinalize "Trade" listener: it stamps SourceKind.Trade before scanning
/// so both the reconciled slots and any trade-driven InventoryChangedRaw events pick up the tag.
/// </summary>
public sealed class TradeReconcileCapture : IDisposable, IRequiredService
{
    private readonly IAddonLifecycle _lifecycle;
    private readonly IClientState _clientState;
    private readonly ResourceObservationService _service;
    private readonly GameStateService _gameState;

    private static readonly TimeSpan TradeTagTtl = TimeSpan.FromSeconds(3);

    private static readonly (InventoryType GameType, Container Container)[] PlayerBags =
    {
        (InventoryType.Inventory1, Container.Inventory1),
        (InventoryType.Inventory2, Container.Inventory2),
        (InventoryType.Inventory3, Container.Inventory3),
        (InventoryType.Inventory4, Container.Inventory4),
    };

    public TradeReconcileCapture(IAddonLifecycle lifecycle, IClientState clientState, ResourceObservationService service, GameStateService gameState)
    {
        _lifecycle = lifecycle;
        _clientState = clientState;
        _service = service;
        _gameState = gameState;
        _lifecycle.RegisterListener(AddonEvent.PreFinalize, "Trade", OnTradeFinalize);
    }

    private unsafe void OnTradeFinalize(AddonEvent type, AddonArgs args)
    {
        if (!_clientState.IsLoggedIn) return;

        // Stamp before scanning so the reconciled slots (and any concurrent trade InventoryChangedRaw
        // events) are attributed to the trade. Detail is left null — partner-name extraction TBD.
        _service.Sink.Stamp(new SourceTag
        {
            Kind      = SourceKind.Trade,
            Detail    = null,
            StampedAt = DateTime.UtcNow,
        }, TradeTagTtl);

        var im = _gameState.InventoryManagerInstance();
        if (im == null) return;
        var pid = _gameState.PlayerContentId;
        if (pid == 0) return;

        // Accumulate the cleared slots across all four bags into one batch so the sweep commits
        // under a single observation-lock acquisition.
        var batch = new List<ResourceObservation>();
        foreach (var (gameType, container) in PlayerBags)
            ScanBag(im, gameType, container, pid, batch);

        _service.RecordObservations(batch);
    }

    private unsafe void ScanBag(InventoryManager* im, InventoryType gameType, Container container, ulong pid, List<ResourceObservation> batch)
    {
        var c = im->GetInventoryContainer(gameType);
        if (c == null || !c->IsLoaded) return;

        for (var i = 0; i < c->GetSize(); i++)
        {
            var slot = c->GetInventorySlot(i);
            if (slot == null) continue;
            if (slot->ItemId != 0) continue;

            var prevItemId = _service.Store.GetItemIdForSlot(pid, OwnerKind.Player, container, slot->Slot);
            if (prevItemId is null) continue;

            // Empty slot: the mapper reads the now-zeroed slot (quantity 0, no flags) and stamps it
            // onto the item that used to occupy it, zeroing that item's stored quantity.
            var key = new ResourceKey
            {
                OwnerId   = pid,
                OwnerKind = OwnerKind.Player,
                Container = container,
                ItemId    = prevItemId.Value,
                Slot      = slot->Slot,
            };
            batch.Add(InventorySlotMapper.FromInventorySlot(slot, key, 0UL));
        }
    }

    public void Dispose() => _lifecycle.UnregisterListener(AddonEvent.PreFinalize, "Trade", OnTradeFinalize);
}
