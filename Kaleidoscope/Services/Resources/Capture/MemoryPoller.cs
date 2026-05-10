using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// 500 ms framework-tick poller for the five game-memory-only counters that don't fire
/// IGameInventory.InventoryChanged events: Gil, MGP, WolfMarks, AlliedSeals, FCCredits.
/// All other currencies live in the Currency container and are captured event-driven.
/// </summary>
public sealed class MemoryPoller : IDisposable, IRequiredService
{
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly ResourceObservationService _service;
    private DateTime _nextPoll;
    private const int PollIntervalMs = 500;

    public MemoryPoller(IFramework framework, IClientState clientState, ResourceObservationService service)
    {
        _framework = framework;
        _clientState = clientState;
        _service = service;
        _framework.Update += OnFrameworkUpdate;
    }

    private unsafe void OnFrameworkUpdate(IFramework f)
    {
        var now = DateTime.UtcNow;
        if (now < _nextPoll || !_clientState.IsLoggedIn) return;
        _nextPoll = now.AddMilliseconds(PollIntervalMs);

        var im = GameStateService.InventoryManagerInstance();
        if (im == null) return;
        var pid = GameStateService.PlayerContentId;
        if (pid == 0) return;

        Observe(pid, ResourceCatalog.GilItemId,         im->GetGil(),              Container.SpecialPlayer);
        Observe(pid, ResourceCatalog.MGPItemId,         im->GetGoldSaucerCoin(),   Container.SpecialPlayer);
        Observe(pid, ResourceCatalog.WolfMarksItemId,   im->GetWolfMarks(),        Container.SpecialPlayer);
        Observe(pid, ResourceCatalog.AlliedSealsItemId, im->GetAlliedSeals(),      Container.SpecialPlayer);

        // FC Credits — long? return; skip silently if null (not in an FC).
        var fcCredits = GameStateService.GetFreeCompanyCredits();
        if (fcCredits.HasValue)
            Observe(pid, ResourceCatalog.FCCreditsItemId, fcCredits.Value, Container.SpecialFreeCompany);

        // Per-retainer gil — available whenever RetainerManager cache is populated.
        foreach (var (retainerId, gil) in GameStateService.GetPerRetainerGil())
        {
            _service.RecordObservation(new ResourceObservation
            {
                Key = new ResourceKey
                {
                    OwnerId   = retainerId,
                    OwnerKind = OwnerKind.Retainer,
                    Container = Container.RetainerGil,
                    ItemId    = ResourceCatalog.GilItemId,
                    Slot      = -1,
                },
                Quantity      = gil,
                UpdatedAt     = DateTime.UtcNow,
                ParentOwnerId = pid,
            });
        }
    }

    private void Observe(ulong ownerId, uint itemId, long quantity, Container container)
    {
        _service.RecordObservation(new ResourceObservation
        {
            Key = new ResourceKey
            {
                OwnerId   = ownerId,
                OwnerKind = container == Container.SpecialFreeCompany ? OwnerKind.FreeCompany : OwnerKind.Player,
                Container = container,
                ItemId    = itemId,
                Slot      = -1,
            },
            Quantity  = quantity,
            UpdatedAt = DateTime.UtcNow,
        });
    }

    public void Dispose() => _framework.Update -= OnFrameworkUpdate;
}
