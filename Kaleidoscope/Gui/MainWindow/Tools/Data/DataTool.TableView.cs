using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// DataTool partial class containing table view rendering and data population logic.
/// </summary>
public partial class DataTool
{
    private void DrawTableView()
    {
        using (ProfilerService.BeginStaticChildScope("TableView"))
        {
            // Auto-refresh every 2s
            var shouldAutoRefresh = (DateTime.UtcNow - _lastTableRefresh).TotalSeconds > 2.0;
            
            if (_pendingTableRefresh || shouldAutoRefresh)
            {
                using (ProfilerService.BeginStaticChildScope("RefreshTableData"))
                {
                    RefreshTableData();
                }
            }
            
            using (ProfilerService.BeginStaticChildScope("DrawTable"))
            {
                _tableWidget.Draw(_cachedTableData, Settings);
            }
        }
    }
    
    private void RefreshTableData()
    {
        try
        {
            var settings = Settings;
            var allColumns = settings.Columns;
            
            // Apply special grouping filter to get visible columns
            List<ItemColumnConfig> columns;
            using (ProfilerService.BeginStaticChildScope("ApplyGroupingFilter"))
            {
                columns = SpecialGroupingHelper.ApplySpecialGroupingFilter(allColumns, settings.SpecialGrouping).ToList();
            }
            
            if (columns.Count == 0)
            {
                _cachedTableData = new PreparedItemTableData
                {
                    Rows = Array.Empty<ItemTableCharacterRow>(),
                    Columns = columns
                };
                _lastTableRefresh = DateTime.UtcNow;
                _pendingTableRefresh = false;
                return;
            }
            
            // Get all character names with disambiguation (from cache, no DB access)
            IReadOnlyDictionary<ulong, string?> characterNames;
            IReadOnlyDictionary<ulong, string> disambiguatedNames;
            using (ProfilerService.BeginStaticChildScope("GetCharacterNames"))
            {
                characterNames = CharacterDataCache.GetAllCharacterNamesDict();
                disambiguatedNames = CharacterDataCache.GetDisambiguatedNames(characterNames.Keys);
            }
            var rows = new Dictionary<ulong, ItemTableCharacterRow>();
            
            // Get world data for DC/Region lookups (from PriceTrackingService)
            var worldData = _priceTrackingService?.WorldData;
            
            // Get character world info from AutoRetainer (maps CID to world name)
            var characterWorlds = new Dictionary<ulong, string>();
            if (_autoRetainerService != null && _autoRetainerService.IsAvailable)
            {
                var arData = _autoRetainerService.GetAllCharacterData();
                foreach (var (_, world, _, cid) in arData)
                {
                    if (!string.IsNullOrEmpty(world))
                    {
                        characterWorlds[cid] = world;
                    }
                }
            }
            
            // Get character filter (if using multi-select)
            HashSet<ulong>? allowedCharacters = null;
            if (settings.UseCharacterFilter && settings.SelectedCharacterIds.Count > 0)
            {
                allowedCharacters = settings.SelectedCharacterIds.ToHashSet();
            }
            
            // Initialize rows for all known characters (filtered if applicable)
            foreach (var (charId, name) in characterNames)
            {
                // Skip characters not in the allowed set (if filtering is enabled)
                if (allowedCharacters != null && !allowedCharacters.Contains(charId))
                    continue;
                
                var displayName = disambiguatedNames.TryGetValue(charId, out var formatted) 
                    ? formatted : name ?? $"CID:{charId}";
                
                // Get world info for this character
                var charWorldName = characterWorlds.TryGetValue(charId, out var w) ? w : string.Empty;
                var dcName = !string.IsNullOrEmpty(charWorldName) ? worldData?.GetDataCenterForWorld(charWorldName)?.Name ?? string.Empty : string.Empty;
                var regionName = !string.IsNullOrEmpty(charWorldName) ? worldData?.GetRegionForWorld(charWorldName) ?? string.Empty : string.Empty;
                
                rows[charId] = new ItemTableCharacterRow
                {
                    CharacterId = charId,
                    Name = displayName,
                    WorldName = charWorldName,
                    DataCenterName = dcName,
                    RegionName = regionName,
                    ItemCounts = new Dictionary<uint, long>()
                };
            }
            
            // Fetch inventories once for all item columns (cache-first, avoids per-column DB calls)
            List<Kaleidoscope.Models.Inventory.InventoryCacheEntry>? allInventories = null;
            var hasItemColumns = columns.Any(c => !c.IsCurrency);
            if (hasItemColumns && _inventoryCacheService != null)
            {
                using (ProfilerService.BeginStaticChildScope("GetAllInventories"))
                {
                    allInventories = _inventoryCacheService.GetAllInventories();
                }
            }
            
            // Populate data for each column
            using (ProfilerService.BeginStaticChildScope("PopulateColumns"))
            {
                foreach (var column in columns)
                {
                    if (column.IsCurrency)
                    {
                        PopulateCurrencyData(column, rows);
                    }
                    else
                    {
                        PopulateItemData(column, rows, settings.IncludeRetainers, settings.ShowRetainerBreakdown, allInventories);
                    }
                }
            }
            
            // Apply gil merging if enabled
            if (settings.SpecialGrouping.AllGilEnabled && settings.SpecialGrouping.MergeGilCurrencies)
            {
                ApplyGilMerging(rows);
            }
            
            // Sort rows
            List<ItemTableCharacterRow> sortedRows;
            using (ProfilerService.BeginStaticChildScope("SortRows"))
            {
                sortedRows = CharacterSortHelper.SortByCharacter(
                    rows.Values,
                    _configService,
                    _autoRetainerService,
                    r => r.CharacterId,
                    r => r.Name).ToList();
            }
            
            _cachedTableData = new PreparedItemTableData
            {
                Rows = sortedRows,
                Columns = columns
            };
            
            _lastTableRefresh = DateTime.UtcNow;
            _pendingTableRefresh = false;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTool] RefreshTableData error: {ex.Message}");
        }
    }
    
    private void PopulateCurrencyData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        using (ProfilerService.BeginStaticChildScope("PopulateCurrency"))
        {
            try
            {
                var dataType = (TrackedDataType)column.Id;
                var variableName = dataType.ToString();
                
                using (ProfilerService.BeginStaticChildScope("CacheGetLatestValues"))
                {
                    // Cache-first: get latest values from TimeSeriesCacheService
                    var latestValues = CacheService.GetLatestValuesForVariable(variableName);
                
                    foreach (var (charId, value) in latestValues)
                    {
                        if (rows.TryGetValue(charId, out var row))
                        {
                            row.ItemCounts[column.Id] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[DataTool] PopulateCurrencyData error: {ex.Message}");
            }
        }
    }
    
    private void PopulateItemData(
        ItemColumnConfig column, 
        Dictionary<ulong, ItemTableCharacterRow> rows, 
        bool includeRetainers, 
        bool showRetainerBreakdown,
        List<Kaleidoscope.Models.Inventory.InventoryCacheEntry>? allInventories)
    {
        using (ProfilerService.BeginStaticChildScope("PopulateItem"))
        {
            try
            {
                if (allInventories == null) return;
                
                foreach (var cache in allInventories)
                {
                    if (!rows.TryGetValue(cache.CharacterId, out var row))
                        continue;
                    
                    var count = cache.Items
                        .Where(i => i.ItemId == column.Id)
                        .Sum(i => (long)i.Quantity);
                    
                    // Initialize ItemCounts if needed
                    if (!row.ItemCounts.ContainsKey(column.Id))
                        row.ItemCounts[column.Id] = 0;
                    
                    if (cache.SourceType == Kaleidoscope.Models.Inventory.InventorySourceType.Player)
                    {
                        // Always add player inventory to total
                        row.ItemCounts[column.Id] += count;
                        
                        // If showing breakdown, also track player-only counts
                        if (showRetainerBreakdown)
                        {
                            row.PlayerItemCounts ??= new Dictionary<uint, long>();
                            if (!row.PlayerItemCounts.ContainsKey(column.Id))
                                row.PlayerItemCounts[column.Id] = 0;
                            row.PlayerItemCounts[column.Id] += count;
                        }
                    }
                    else if (cache.SourceType == Kaleidoscope.Models.Inventory.InventorySourceType.Retainer)
                    {
                        // Add retainer inventory to total if includeRetainers is enabled
                        if (includeRetainers)
                        {
                            row.ItemCounts[column.Id] += count;
                        }
                        
                        // If showing breakdown, track per-retainer counts
                        if (showRetainerBreakdown && count > 0)
                        {
                            var retainerKey = (cache.RetainerId, cache.Name ?? $"Retainer {cache.RetainerId}");
                            row.RetainerBreakdown ??= new Dictionary<(ulong, string), Dictionary<uint, long>>();
                            
                            if (!row.RetainerBreakdown.TryGetValue(retainerKey, out var retainerCounts))
                            {
                                retainerCounts = new Dictionary<uint, long>();
                                row.RetainerBreakdown[retainerKey] = retainerCounts;
                            }
                            
                            if (!retainerCounts.ContainsKey(column.Id))
                                retainerCounts[column.Id] = 0;
                            retainerCounts[column.Id] += count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[DataTool] PopulateItemData error: {ex.Message}");
            }
        }
    }
    
    private void ApplyGilMerging(Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        var gilId = (uint)TrackedDataType.Gil;
        var fcGilId = (uint)TrackedDataType.FreeCompanyGil;
        var retainerGilId = (uint)TrackedDataType.RetainerGil;
        
        foreach (var row in rows.Values)
        {
            long totalGil = 0;
            
            if (row.ItemCounts.TryGetValue(gilId, out var gil))
                totalGil += gil;
            if (row.ItemCounts.TryGetValue(fcGilId, out var fcGil))
                totalGil += fcGil;
            if (row.ItemCounts.TryGetValue(retainerGilId, out var retainerGil))
                totalGil += retainerGil;
            
            row.ItemCounts[gilId] = totalGil;
        }
    }
}
