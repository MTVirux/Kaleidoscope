using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using System.Numerics;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Developer category for displaying cache statistics.
/// Shows details about all in-memory caches used by the plugin.
/// </summary>
public sealed class CachesCategory
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly InventoryCacheService _inventoryCacheService;
    private readonly ListingsService _listingsService;
    private readonly CharacterDataService _characterDataService;

    private static readonly Vector4 HeaderColor = new(0.4f, 0.8f, 1f, 1f);

    public CachesCategory(
        CurrencyTrackerService currencyTrackerService,
        InventoryCacheService inventoryCacheService,
        ListingsService listingsService,
        CharacterDataService characterDataService)
    {
        _currencyTrackerService = currencyTrackerService;
        _inventoryCacheService = inventoryCacheService;
        _listingsService = listingsService;
        _characterDataService = characterDataService;
    }

    public void Draw()
    {
        ImGui.TextColored(HeaderColor, "Cache Statistics");
        ImGui.Separator();
        ImGui.TextDisabled("Real-time view of in-memory caches. These caches improve performance by reducing database and API calls.");
        ImGui.Spacing();

        DrawTimeSeriesCache();
        ImGui.Spacing();
        
        DrawInventoryCache();
        ImGui.Spacing();
        
        DrawListingsCache();
        ImGui.Spacing();
        
        DrawCharacterDataCache();
        ImGui.Spacing();
        
        DrawCacheActions();
    }

    private void DrawTimeSeriesCache()
    {
        if (ImGui.CollapsingHeader("Time Series Cache", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            
            var stats = _currencyTrackerService.CacheService.GetStatistics();
            
            ImGuiHelpers.DrawStatRow("Cached Series", stats.SeriesCount.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Total Data Points", stats.TotalPoints.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Character Names", stats.CharacterCount.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Cache Hits", stats.CacheHits.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Cache Misses", stats.CacheMisses.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Hit Rate", $"{stats.HitRate:P1}");
            
            // Estimate memory usage (each point ~20 bytes, series ~100 bytes, character ~200 bytes)
            var estimatedBytes = stats.TotalPoints * 20 + stats.SeriesCount * 100 + stats.CharacterCount * 200;
            ImGuiHelpers.DrawStatRow("Est. Memory", FormatUtils.FormatByteSize(estimatedBytes), ImGuiHelpers.StatDimColor);
            
            ImGui.Unindent();
        }
    }

    private void DrawInventoryCache()
    {
        if (ImGui.CollapsingHeader("Inventory Cache", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            
            var stats = _inventoryCacheService.GetCacheStatistics();
            
            ImGuiHelpers.DrawStatRow("Cached Characters", stats.CachedCharacterCount.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Inventory Entries", stats.CachedEntryCount.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Total Items", stats.CachedItemCount.ToString("N0"));
            ImGuiHelpers.DrawStatRow("All-Characters Cache", stats.AllCharactersCacheCount.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Pending Samples", stats.PendingSamplesCount.ToString("N0"));
            ImGuiHelpers.DrawStatRow("Est. Memory", FormatUtils.FormatByteSize(stats.EstimatedMemoryBytes), ImGuiHelpers.StatDimColor);
            
            ImGui.Unindent();
        }
    }

    private void DrawListingsCache()
    {
        if (ImGui.CollapsingHeader("Listings Cache (Universalis)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            
            var cacheCount = _listingsService.CacheCount;
            var isInitialized = _listingsService.IsInitialized;
            
            ImGuiHelpers.DrawStatRow("Status", isInitialized ? "Initialized" : "Initializing...", 
                isInitialized ? new Vector4(0.5f, 1f, 0.5f, 1f) : new Vector4(1f, 0.8f, 0.2f, 1f));
            ImGuiHelpers.DrawStatRow("Cached Listings", cacheCount.ToString("N0"));
            
            // Each listing entry is roughly 200 bytes (item ID, world ID, listings array, timestamps)
            ImGuiHelpers.DrawStatRow("Est. Memory", FormatUtils.FormatByteSize(cacheCount * 200), ImGuiHelpers.StatDimColor);
            
            ImGui.Unindent();
        }
    }

    private void DrawCharacterDataCache()
    {
        if (ImGui.CollapsingHeader("Character Data Cache", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            
            var characters = _characterDataService.GetCharacters(includeAllCharactersOption: false, sortByFavorites: false);
            var characterCount = characters.Count;
            
            ImGuiHelpers.DrawStatRow("Cached Characters", characterCount.ToString("N0"));
            
            // Each CharacterInfo is roughly 150 bytes (strings, IDs)
            ImGuiHelpers.DrawStatRow("Est. Memory", FormatUtils.FormatByteSize(characterCount * 150), ImGuiHelpers.StatDimColor);
            
            ImGui.Unindent();
        }
    }

    private void DrawCacheActions()
    {
        ImGui.Separator();
        ImGui.TextColored(HeaderColor, "Cache Actions");
        ImGui.Spacing();
        
        if (ImGui.Button("Clear Time Series Cache"))
        {
            _currencyTrackerService.CacheService.ClearAll();
            LogService.Info("[CachesCategory] Cleared time series cache");
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Clears all cached time series data. Will reload from DB on next access.");
        
        if (ImGui.Button("Invalidate Inventory Cache"))
        {
            _inventoryCacheService.InvalidateAllCaches();
            LogService.Info("[CachesCategory] Invalidated inventory cache");
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Marks inventory cache as dirty. Will reload from DB on next access.");
        
        if (ImGui.Button("Refresh Character Data"))
        {
            _characterDataService.MarkDirty();
            LogService.Info("[CachesCategory] Marked character data cache as dirty");
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Forces character data to refresh on next access.");
    }
}
