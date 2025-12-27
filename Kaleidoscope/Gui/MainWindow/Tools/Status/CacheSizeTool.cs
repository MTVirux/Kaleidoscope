using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the current size of the inventory memory cache.
/// </summary>
public class CacheSizeTool : StatusToolBase
{
    public override string ToolName => "Cache Size";
    
    private readonly InventoryCacheService _inventoryCacheService;

    // Cached values to avoid recalculating every frame
    private int _cachedCharacterCount;
    private int _cachedEntryCount;
    private int _cachedItemCount;
    private long _estimatedBytes;
    private DateTime _lastCacheCheck = DateTime.MinValue;
    private readonly TimeSpan _cacheCheckInterval = TimeSpan.FromSeconds(2);

    public CacheSizeTool(InventoryCacheService inventoryCacheService)
    {
        _inventoryCacheService = inventoryCacheService;

        Title = "Cache Size";
        Size = new Vector2(220, 110);
    }

    public override void RenderToolContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            // Update cached values periodically
            var now = DateTime.UtcNow;
            if (now - _lastCacheCheck >= _cacheCheckInterval)
            {
                UpdateCacheStats();
                _lastCacheCheck = now;
            }

            // Size line (primary info)
            var sizeStr = FormatUtils.FormatByteSize(_estimatedBytes);
            ImGui.TextColored(UiColors.Info, "Size:");
            ImGui.SameLine();
            ImGui.TextColored(UiColors.Value, $"~{sizeStr}");

            if (ShowDetails)
            {
                ImGui.Spacing();

                // Character and entry count
                ImGui.TextColored(UiColors.Info, $"  {_cachedCharacterCount} characters, {_cachedEntryCount} entries");

                // Item count
                ImGui.TextColored(UiColors.Info, $"  {_cachedItemCount:N0} items cached");
            }

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CacheSizeTool] Draw error: {ex.Message}");
        }
    }

    private void UpdateCacheStats()
    {
        try
        {
            // Get all cached inventories to calculate stats
            var allInventories = _inventoryCacheService.GetAllInventories();

            _cachedCharacterCount = allInventories.Select(e => e.CharacterId).Distinct().Count();
            _cachedEntryCount = allInventories.Count;
            _cachedItemCount = allInventories.Sum(e => e.Items.Count);

            // Estimate memory usage:
            // - InventoryCacheEntry: ~100 bytes base (strings, timestamps, etc.)
            // - InventoryItemSnapshot: ~60 bytes each (uint, long, flags, etc.)
            // - Dictionary overhead per character: ~50 bytes
            _estimatedBytes = (_cachedCharacterCount * 50L) +
                              (_cachedEntryCount * 100L) +
                              (_cachedItemCount * 60L);
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CacheSizeTool] Failed to update cache stats: {ex.Message}");
            _cachedCharacterCount = 0;
            _cachedEntryCount = 0;
            _cachedItemCount = 0;
            _estimatedBytes = 0;
        }
    }

}
