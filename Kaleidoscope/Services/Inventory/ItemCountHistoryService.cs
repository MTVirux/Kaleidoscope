using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Database;
using Kaleidoscope.Services.Resources;
using Kaleidoscope.Services.Resources.Adapters;
using OtterGui.Services;

namespace Kaleidoscope.Services.Inventory;

/// <summary>
/// Restores historical item-count sampling on top of the unified resources pipeline.
/// The legacy sampler that wrote the "Item_{id}" / "ItemRetainer_{id}" series went away with
/// the old inventory capture, but the Data tool still reads those series (the PlayerAggregate /
/// RetainerAggregate history rows) for its count graphs. This service listens for committed
/// observations on tracked items, coalesces them per framework tick, recomputes the active
/// character's totals from ResourceStore, and persists changed values off the framework thread.
/// </summary>
public sealed class ItemCountHistoryService : IDisposable, IRequiredService
{
    private static readonly TimeSpan RetainerIdCacheExpiry = TimeSpan.FromMinutes(5);

    private readonly IFramework _framework;
    private readonly ResourceObservationService _observations;
    private readonly ResourceStore _store;
    private readonly ConfigurationService _configService;
    private readonly KaleidoscopeDbService _dbService;
    private readonly TimeSeriesCacheService _cacheService;
    private readonly GameStateService _gameState;

    private readonly object _pendingLock = new();
    private readonly HashSet<uint> _pendingItems = new();

    private ulong _seededCharacterId;

    private volatile List<ulong> _retainerIds = new();
    private ulong _retainerIdsCharacterId;
    private DateTime _retainerIdsRefreshedAt = DateTime.MinValue;

    private int _flushRunning;

    public ItemCountHistoryService(
        IFramework framework,
        ResourceObservationService observations,
        ResourceStore store,
        ConfigurationService configService,
        KaleidoscopeDbService dbService,
        TimeSeriesCacheService cacheService,
        GameStateService gameState)
    {
        _framework = framework;
        _observations = observations;
        _store = store;
        _configService = configService;
        _dbService = dbService;
        _cacheService = cacheService;
        _gameState = gameState;

        _framework.Update += OnFrameworkUpdate;
        _observations.ObservationCommitted += OnObservationCommitted;

        LogService.Debug(LogCategory.Inventory, "[ItemCountHistoryService] Initialized");
    }

    /// <summary>
    /// Queues an item for sampling on the next flush regardless of pending observations —
    /// used when historical tracking is toggled on so the series gets an immediate baseline.
    /// </summary>
    public void RequestSample(uint itemId)
    {
        if (itemId == 0) return;
        lock (_pendingLock) _pendingItems.Add(itemId);
    }

    private void OnObservationCommitted(ResourceKey key)
    {
        if (!ItemCountProjection.AffectsItemCounts(key)) return;
        if (!_configService.Config.ItemsWithHistoricalTracking.Contains(key.ItemId)) return;

        lock (_pendingLock) _pendingItems.Add(key.ItemId);

        // A retainer not attributed yet (e.g. first opened this session) — refresh the id list.
        if (key.OwnerKind == OwnerKind.Retainer && !_retainerIds.Contains(key.OwnerId))
            _retainerIdsRefreshedAt = DateTime.MinValue;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var characterId = _gameState.PlayerContentId;
        if (characterId == 0) return;   // retain pending until a character is active

        if (characterId != _seededCharacterId)
        {
            _seededCharacterId = characterId;
            SeedAllTrackedItems();
        }

        uint[] items;
        lock (_pendingLock)
        {
            if (_pendingItems.Count == 0) return;
            // A flush is still writing — keep pending and retry next tick.
            if (Interlocked.CompareExchange(ref _flushRunning, 1, 0) != 0) return;
            items = new uint[_pendingItems.Count];
            _pendingItems.CopyTo(items);
            _pendingItems.Clear();
        }

        _ = Task.Run(() => Flush(characterId, items));
    }

    /// <summary>Queues every tracked item once so a login/character switch gets baseline rows.</summary>
    private void SeedAllTrackedItems()
    {
        var tracked = _configService.Config.ItemsWithHistoricalTracking;
        if (tracked.Count == 0) return;
        lock (_pendingLock)
        {
            foreach (var itemId in tracked)
                _pendingItems.Add(itemId);
        }
    }

    private void Flush(ulong characterId, uint[] itemIds)
    {
        try
        {
            var samples = ItemCountProjection.BuildSamples(_store, characterId, GetRetainerIds(characterId), itemIds);
            _dbService.SaveSamplesIfChangedBatched(samples);

            // Bump the time-series version so open Data tools re-query and pick up the new rows.
            foreach (var (variable, charId, value) in samples)
                _cacheService.AddPoint(variable, charId, value);
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Inventory, $"[ItemCountHistoryService] Flush error: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _flushRunning, 0);
        }
    }

    private List<ulong> GetRetainerIds(ulong characterId)
    {
        if (characterId == _retainerIdsCharacterId &&
            DateTime.UtcNow - _retainerIdsRefreshedAt < RetainerIdCacheExpiry)
        {
            return _retainerIds;
        }

        var ids = _dbService.GetRetainerIdsForCharacter(characterId);
        _retainerIds = ids;
        _retainerIdsCharacterId = characterId;
        _retainerIdsRefreshedAt = DateTime.UtcNow;
        return ids;
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _observations.ObservationCommitted -= OnObservationCommitted;

        // Persist anything still pending so counts sampled just before shutdown aren't lost.
        uint[] items;
        lock (_pendingLock)
        {
            items = new uint[_pendingItems.Count];
            _pendingItems.CopyTo(items);
            _pendingItems.Clear();
        }
        if (items.Length > 0 && _seededCharacterId != 0)
            Flush(_seededCharacterId, items);

        LogService.Debug(LogCategory.Inventory, "[ItemCountHistoryService] Disposed");
    }
}
