using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Kaleidoscope.Models.Inventory;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service for caching and tracking inventory contents for players and retainers.
/// Scans inventory containers and persists snapshots to the database for offline access.
/// </summary>
public sealed class InventoryCacheService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly SamplerService _samplerService;
    private readonly IObjectTable _objectTable;
    private readonly IFramework _framework;
    private readonly InventoryChangeService _inventoryChangeService;

    // Player inventory containers to scan
    private static readonly InventoryType[] PlayerInventoryContainers = new[]
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.EquippedItems,
        InventoryType.Crystals,
        InventoryType.Currency,
        InventoryType.KeyItems,
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.ArmorySoulCrystal,
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2,
    };

    // Retainer inventory containers to scan
    private static readonly InventoryType[] RetainerInventoryContainers = new[]
    {
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
        InventoryType.RetainerEquippedItems,
        InventoryType.RetainerCrystals,
        InventoryType.RetainerMarket,
    };

    private ulong _lastCachedRetainerId = 0;
    private DateTime _lastPlayerCacheTime = DateTime.MinValue;
    private readonly TimeSpan _playerCacheInterval = TimeSpan.FromSeconds(30);
    private bool _pendingPlayerCache = false;
    private bool _pendingRetainerCache = false;

    public InventoryCacheService(
        IPluginLog log,
        SamplerService samplerService,
        IObjectTable objectTable,
        IFramework framework,
        InventoryChangeService inventoryChangeService)
    {
        _log = log;
        _samplerService = samplerService;
        _objectTable = objectTable;
        _framework = framework;
        _inventoryChangeService = inventoryChangeService;

        // Subscribe to events
        _framework.Update += OnFrameworkUpdate;
        _inventoryChangeService.OnRetainerInventoryReady += OnRetainerInventoryReady;
        _inventoryChangeService.OnRetainerClosed += OnRetainerClosed;
        _inventoryChangeService.OnValuesChanged += OnValuesChanged;

        _log.Debug("[InventoryCacheService] Initialized");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Process pending cache operations on the main thread
        if (_pendingPlayerCache)
        {
            _pendingPlayerCache = false;
            CachePlayerInventory();
        }

        if (_pendingRetainerCache)
        {
            _pendingRetainerCache = false;
            CacheActiveRetainerInventory();
        }

        // Periodically cache player inventory
        var now = DateTime.UtcNow;
        if (now - _lastPlayerCacheTime >= _playerCacheInterval && GameStateService.PlayerContentId != 0)
        {
            CachePlayerInventory();
        }
    }

    private void OnRetainerInventoryReady()
    {
        _log.Debug("[InventoryCacheService] Retainer inventory ready, scheduling cache");
        _pendingRetainerCache = true;
    }

    private void OnRetainerClosed()
    {
        _log.Debug("[InventoryCacheService] Retainer closed, resetting tracking");
        ResetRetainerCacheTracking();
    }

    private void OnValuesChanged(IReadOnlyDictionary<Models.TrackedDataType, long> changes)
    {
        // When inventory-related values change, schedule a player cache update
        _pendingPlayerCache = true;
    }

    /// <summary>
    /// Scans and caches the current player's inventory.
    /// Call this periodically or when inventory changes are detected.
    /// </summary>
    public unsafe void CachePlayerInventory()
    {
        try
        {
            var characterId = GameStateService.PlayerContentId;
            if (characterId == 0) return;

            var now = DateTime.UtcNow;
            if (now - _lastPlayerCacheTime < _playerCacheInterval) return;

            var im = GameStateService.InventoryManagerInstance();
            if (im == null) return;

            var playerName = GameStateService.LocalPlayerName;
            var localPlayer = _objectTable.LocalPlayer;
            var world = localPlayer?.CurrentWorld.RowId > 0
                ? localPlayer?.CurrentWorld.Value.Name.ToString()
                : null;

            var entry = InventoryCacheEntry.ForPlayer(characterId, playerName, world);
            entry.Gil = im->GetGil();

            // Scan all player inventory containers
            foreach (var containerType in PlayerInventoryContainers)
            {
                ScanContainer(im, containerType, entry.Items);
            }

            // Save to database
            _samplerService.DbService.SaveInventoryCache(entry);
            _lastPlayerCacheTime = now;

            _log.Debug($"[InventoryCacheService] Cached player inventory: {entry.Items.Count} items, {entry.Gil:N0} gil");
        }
        catch (Exception ex)
        {
            _log.Error($"[InventoryCacheService] Failed to cache player inventory: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans and caches the currently active retainer's inventory.
    /// Should be called when a retainer's inventory becomes available.
    /// </summary>
    public unsafe void CacheActiveRetainerInventory()
    {
        try
        {
            if (!GameStateService.IsRetainerActive()) return;

            var characterId = GameStateService.PlayerContentId;
            if (characterId == 0) return;

            var retainerId = GameStateService.GetActiveRetainerId();
            if (retainerId == 0) return;

            // Avoid recaching the same retainer repeatedly
            if (retainerId == _lastCachedRetainerId) return;

            var im = GameStateService.InventoryManagerInstance();
            if (im == null) return;

            var retainerName = GameStateService.GetActiveRetainerName();
            var entry = InventoryCacheEntry.ForRetainer(characterId, retainerId, retainerName);

            // Get retainer gil from RetainerManager
            var rm = GameStateService.RetainerManagerInstance();
            if (rm != null && rm->IsReady)
            {
                var retainer = rm->GetActiveRetainer();
                if (retainer != null)
                {
                    entry.Gil = retainer->Gil;
                }
            }

            // Scan all retainer inventory containers
            foreach (var containerType in RetainerInventoryContainers)
            {
                ScanContainer(im, containerType, entry.Items);
            }

            // Save to database
            _samplerService.DbService.SaveInventoryCache(entry);
            _lastCachedRetainerId = retainerId;

            _log.Debug($"[InventoryCacheService] Cached retainer inventory '{retainerName}': {entry.Items.Count} items, {entry.Gil:N0} gil");
        }
        catch (Exception ex)
        {
            _log.Error($"[InventoryCacheService] Failed to cache retainer inventory: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans all known retainers from RetainerManager cache.
    /// This only updates retainer metadata (names, gil), not inventory items.
    /// Inventory items are only updated when a retainer is actually opened.
    /// </summary>
    public unsafe void CacheRetainerMetadata()
    {
        try
        {
            var characterId = GameStateService.PlayerContentId;
            if (characterId == 0) return;

            var rm = GameStateService.RetainerManagerInstance();
            if (rm == null || !rm->IsReady) return;

            var retainerCount = rm->GetRetainerCount();
            for (uint i = 0; i < retainerCount; i++)
            {
                var retainer = rm->GetRetainerBySortedIndex(i);
                if (retainer == null || !retainer->Available) continue;

                var retainerId = retainer->RetainerId;
                if (retainerId == 0) continue;

                // Check if we already have a cache for this retainer
                var existing = _samplerService.DbService.GetInventoryCache(
                    characterId, 
                    InventorySourceType.Retainer, 
                    retainerId);

                if (existing != null)
                {
                    // Update gil and name if changed
                    if (existing.Gil != retainer->Gil || existing.Name != retainer->NameString)
                    {
                        existing.Gil = retainer->Gil;
                        existing.Name = retainer->NameString;
                        existing.UpdatedAt = DateTime.UtcNow;
                        _samplerService.DbService.SaveInventoryCache(existing);
                    }
                }
                else
                {
                    // Create a placeholder entry (inventory items will be filled when retainer is opened)
                    var entry = InventoryCacheEntry.ForRetainer(characterId, retainerId, retainer->NameString);
                    entry.Gil = retainer->Gil;
                    _samplerService.DbService.SaveInventoryCache(entry);
                    _log.Debug($"[InventoryCacheService] Created placeholder for retainer '{retainer->NameString}'");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[InventoryCacheService] Failed to cache retainer metadata: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans a single inventory container and adds items to the list.
    /// </summary>
    private unsafe void ScanContainer(InventoryManager* im, InventoryType containerType, List<InventoryItemSnapshot> items)
    {
        try
        {
            var container = im->GetInventoryContainer(containerType);
            if (container == null || !container->IsLoaded) return;

            var size = container->GetSize();
            for (int i = 0; i < size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null) continue;
                if (slot->ItemId == 0) continue; // Empty slot

                var flags = slot->Flags;
                var isHq = (flags & InventoryItem.ItemFlags.HighQuality) != 0;
                var isCollectable = (flags & InventoryItem.ItemFlags.Collectable) != 0;

                items.Add(new InventoryItemSnapshot
                {
                    ItemId = slot->ItemId,
                    Quantity = slot->Quantity,
                    IsHq = isHq,
                    IsCollectable = isCollectable,
                    Slot = slot->Slot,
                    ContainerType = (uint)containerType,
                    SpiritbondOrCollectability = slot->SpiritbondOrCollectability,
                    Condition = slot->Condition,
                    GlamourId = slot->GlamourId
                });
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[InventoryCacheService] Failed to scan container {containerType}: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all cached inventories for the current character.
    /// </summary>
    public List<InventoryCacheEntry> GetCurrentCharacterInventories()
    {
        var characterId = GameStateService.PlayerContentId;
        if (characterId == 0) return new List<InventoryCacheEntry>();
        
        return _samplerService.DbService.GetAllInventoryCaches(characterId);
    }

    /// <summary>
    /// Gets all cached inventories across all characters.
    /// </summary>
    public List<InventoryCacheEntry> GetAllInventories()
    {
        return _samplerService.DbService.GetAllInventoryCachesAllCharacters();
    }

    /// <summary>
    /// Gets the total count of a specific item across all caches for the current character.
    /// </summary>
    public long GetTotalItemCount(uint itemId)
    {
        var characterId = GameStateService.PlayerContentId;
        if (characterId == 0) return 0;

        var summary = _samplerService.DbService.GetItemCountSummary(characterId, itemId);
        return summary.TryGetValue(itemId, out var count) ? count : 0;
    }

    /// <summary>
    /// Gets the total count of a specific item across all caches for all characters.
    /// </summary>
    public long GetTotalItemCountAllCharacters(uint itemId)
    {
        var summary = _samplerService.DbService.GetItemCountSummary(null, itemId);
        return summary.TryGetValue(itemId, out var count) ? count : 0;
    }

    /// <summary>
    /// Resets the retainer cache tracking so the next retainer will be re-cached.
    /// </summary>
    public void ResetRetainerCacheTracking()
    {
        _lastCachedRetainerId = 0;
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _inventoryChangeService.OnRetainerInventoryReady -= OnRetainerInventoryReady;
        _inventoryChangeService.OnRetainerClosed -= OnRetainerClosed;
        _inventoryChangeService.OnValuesChanged -= OnValuesChanged;

        _log.Debug("[InventoryCacheService] Disposed");
    }
}
