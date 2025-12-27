using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Kaleidoscope.Models.Inventory;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service for caching and tracking inventory contents for players and retainers.
/// Scans inventory containers and persists snapshots to the database for offline access.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optimization:</b> Inventory data for characters who are not logged in cannot change.
/// This service maintains an in-memory cache that avoids repeated database reads for offline
/// characters. Only the currently logged-in character's cache is invalidated when inventory
/// changes are detected.
/// </para>
/// </remarks>
public sealed class InventoryCacheService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly SamplerService _samplerService;
    private readonly IObjectTable _objectTable;
    private readonly IFramework _framework;
    private readonly InventoryChangeService _inventoryChangeService;
    private readonly IClientState _clientState;
    private readonly ConfigurationService _configService;
    
    // In-memory cache for inventory data - avoids repeated DB reads for offline characters
    // Key: characterId, Value: list of inventory cache entries for that character
    private readonly ConcurrentDictionary<ulong, List<InventoryCacheEntry>> _inventoryMemoryCache = new();
    
    // Track when the full "all characters" cache was last loaded
    private List<InventoryCacheEntry>? _allCharactersCache;
    private bool _allCharactersCacheDirty = true;

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

    // Debounce cache for item time series data (player, retainer totals, and per-retainer)
    // Key: (variableName, characterId), Value: (value, timestamp)
    // Data is flushed to DB on logout or plugin unload, not on each sample interval
    private readonly ConcurrentDictionary<(string VariableName, ulong CharacterId), (long Value, DateTime Timestamp)> _pendingSamples = new();

    public InventoryCacheService(
        IPluginLog log,
        SamplerService samplerService,
        IObjectTable objectTable,
        IFramework framework,
        InventoryChangeService inventoryChangeService,
        IClientState clientState,
        ConfigurationService configService)
    {
        _log = log;
        _samplerService = samplerService;
        _objectTable = objectTable;
        _framework = framework;
        _inventoryChangeService = inventoryChangeService;
        _configService = configService;
        _clientState = clientState;

        // Subscribe to events
        _framework.Update += OnFrameworkUpdate;
        _inventoryChangeService.OnRetainerInventoryReady += OnRetainerInventoryReady;
        _inventoryChangeService.OnRetainerClosed += OnRetainerClosed;
        _inventoryChangeService.OnValuesChanged += OnValuesChanged;
        
        // Subscribe to login/logout events to invalidate memory cache for the new character
        _clientState.Login += OnLogin;
        _clientState.Logout += OnLogout;

        _log.Debug("[InventoryCacheService] Initialized");
    }
    
    /// <summary>
    /// Called when a character logs in. Invalidates the memory cache for that character
    /// since their inventory may have changed while we weren't tracking (e.g., retainer ventures).
    /// </summary>
    private void OnLogin()
    {
        var characterId = GameStateService.PlayerContentId;
        if (characterId != 0)
        {
            InvalidateCharacterCache(characterId);
            _log.Debug($"[InventoryCacheService] Character logged in, invalidated cache for {characterId}");
        }
    }
    
    /// <summary>
    /// Called when a character logs out. Marks all-characters cache as dirty.
    /// </summary>
    private void OnLogout(int type, int code)
    {
        _allCharactersCacheDirty = true;
        
        // Flush pending item samples on logout
        FlushPendingSamples("logout");
    }
    
    /// <summary>
    /// Invalidates the in-memory cache for a specific character.
    /// Call this when the character's inventory may have changed.
    /// </summary>
    public void InvalidateCharacterCache(ulong characterId)
    {
        _inventoryMemoryCache.TryRemove(characterId, out _);
        _allCharactersCacheDirty = true;
    }
    
    /// <summary>
    /// Invalidates all in-memory caches. Use sparingly.
    /// </summary>
    public void InvalidateAllCaches()
    {
        _inventoryMemoryCache.Clear();
        _allCharactersCache = null;
        _allCharactersCacheDirty = true;
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
        // Also invalidate the memory cache for the current character
        var characterId = GameStateService.PlayerContentId;
        if (characterId != 0)
        {
            InvalidateCharacterCache(characterId);
        }
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
            
            // Sample tracked items to time-series for historical graphing
            SampleTrackedItems(characterId, entry.Items);
            
            // Invalidate memory cache since DB was updated
            InvalidateCharacterCache(characterId);

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
            
            // Invalidate memory cache since DB was updated
            InvalidateCharacterCache(characterId);
            
            // Re-sample tracked items now that retainer data has been updated
            // Get player inventory cache to combine with new retainer data
            var playerCache = _samplerService.DbService.GetInventoryCache(
                characterId, InventorySourceType.Player, 0);
            if (playerCache != null)
            {
                SampleTrackedItems(characterId, playerCache.Items);
            }

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
    /// Gets all cached inventories for a specific character.
    /// Uses in-memory cache for efficiency - offline characters' data is static.
    /// </summary>
    public List<InventoryCacheEntry> GetInventoriesForCharacter(ulong characterId)
    {
        if (characterId == 0) return new List<InventoryCacheEntry>();
        
        // Check memory cache first
        if (_inventoryMemoryCache.TryGetValue(characterId, out var cached))
        {
            return cached;
        }
        
        // Cache miss - load from database
        var entries = _samplerService.DbService.GetAllInventoryCaches(characterId);
        
        // Store in memory cache (this data is static for offline characters)
        _inventoryMemoryCache[characterId] = entries;
        
        return entries;
    }
    
    /// <summary>
    /// Gets all cached inventories for the current character.
    /// Uses in-memory cache for efficiency.
    /// </summary>
    public List<InventoryCacheEntry> GetCurrentCharacterInventories()
    {
        var characterId = GameStateService.PlayerContentId;
        return GetInventoriesForCharacter(characterId);
    }

    /// <summary>
    /// Gets all cached inventories across all characters.
    /// Uses in-memory cache for efficiency - offline characters' data is static.
    /// </summary>
    public List<InventoryCacheEntry> GetAllInventories()
    {
        // If cache is valid, return it
        if (!_allCharactersCacheDirty && _allCharactersCache != null)
        {
            return _allCharactersCache;
        }
        
        // Cache miss or dirty - reload from database
        _allCharactersCache = _samplerService.DbService.GetAllInventoryCachesAllCharacters();
        _allCharactersCacheDirty = false;
        
        // Also populate per-character cache for individual lookups
        foreach (var group in _allCharactersCache.GroupBy(e => e.CharacterId))
        {
            _inventoryMemoryCache[group.Key] = group.ToList();
        }
        
        return _allCharactersCache;
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

    /// <summary>
    /// Samples tracked item quantities to the time-series database for historical graphing.
    /// Stores player inventory and retainer inventory separately for toggleable display.
    /// Player items stored as "Item_{itemId}", retainer totals as "ItemRetainer_{itemId}",
    /// and per-retainer data as "ItemRetainerX_{retainerId}_{itemId}".
    /// Only samples items that are in the ItemsWithHistoricalTracking set.
    /// </summary>
    /// <param name="characterId">The character ID to associate with the samples.</param>
    /// <param name="playerItems">The player's inventory items to check against tracked items.</param>
    private void SampleTrackedItems(ulong characterId, List<InventoryItemSnapshot> playerItems)
    {
        try
        {
            // Get the global set of items that have historical tracking enabled
            var itemsWithTracking = _configService.Config.ItemsWithHistoricalTracking;
            if (itemsWithTracking.Count == 0)
                return;

            // Get the list of tracked item IDs from all configured sources
            var trackedItems = new HashSet<uint>();
            
            // Check ItemGraph for tracked items that also have historical tracking enabled
            var graphItems = _configService.Config.ItemGraph?.Series?
                .Where(s => !s.IsCurrency && itemsWithTracking.Contains(s.Id))
                .Select(s => s.Id);
            
            if (graphItems != null)
            {
                foreach (var id in graphItems)
                    trackedItems.Add(id);
            }

            // Also check ItemTable for tracked items that have historical tracking enabled
            var tableItems = _configService.Config.ItemTable?.Columns?
                .Where(c => !c.IsCurrency && itemsWithTracking.Contains(c.Id))
                .Select(c => c.Id);
            
            if (tableItems != null)
            {
                foreach (var id in tableItems)
                    trackedItems.Add(id);
            }
            
            // Also check layout-stored DataTool instances for tracked items with historical tracking enabled
            foreach (var layout in _configService.Config.Layouts)
            {
                foreach (var tool in layout.Tools)
                {
                    // Check if this is a DataTool with stored columns
                    if (tool.ToolSettings.TryGetValue("Columns", out var columnsObj) && columnsObj is Newtonsoft.Json.Linq.JArray columnsArray)
                    {
                        foreach (var columnToken in columnsArray)
                        {
                            try
                            {
                                var isCurrency = columnToken["IsCurrency"]?.ToObject<bool>() ?? false;
                                var id = columnToken["Id"]?.ToObject<uint>() ?? 0;
                                
                                if (!isCurrency && id > 0 && itemsWithTracking.Contains(id))
                                {
                                    trackedItems.Add(id);
                                }
                            }
                            catch
                            {
                                // Skip malformed column entries
                            }
                        }
                    }
                }
            }
            
            if (trackedItems.Count == 0)
                return;

            // Gather all retainer caches for this character (with retainer info)
            var retainerCaches = _samplerService.DbService.GetAllInventoryCaches(characterId)
                .Where(c => c.SourceType == InventorySourceType.Retainer)
                .ToList();

            // Calculate quantities for each tracked item (player and retainers separately)
            foreach (var itemId in trackedItems)
            {
                // Player inventory count
                var playerQuantity = playerItems
                    .Where(i => i.ItemId == itemId)
                    .Sum(i => (long)i.Quantity);
                
                // Queue player inventory to cache (debounced)
                var playerVariableName = $"Item_{itemId}";
                _pendingSamples[(playerVariableName, characterId)] = (playerQuantity, DateTime.UtcNow);
                
                // Store retainer total (sum across all retainers)
                long totalRetainerQuantity = 0;
                foreach (var retainerCache in retainerCaches)
                {
                    var retainerQuantity = retainerCache.Items
                        .Where(i => i.ItemId == itemId)
                        .Sum(i => (long)i.Quantity);
                    
                    totalRetainerQuantity += retainerQuantity;
                    
                    // Queue per-retainer data to cache (debounced - only if retainer has a valid ID)
                    if (retainerCache.RetainerId != 0)
                    {
                        var perRetainerVariableName = $"ItemRetainerX_{retainerCache.RetainerId}_{itemId}";
                        _pendingSamples[(perRetainerVariableName, characterId)] = (retainerQuantity, DateTime.UtcNow);
                    }
                }
                
                // Queue retainer total to cache (for backward compatibility and when breakdown is disabled)
                var retainerVariableName = $"ItemRetainer_{itemId}";
                _pendingSamples[(retainerVariableName, characterId)] = (totalRetainerQuantity, DateTime.UtcNow);
            }

            if (trackedItems.Count > 0)
            {
                _log.Debug($"[InventoryCacheService] Sampled {trackedItems.Count} tracked items (player + {retainerCaches.Count} retainers) for character {characterId}");
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[InventoryCacheService] Failed to sample tracked items: {ex.Message}");
        }
    }

    /// <summary>
    /// Flushes all pending item samples to the database.
    /// Called on logout or plugin dispose.
    /// </summary>
    /// <param name="reason">The reason for flushing (for logging).</param>
    private void FlushPendingSamples(string reason)
    {
        if (_pendingSamples.IsEmpty)
            return;
        
        var count = 0;
        var keys = _pendingSamples.Keys.ToList();
        
        foreach (var key in keys)
        {
            if (_pendingSamples.TryRemove(key, out var sample))
            {
                _samplerService.DbService.SaveSampleIfChanged(key.VariableName, key.CharacterId, sample.Value);
                count++;
            }
        }
        
        if (count > 0)
        {
            _log.Debug($"[InventoryCacheService] Flushed {count} pending item samples ({reason})");
        }
    }

    /// <summary>
    /// Gets the variable name used for storing item time-series data.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The variable name in format "Item_{itemId}".</returns>
    public static string GetItemVariableName(uint itemId) => $"Item_{itemId}";
    
    /// <summary>
    /// Gets the variable name used for storing retainer item time-series data.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The variable name in format "ItemRetainer_{itemId}".</returns>
    public static string GetRetainerItemVariableName(uint itemId) => $"ItemRetainer_{itemId}";

    /// <summary>
    /// Gets pending (not yet flushed) item samples that match the given prefix and suffix.
    /// Used for real-time display before data is flushed to the database.
    /// </summary>
    /// <param name="prefix">The variable name prefix (e.g., "Item_", "ItemRetainer_", "ItemRetainerX_").</param>
    /// <param name="suffix">The variable name suffix (e.g., "_12345" for item ID, or empty string).</param>
    /// <returns>Dictionary of variable name to list of (characterId, timestamp, value) tuples.</returns>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPendingSamples(string prefix, string suffix)
    {
        var result = new Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>>();
        
        foreach (var kvp in _pendingSamples)
        {
            var variableName = kvp.Key.VariableName;
            if (variableName.StartsWith(prefix) && (string.IsNullOrEmpty(suffix) || variableName.EndsWith(suffix)))
            {
                var characterId = kvp.Key.CharacterId;
                var (value, timestamp) = kvp.Value;
                
                if (!result.TryGetValue(variableName, out var list))
                {
                    list = new List<(ulong, DateTime, long)>();
                    result[variableName] = list;
                }
                
                list.Add((characterId, timestamp, value));
            }
        }
        
        return result;
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _inventoryChangeService.OnRetainerInventoryReady -= OnRetainerInventoryReady;
        _inventoryChangeService.OnRetainerClosed -= OnRetainerClosed;
        _inventoryChangeService.OnValuesChanged -= OnValuesChanged;
        _clientState.Login -= OnLogin;
        _clientState.Logout -= OnLogout;
        
        // Flush pending item samples before disposing
        FlushPendingSamples("dispose");
        
        // Clear memory caches
        _inventoryMemoryCache.Clear();
        _allCharactersCache = null;
        _pendingSamples.Clear();

        _log.Debug("[InventoryCacheService] Disposed");
    }
}
