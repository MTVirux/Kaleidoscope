using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the current size of the inventory memory cache.
/// </summary>
public class CacheSizeTool : ToolComponent
{
    private readonly InventoryCacheService _inventoryCacheService;

    private static readonly Vector4 InfoColor = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ValueColor = new(0.9f, 0.9f, 0.9f, 1f);

    // Cached values to avoid recalculating every frame
    private int _cachedCharacterCount;
    private int _cachedEntryCount;
    private int _cachedItemCount;
    private long _estimatedBytes;
    private DateTime _lastCacheCheck = DateTime.MinValue;
    private readonly TimeSpan _cacheCheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether to show extra details beyond the summary.
    /// </summary>
    public bool ShowDetails { get; set; } = true;

    public CacheSizeTool(InventoryCacheService inventoryCacheService)
    {
        _inventoryCacheService = inventoryCacheService;

        Title = "Cache Size";
        Size = new Vector2(220, 110);
    }

    public override void DrawContent()
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
            var sizeStr = FormatMemorySize(_estimatedBytes);
            ImGui.TextColored(InfoColor, "Size:");
            ImGui.SameLine();
            ImGui.TextColored(ValueColor, $"~{sizeStr}");

            if (ShowDetails)
            {
                ImGui.Spacing();

                // Character and entry count
                ImGui.TextColored(InfoColor, $"  {_cachedCharacterCount} characters, {_cachedEntryCount} entries");

                // Item count
                ImGui.TextColored(InfoColor, $"  {_cachedItemCount:N0} items cached");
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

    private static string FormatMemorySize(long bytes)
    {
        if (bytes < 0)
            return "Unknown";

        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";

        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F2} MB";

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    public override bool HasSettings => true;
    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        var showDetails = ShowDetails;
        if (ImGui.Checkbox("Show Details", ref showDetails))
        {
            ShowDetails = showDetails;
            NotifyToolSettingsChanged();
        }
    }
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["ShowDetails"] = ShowDetails
        };
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        ShowDetails = GetSetting(settings, "ShowDetails", ShowDetails);
    }
}
