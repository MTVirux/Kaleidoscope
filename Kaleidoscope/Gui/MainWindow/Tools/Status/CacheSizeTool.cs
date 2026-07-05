using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services.Inventory;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

[ToolType("CacheSize", "Cache Size", "Utility", "Shows the current size of the inventory memory cache")]
public sealed class CacheSizeTool : StatusToolBase
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
    private int _statsUpdateInFlight;

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

            var now = DateTime.UtcNow;
            if (now - _lastCacheCheck >= _cacheCheckInterval)
            {
                _lastCacheCheck = now;
                QueueCacheStatsUpdate();
            }

            var sizeStr = FormatUtils.FormatByteSize(_estimatedBytes);
            ImGui.TextColored(UiColors.Info, "Size:");
            ImGui.SameLine();
            ImGui.TextColored(UiColors.Value, $"~{sizeStr}");

            if (ShowDetails)
            {
                ImGui.Spacing();

                ImGui.TextColored(UiColors.Info, $"  {_cachedCharacterCount} characters, {_cachedEntryCount} entries");

                ImGui.TextColored(UiColors.Info, $"  {_cachedItemCount:N0} items cached");
            }

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    private void QueueCacheStatsUpdate()
    {
        // GetAllInventories() is a full resources-table scan under the DB read lock; keep it off
        // the render thread. Single-flight: skip if the previous update is still running.
        if (Interlocked.CompareExchange(ref _statsUpdateInFlight, 1, 0) != 0)
            return;

        Task.Run(() =>
        {
            try
            {
                UpdateCacheStats();
            }
            finally
            {
                Interlocked.Exchange(ref _statsUpdateInFlight, 0);
            }
        });
    }

    private void UpdateCacheStats()
    {
        try
        {
            var allInventories = _inventoryCacheService.GetAllInventories();

            _cachedCharacterCount = allInventories.Select(e => e.CharacterId).Distinct().Count();
            _cachedEntryCount = allInventories.Count;
            _cachedItemCount = allInventories.Sum(e => e.Items.Count);

            // Estimate memory usage:
            // - InventoryCacheEntry: ~100 bytes base (strings, timestamps, etc.)
            // - InventoryItemSnapshot: ~24 bytes each (readonly record struct, stored inline in List<T> array)
            // - Dictionary overhead per character: ~50 bytes
            _estimatedBytes = (_cachedCharacterCount * 50L) +
                              (_cachedEntryCount * 100L) +
                              (_cachedItemCount * 24L);
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to update cache stats: {ex.Message}");
            _cachedCharacterCount = 0;
            _cachedEntryCount = 0;
            _cachedItemCount = 0;
            _estimatedBytes = 0;
        }
    }

}
