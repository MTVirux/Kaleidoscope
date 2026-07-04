using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// ItemSalesTrackingTool partial class containing data fetching and world scope resolution logic.
/// </summary>
public sealed partial class ItemSalesTrackingTool
{
    private void OnItemSelectionChanged(IReadOnlySet<uint> selectedIds)
    {
        // Find newly selected items that need fetching
        var newItems = selectedIds.Except(_loadedItemIds).ToList();

        if (newItems.Count > 0)
        {
            _ = FetchHistoryForItemsAsync(newItems);
        }

        // Remove cached data for deselected items
        var removedItems = _loadedItemIds.Except(selectedIds).ToList();
        foreach (var itemId in removedItems)
        {
            _salesDataCache.Remove(itemId);
            _loadedItemIds.Remove(itemId);
        }

        _seriesDataDirty = true;
    }

    private async Task FetchAllHistoryAsync()
    {
        var selectedIds = _itemCombo.SelectedItemIds.ToList();
        if (selectedIds.Count == 0) return;

        _loadedItemIds.Clear();
        _salesDataCache.Clear();

        await FetchHistoryForItemsAsync(selectedIds);
    }

    private async Task FetchHistoryForItemsAsync(IEnumerable<uint> itemIds)
    {
        _isLoading = true;
        _errorMessage = null;

        try
        {
            var scopes = GetQueryScopes();

            if (scopes.Count == 0)
            {
                _errorMessage = "Cannot determine query scope - check settings.";
                return;
            }

            var itemIdList = itemIds.ToList();

            // Batch items into chunks of 100 (Universalis API limit)
            const int batchSize = 100;
            var batches = new List<List<uint>>();
            for (var i = 0; i < itemIdList.Count; i += batchSize)
            {
                batches.Add(itemIdList.Skip(i).Take(batchSize).ToList());
            }

            foreach (var batch in batches)
            {
                await FetchBatchAsync(batch, scopes);
            }

            _lastFetchTime = DateTime.UtcNow;
            _seriesDataDirty = true;

            var totalDataPoints = _salesDataCache.Values.Sum(v => v.Count);
            LogDebug($"FetchHistoryForItemsAsync complete: {_salesDataCache.Count} items cached, {totalDataPoints} total data points");
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error fetching data: {ex.Message}";
            LogService.Error(LogCategory.UI, $"[ItemSalesTrackingTool] FetchHistoryForItemsAsync error: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task FetchBatchAsync(List<uint> batch, List<string> scopes)
    {
        // Initialize sales data accumulators for this batch
        var batchSalesData = batch.ToDictionary(id => id, _ => new List<(DateTime, float)>());

        // Query each scope and combine results
        foreach (var scope in scopes)
        {
            try
            {
                // Determine if this scope is a single world (no filtering needed since all results are from that world)
                var isSingleWorldScope = IsSingleWorldScope(scope);

                var historyBatch = await _universalisService.GetHistoryBatchAsync(scope, batch, Settings.MaxHistoryEntries);

                if (historyBatch == null)
                    continue;

                foreach (var kvp in historyBatch)
                {
                    var itemId = kvp.Key;
                    var history = kvp.Value;

                    if (history?.Entries == null || history.Entries.Count == 0)
                        continue;

                    // Skip world filtering if we queried a single world - all entries are already from that world
                    // (Universalis doesn't include WorldId in responses when querying a single world)
                    var filteredEntries = isSingleWorldScope
                        ? history.Entries
                        : FilterEntriesByWorldScope(history.Entries);

                    var salesData = filteredEntries
                        .Select(e => (e.SaleDateTime, (float)e.PricePerUnit));

                    if (batchSalesData.TryGetValue(itemId, out var existingData))
                    {
                        existingData.AddRange(salesData);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error fetching batch history for scope {scope}: {ex.Message}");
            }
        }

        // Process and cache results for this batch
        foreach (var kvp in batchSalesData)
        {
            var itemId = kvp.Key;
            var allSalesData = kvp.Value;

            // Sort by timestamp and deduplicate
            var deduplicatedData = allSalesData
                .GroupBy(s => s.Item1)
                .Select(g => g.First())
                .OrderBy(s => s.Item1)
                .ToList();

            _salesDataCache[itemId] = deduplicatedData;
            _loadedItemIds.Add(itemId);
        }
    }

    private List<HistorySale> FilterEntriesByWorldScope(List<HistorySale> entries)
    {
        if (Settings.ScopeMode == WorldSelectionMode.Worlds && Settings.SelectedWorldIds.Count == 0)
            return entries;

        var effectiveWorldIds = GetEffectiveWorldIds();
        if (effectiveWorldIds.Count == 0)
            return entries;

        // Entries without a WorldId cannot be matched against the scope, so they are excluded.
        return entries
            .Where(e => e.WorldId.HasValue && effectiveWorldIds.Contains(e.WorldId.Value))
            .ToList();
    }

    /// <summary>
    /// Determines if the given scope is a single world (as opposed to a DC or region).
    /// When querying a single world, Universalis returns entries without WorldId populated,
    /// so we should skip world filtering for such queries.
    /// </summary>
    private bool IsSingleWorldScope(string scope)
    {
        var worldData = _priceTrackingService.WorldData;
        if (worldData == null)
            return false;
        
        // Check if the scope matches a known world name
        var isWorld = worldData.Worlds.Any(w => 
            string.Equals(w.Name, scope, StringComparison.OrdinalIgnoreCase));
        
        return isWorld;
    }

    private HashSet<int> GetEffectiveWorldIds()
    {
        if (_worldSelectionWidget != null)
            return _worldSelectionWidget.GetEffectiveWorldIds();

        // Widget not created yet (e.g. a WebSocket sale arrives before the first draw): resolve from
        // persisted settings via a transient widget so the world-ID expansion lives in one place.
        var worldData = _priceTrackingService.WorldData;
        if (worldData == null)
        {
            return Settings.ScopeMode == WorldSelectionMode.Worlds
                ? new HashSet<int>(Settings.SelectedWorldIds)
                : new HashSet<int>();
        }

        var widget = new WorldSelectionWidget(worldData) { Mode = Settings.ScopeMode };
        widget.InitializeFrom(Settings.SelectedRegions, Settings.SelectedDataCenters, Settings.SelectedWorldIds);
        return widget.GetEffectiveWorldIds();
    }

    /// <summary>
    /// Returns all scopes that need to be queried to cover all selected worlds/DCs/regions.
    /// </summary>
    private List<string> GetQueryScopes()
    {
        var worldData = _priceTrackingService.WorldData;
        var result = new List<string>();
        
        if (worldData == null)
        {
            var fallback = _universalisService.GetConfiguredScope();
            if (fallback != null)
                result.Add(fallback);
            return result;
        }

        switch (Settings.ScopeMode)
        {
            case WorldSelectionMode.Regions when Settings.SelectedRegions.Count > 0:
                result.AddRange(Settings.SelectedRegions);
                break;

            case WorldSelectionMode.DataCenters when Settings.SelectedDataCenters.Count > 0:
                AddDataCenterScopes(result, worldData);
                break;

            case WorldSelectionMode.Worlds when Settings.SelectedWorldIds.Count > 0:
                AddWorldScopes(result, worldData);
                break;

            default:
                var fallbackScope = _universalisService.GetConfiguredScope();
                if (fallbackScope != null)
                    result.Add(fallbackScope);
                break;
        }
        
        if (result.Count == 0)
        {
            var fallbackScope = _universalisService.GetConfiguredScope();
            if (fallbackScope != null)
                result.Add(fallbackScope);
        }
        
        return result.Distinct().ToList();
    }

    private void AddDataCenterScopes(List<string> result, UniversalisWorldData worldData)
    {
        var dcsByRegion = new Dictionary<string, List<string>>();
        foreach (var dcName in Settings.SelectedDataCenters)
        {
            var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
            var region = dc?.Region ?? "Unknown";
            if (!dcsByRegion.ContainsKey(region))
                dcsByRegion[region] = new List<string>();
            dcsByRegion[region].Add(dcName);
        }
        
        foreach (var kvp in dcsByRegion)
        {
            var region = kvp.Key;
            var dcsInRegion = kvp.Value;
            var allDcsInRegion = worldData.DataCenters
                .Where(dc => dc.Region == region)
                .Select(dc => dc.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
            
            if (allDcsInRegion.All(dc => dcsInRegion.Contains(dc!)) && allDcsInRegion.Count > 0)
            {
                result.Add(region);
            }
            else
            {
                result.AddRange(dcsInRegion);
            }
        }
    }

    private void AddWorldScopes(List<string> result, UniversalisWorldData worldData)
    {
        var worldsByDc = new Dictionary<string, List<int>>();
        var dcToRegion = new Dictionary<string, string>();
        
        foreach (var worldId in Settings.SelectedWorldIds)
        {
            var dc = worldData.GetDataCenterForWorldId(worldId);
            if (dc != null && !string.IsNullOrEmpty(dc.Name))
            {
                if (!worldsByDc.ContainsKey(dc.Name))
                    worldsByDc[dc.Name] = new List<int>();
                worldsByDc[dc.Name].Add(worldId);
                if (!string.IsNullOrEmpty(dc.Region))
                    dcToRegion[dc.Name] = dc.Region;
            }
        }
        
        var dcsByRegionFromWorlds = new Dictionary<string, List<string>>();
        foreach (var kvp in worldsByDc)
        {
            var dcName = kvp.Key;
            var region = dcToRegion.GetValueOrDefault(dcName, "Unknown");
            if (!dcsByRegionFromWorlds.ContainsKey(region))
                dcsByRegionFromWorlds[region] = new List<string>();
            dcsByRegionFromWorlds[region].Add(dcName);
        }
        
        foreach (var regionKvp in dcsByRegionFromWorlds)
        {
            var region = regionKvp.Key;
            var dcsInThisRegion = regionKvp.Value;
            
            var allDcsInRegion = worldData.DataCenters
                .Where(dc => dc.Region == region)
                .ToList();
            
            var allWorldsInRegionSelected = allDcsInRegion.All(dc =>
                dc.Worlds == null || dc.Worlds.All(wid => Settings.SelectedWorldIds.Contains((int)wid)));
            
            if (allWorldsInRegionSelected && allDcsInRegion.Count > 0)
            {
                result.Add(region);
            }
            else
            {
                foreach (var dcName in dcsInThisRegion)
                {
                    var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
                    if (dc?.Worlds != null)
                    {
                        var allWorldsInDcSelected = dc.Worlds.All(wid => Settings.SelectedWorldIds.Contains((int)wid));
                        if (allWorldsInDcSelected)
                        {
                            result.Add(dcName);
                        }
                        else
                        {
                            foreach (var wid in worldsByDc[dcName])
                            {
                                var worldName = worldData.GetWorldName(wid);
                                if (!string.IsNullOrEmpty(worldName))
                                    result.Add(worldName);
                            }
                        }
                    }
                }
            }
        }
    }

    private void OnPriceUpdate(PriceFeedEntry entry)
    {
        if (entry.EventType != "Sale")
            return;

        if (!_itemCombo.SelectedItemIds.Contains((uint)entry.ItemId))
            return;

        var effectiveWorldIds = GetEffectiveWorldIds();
        if (effectiveWorldIds.Count > 0 && !effectiveWorldIds.Contains(entry.WorldId))
            return;

        var itemId = (uint)entry.ItemId;
        if (!_salesDataCache.TryGetValue(itemId, out var salesData))
        {
            salesData = new List<(DateTime, float)>();
            _salesDataCache[itemId] = salesData;
        }

        salesData.Add((entry.ReceivedAt, entry.PricePerUnit));

        while (salesData.Count > Settings.MaxHistoryEntries)
        {
            salesData.RemoveAt(0);
        }

        _seriesDataDirty = true;
    }

    /// <summary>
    /// Returns a human-readable description of the current query scope for tooltips.
    /// </summary>
    private string GetCurrentScopeDescription()
    {
        var scopes = GetQueryScopes();
        if (scopes.Count == 0)
            return "None (check settings)";

        return Settings.ScopeMode switch
        {
            WorldSelectionMode.Regions when Settings.SelectedRegions.Count > 0 
                => string.Join(", ", Settings.SelectedRegions),
            WorldSelectionMode.DataCenters when Settings.SelectedDataCenters.Count > 0 
                => string.Join(", ", Settings.SelectedDataCenters),
            WorldSelectionMode.Worlds when Settings.SelectedWorldIds.Count > 0 
                => FormatSelectedWorlds(),
            _ => scopes.Count == 1 ? scopes[0] : string.Join(", ", scopes)
        };
    }

    private string FormatSelectedWorlds()
    {
        var worldData = _priceTrackingService.WorldData;
        if (worldData == null)
            return $"{Settings.SelectedWorldIds.Count} worlds";

        var worldNames = Settings.SelectedWorldIds
            .Select(id => worldData.GetWorldName(id))
            .Where(name => !string.IsNullOrEmpty(name))
            .Take(3)
            .ToList();

        if (Settings.SelectedWorldIds.Count > 3)
            return string.Join(", ", worldNames) + $" +{Settings.SelectedWorldIds.Count - 3} more";

        return string.Join(", ", worldNames);
    }
}
