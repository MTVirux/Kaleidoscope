using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// Captures the Glamour Dresser (glamour chest) into the unified resource store. The dresser lives in
/// <c>AgentMiragePrismPrismBox-&gt;Data-&gt;PrismBoxItems</c> (8000 entries) and has no GameInventoryType,
/// so IGameInventory.InventoryChangedRaw never fires for it. The agent only holds valid data while the
/// MiragePrismPrismBox addon is open, and the array settles a moment after the addon appears — so scanning
/// is gated on the addon lifecycle plus a ~2s stabilization delay, then throttled while the window stays
/// open to catch deposits/withdrawals. Zero idle cost: the framework tick does nothing until PostSetup flips
/// the active flag; PreFinalize clears it. Removals produce no event, so each scan zeroes out any previously
/// stored slot that is no longer occupied.
/// </summary>
public sealed class GlamourChestCapture : IDisposable, IRequiredService
{
    private const string AddonName = "MiragePrismPrismBox";
    private const int PrismBoxSize = 8000;
    private const uint HqItemIdOffset = 1_000_000;
    private static readonly TimeSpan StabilizationDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(1);

    private readonly IAddonLifecycle _lifecycle;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly ResourceObservationService _service;
    private readonly GameStateService _gameState;

    private bool _active;
    private bool _didInitialScan;
    private DateTime _nextScan;

    public GlamourChestCapture(IAddonLifecycle lifecycle, IFramework framework, IClientState clientState, ResourceObservationService service, GameStateService gameState)
    {
        _lifecycle = lifecycle;
        _framework = framework;
        _clientState = clientState;
        _service = service;
        _gameState = gameState;

        _lifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, OnAddonSetup);
        _lifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnAddonFinalize);
        _framework.Update += OnTick;
    }

    private void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        _active = true;
        _didInitialScan = false;
        _nextScan = DateTime.UtcNow + StabilizationDelay;   // first scan once the agent array has stabilized
        LogService.Debug(LogCategory.Inventory, "[GlamourChestCapture] Glamour Dresser opened");
    }

    private void OnAddonFinalize(AddonEvent type, AddonArgs args)
    {
        _active = false;
        LogService.Debug(LogCategory.Inventory, "[GlamourChestCapture] Glamour Dresser closed");
    }

    private unsafe void OnTick(IFramework framework)
    {
        if (!_active || !_clientState.IsLoggedIn) return;

        var now = DateTime.UtcNow;
        if (now < _nextScan) return;
        _nextScan = now + RescanInterval;

        var pid = _gameState.PlayerContentId;
        if (pid == 0) return;

        var agentModule = AgentModule.Instance();
        if (agentModule == null) return;
        var agent = (AgentMiragePrismPrismBox*)agentModule->GetAgentByInternalId(AgentId.MiragePrismPrismBox);
        if (agent == null || !agent->IsAgentActive() || agent->Data == null) return;

        Scan(agent, pid);
    }

    private unsafe void Scan(AgentMiragePrismPrismBox* agent, ulong pid)
    {
        var items = agent->Data->PrismBoxItems;
        var seen = new HashSet<short>();
        var batch = new List<ResourceObservation>();

        for (var i = 0; i < PrismBoxSize; i++)
        {
            var itemId = items[i].ItemId;
            if (itemId == 0) continue;

            var flags = ResourceFlags.None;
            if (itemId >= HqItemIdOffset)
            {
                itemId -= HqItemIdOffset;
                flags |= ResourceFlags.HQ;
            }

            var slot = (short)i;
            seen.Add(slot);

            batch.Add(new ResourceObservation
            {
                Key = new ResourceKey
                {
                    OwnerId   = pid,
                    OwnerKind = OwnerKind.Player,
                    Container = Container.GlamourChest,
                    ItemId    = itemId,
                    Slot      = slot,
                },
                Quantity      = 1,
                Flags         = flags,
                UpdatedAt     = DateTime.UtcNow,
                ParentOwnerId = 0,
            });
        }

        var occupied = batch.Count;
        ReconcileRemovals(pid, seen, batch);
        _service.RecordObservations(batch);

        if (!_didInitialScan)
        {
            _didInitialScan = true;
            LogService.Debug(LogCategory.Inventory, $"[GlamourChestCapture] Initial scan: {occupied} items");
        }
    }

    /// <summary>Zero out any stored dresser slot that was not seen in this scan (item was withdrawn).</summary>
    private void ReconcileRemovals(ulong pid, HashSet<short> seen, List<ResourceObservation> batch)
    {
        foreach (var (slot, itemId) in _service.Store.GetOccupiedSlots(pid, OwnerKind.Player, Container.GlamourChest))
        {
            if (seen.Contains(slot)) continue;

            batch.Add(new ResourceObservation
            {
                Key = new ResourceKey
                {
                    OwnerId   = pid,
                    OwnerKind = OwnerKind.Player,
                    Container = Container.GlamourChest,
                    ItemId    = itemId,
                    Slot      = slot,
                },
                Quantity      = 0,
                Flags         = ResourceFlags.None,
                UpdatedAt     = DateTime.UtcNow,
                ParentOwnerId = 0,
            });
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnTick;
        _lifecycle.UnregisterListener(AddonEvent.PostSetup, AddonName, OnAddonSetup);
        _lifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnAddonFinalize);
    }
}
