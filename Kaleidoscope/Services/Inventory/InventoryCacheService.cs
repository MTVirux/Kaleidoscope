using Kaleidoscope.Models.Inventory;
using Kaleidoscope.Services.Database;
using Kaleidoscope.Services.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Inventory;

/// <summary>
/// Read API for inventory data. Reads route through ResourceStore (in-memory) and
/// KaleidoscopeDbService.Resources.Reads (DB-backed).
/// </summary>
public sealed class InventoryCacheService : IDisposable, IRequiredService
{
    private readonly KaleidoscopeDbService _dbService;
    private readonly ResourceStore _resourceStore;

    public InventoryCacheService(KaleidoscopeDbService dbService, ResourceStore resourceStore)
    {
        _dbService = dbService;
        _resourceStore = resourceStore;
        LogService.Debug(LogCategory.Inventory, "[InventoryCacheService] Initialized");
    }

    public List<InventoryCacheEntry> GetAllInventories() => _dbService.GetAllInventoryCachesFromResources();

    public List<InventoryCacheEntry> GetInventoriesForCharacter(ulong characterId)
        => characterId == 0 ? new List<InventoryCacheEntry>() : _dbService.GetAllInventoryCachesFromResources(characterId);

    public List<InventoryCacheEntry> GetCurrentCharacterInventories() => GetInventoriesForCharacter(GameStateService.PlayerContentId);

    public long GetTotalItemCount(uint itemId)
    {
        var characterId = GameStateService.PlayerContentId;
        if (characterId == 0) return 0;
        var summary = _dbService.GetItemCountSummaryFromResources(characterId, itemId);
        return summary.TryGetValue(itemId, out var v) ? v : 0;
    }

    public long GetTotalItemCountAllCharacters(uint itemId) => _resourceStore.GetAggregate(itemId);

    public InventoryCacheStatistics GetCacheStatistics()
    {
        var snapshot = _resourceStore.Snapshot();
        var characterCount = snapshot.Select(r => r.Key.OwnerId).Distinct().Count();
        var entryCount = snapshot.Select(r => (r.Key.OwnerId, r.Key.OwnerKind)).Distinct().Count();
        var itemCount = snapshot.Count;

        return new InventoryCacheStatistics
        {
            CachedCharacterCount = characterCount,
            CachedEntryCount = entryCount,
            CachedItemCount = itemCount,
            AllCharactersCacheCount = entryCount,
            PendingSamplesCount = 0,
            EstimatedMemoryBytes = (characterCount * 50L) + (entryCount * 100L) + (itemCount * 64L),
        };
    }

    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPendingSamples(string prefix, string suffix)
        => new Dictionary<string, List<(ulong, DateTime, long)>>();

    public void Dispose()
    {
        LogService.Debug(LogCategory.Inventory, "[InventoryCacheService] Disposed");
    }
}

public readonly struct InventoryCacheStatistics
{
    public int CachedCharacterCount { get; init; }
    public int CachedEntryCount { get; init; }
    public int CachedItemCount { get; init; }
    public int AllCharactersCacheCount { get; init; }
    public int PendingSamplesCount { get; init; }
    public long EstimatedMemoryBytes { get; init; }
}
