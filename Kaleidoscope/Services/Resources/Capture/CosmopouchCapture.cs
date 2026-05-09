using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// Direct-InventoryManager fallback for Cosmopouch1/Cosmopouch2. Workaround for
/// goatcorp/Dalamud#2329 (struct-truncation bug in IGameInventory for these containers).
/// Polls every 500 ms — small (~80 slots × 2 containers) so cost is negligible.
/// Delete this service once the upstream fix lands and re-enable the event path in
/// InventoryEventCapture.
/// </summary>
public sealed class CosmopouchCapture : IDisposable, IRequiredService
{
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly ResourceObservationService _service;
    private DateTime _nextPoll;
    private const int PollIntervalMs = 500;

    public CosmopouchCapture(IFramework framework, IClientState clientState, ResourceObservationService service)
    {
        _framework = framework;
        _clientState = clientState;
        _service = service;
        _framework.Update += OnTick;
    }

    private unsafe void OnTick(IFramework f)
    {
        var now = DateTime.UtcNow;
        if (now < _nextPoll || !_clientState.IsLoggedIn) return;
        _nextPoll = now.AddMilliseconds(PollIntervalMs);

        var im = GameStateService.InventoryManagerInstance();
        if (im == null) return;
        var pid = GameStateService.PlayerContentId;
        if (pid == 0) return;

        Scan(im, InventoryType.Cosmopouch1, Container.Cosmopouch1, pid);
        Scan(im, InventoryType.Cosmopouch2, Container.Cosmopouch2, pid);
    }

    private unsafe void Scan(InventoryManager* im, InventoryType type, Container container, ulong pid)
    {
        var c = im->GetInventoryContainer(type);
        if (c == null || !c->IsLoaded) return;

        for (int i = 0; i < c->GetSize(); i++)
        {
            var slot = c->GetInventorySlot(i);
            if (slot == null) continue;
            if (slot->ItemId == 0) continue;

            var flags = slot->Flags;
            var isHq = (flags & InventoryItem.ItemFlags.HighQuality) != 0;
            var isCollectable = (flags & InventoryItem.ItemFlags.Collectable) != 0;

            var resourceFlags = ResourceFlags.None;
            if (isHq) resourceFlags |= ResourceFlags.HQ;
            if (isCollectable) resourceFlags |= ResourceFlags.Collectable;

            _service.RecordObservation(new ResourceObservation
            {
                Key = new ResourceKey
                {
                    OwnerId   = pid,
                    OwnerKind = OwnerKind.Player,
                    Container = container,
                    ItemId    = slot->ItemId,
                    Slot      = slot->Slot,
                },
                Quantity       = slot->Quantity,
                Flags          = resourceFlags,
                Spiritbond     = (ushort)(isCollectable ? 0 : slot->SpiritbondOrCollectability),
                Collectability = (ushort)(isCollectable ? slot->SpiritbondOrCollectability : 0),
                Condition      = slot->Condition,
                GlamourId      = slot->GlamourId,
                UpdatedAt      = DateTime.UtcNow,
            });
        }
    }

    public void Dispose() => _framework.Update -= OnTick;
}
