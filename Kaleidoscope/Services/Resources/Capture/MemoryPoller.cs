using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Database;
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
    private readonly KaleidoscopeDbService _db;
    private readonly GameStateService _gameState;
    private DateTime _nextPoll;
    private const int PollIntervalMs = 500;
    private ulong _lastNameStampedPid;

    public MemoryPoller(IFramework framework, IClientState clientState, ResourceObservationService service, KaleidoscopeDbService db, GameStateService gameState)
    {
        _framework = framework;
        _clientState = clientState;
        _service = service;
        _db = db;
        _gameState = gameState;
        _framework.Update += OnFrameworkUpdate;
    }

    private unsafe void OnFrameworkUpdate(IFramework f)
    {
        var now = DateTime.UtcNow;

        if (now < _nextPoll || !_clientState.IsLoggedIn) return;
        _nextPoll = now.AddMilliseconds(PollIntervalMs);

        var im = _gameState.InventoryManagerInstance();
        if (im == null) return;
        var pid = _gameState.PlayerContentId;
        if (pid == 0) return;

        // Stamp the player's name once per character switch so the data table can display it.
        if (_lastNameStampedPid != pid)
        {
            var playerName = _gameState.LocalPlayerName;
            if (!string.IsNullOrEmpty(playerName))
            {
                _db.UpsertOwnerName(pid, OwnerKind.Player, playerName);
                _lastNameStampedPid = pid;
            }
        }

        // During the login/logout transition IsLoggedIn is true while the gil container is not yet
        // loaded (or is being torn down), so GetGil() transiently returns 0. A real character never
        // holds 0 gil, so a 0 read means "not loaded" — skip it. Recording it would overwrite the
        // stored value and persist across restarts (characters showing 0 gil on game restart).
        var playerGil = im->GetGil();
        if (playerGil > 0)
            Observe(pid, ResourceCatalog.GilItemId,     playerGil,                 Container.SpecialPlayer);
        Observe(pid, ResourceCatalog.MGPItemId,         im->GetGoldSaucerCoin(),   Container.SpecialPlayer);
        Observe(pid, ResourceCatalog.WolfMarksItemId,   im->GetWolfMarks(),        Container.SpecialPlayer);
        Observe(pid, ResourceCatalog.AlliedSealsItemId, im->GetAlliedSeals(),      Container.SpecialPlayer);

        // FC Credits — null unless the FC Credit Shop is open (the only time the value is current
        // and in-context); skip silently then. Offline FCs are filled in from AutoRetainer's config
        // by AutoRetainerFcPointsSyncService instead.
        var fcCredits = _gameState.GetFreeCompanyCredits();
        if (fcCredits.HasValue)
            Observe(pid, ResourceCatalog.FCCreditsItemId, fcCredits.Value, Container.SpecialFreeCompany);

        // Free Company gil — only meaningful when player is in an FC.
        var fcId = _gameState.GetFreeCompanyId();
        if (fcId != 0)
        {
            var fcGil = im->GetFreeCompanyGil();
            _service.RecordObservation(new ResourceObservation
            {
                Key = new ResourceKey
                {
                    OwnerId   = fcId,
                    OwnerKind = OwnerKind.FreeCompany,
                    Container = Container.FreeCompanyGil,
                    ItemId    = ResourceCatalog.GilItemId,
                    Slot      = -1,
                },
                Quantity      = fcGil,
                UpdatedAt     = DateTime.UtcNow,
                ParentOwnerId = pid,
            });
        }

        // Per-retainer gil — available whenever RetainerManager cache is populated.
        foreach (var (retainerId, gil) in _gameState.GetPerRetainerGil())
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
