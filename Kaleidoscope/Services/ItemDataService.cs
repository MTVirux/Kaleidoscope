using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service for looking up item data from game Excel sheets.
/// Provides item name resolution and caching for efficient lookups.
/// </summary>
public sealed class ItemDataService : IService
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;

    // Cache for item names to avoid repeated Excel lookups
    private readonly ConcurrentDictionary<uint, string> _itemNameCache = new();

    public ItemDataService(IDataManager dataManager, IPluginLog log)
    {
        _dataManager = dataManager;
        _log = log;

        _log.Debug("[ItemDataService] Initialized");
    }

    /// <summary>
    /// Gets the name of an item by its ID.
    /// </summary>
    /// <param name="itemId">The item ID to look up.</param>
    /// <returns>The item name, or a fallback string with the ID if not found.</returns>
    public string GetItemName(uint itemId)
    {
        // Check cache first
        if (_itemNameCache.TryGetValue(itemId, out var cachedName))
        {
            return cachedName;
        }

        // Look up in Excel sheet
        try
        {
            var itemSheet = _dataManager.GetExcelSheet<Item>();
            if (itemSheet != null && itemSheet.TryGetRow(itemId, out var item))
            {
                var name = item.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _itemNameCache[itemId] = name;
                    return name;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[ItemDataService] Error looking up item {itemId}: {ex.Message}");
        }

        // Fallback
        var fallback = $"Item #{itemId}";
        _itemNameCache[itemId] = fallback;
        return fallback;
    }

    /// <summary>
    /// Gets the name of an item by its ID (int overload).
    /// </summary>
    /// <param name="itemId">The item ID to look up.</param>
    /// <returns>The item name, or a fallback string with the ID if not found.</returns>
    public string GetItemName(int itemId)
    {
        return GetItemName((uint)itemId);
    }

    /// <summary>
    /// Tries to get item data by ID.
    /// </summary>
    /// <param name="itemId">The item ID to look up.</param>
    /// <param name="item">The item data if found.</param>
    /// <returns>True if the item was found, false otherwise.</returns>
    public bool TryGetItem(uint itemId, out Item item)
    {
        try
        {
            var itemSheet = _dataManager.GetExcelSheet<Item>();
            if (itemSheet != null && itemSheet.TryGetRow(itemId, out item))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[ItemDataService] Error looking up item {itemId}: {ex.Message}");
        }

        item = default;
        return false;
    }

    /// <summary>
    /// Clears the item name cache.
    /// </summary>
    public void ClearCache()
    {
        _itemNameCache.Clear();
        _log.Debug("[ItemDataService] Cache cleared");
    }
}
