using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Settings;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// Settings class for DatabaseSizeTool.
/// </summary>
public class DatabaseSizeToolSettings
{
    public bool ShowDetails { get; set; } = true;
}

/// <summary>
/// A tool that displays the current size of the SQLite database file.
/// </summary>
public class DatabaseSizeTool : ToolComponent
{
    public override string ToolName => "Database Size";
    
    private readonly SamplerService _samplerService;

    // Cached values to avoid hitting the file system every frame
    private long _cachedFileSize;
    private DateTime _lastSizeCheck = DateTime.MinValue;
    private readonly TimeSpan _sizeCheckInterval = TimeSpan.FromSeconds(5);
    
    // Settings instance and schema
    private readonly DatabaseSizeToolSettings _settings = new();
    
    private static readonly SettingsSchema<DatabaseSizeToolSettings> Schema = SettingsSchema.For<DatabaseSizeToolSettings>()
        .Checkbox(s => s.ShowDetails, "Show Details", "Show additional details like raw byte count and size warnings", defaultValue: true);

    /// <summary>
    /// Whether to show extra details beyond the size.
    /// </summary>
    public bool ShowDetails
    {
        get => _settings.ShowDetails;
        set => _settings.ShowDetails = value;
    }

    public DatabaseSizeTool(SamplerService samplerService)
    {
        _samplerService = samplerService;

        Title = "Database Size";
        Size = new Vector2(220, 90);
    }

    public override void RenderToolContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            var dbPath = _samplerService.DbService?.DbPath;

            if (string.IsNullOrEmpty(dbPath))
            {
                ImGui.TextColored(UiColors.Error, "Database not available");
                ImGui.PopTextWrapPos();
                return;
            }

            // Update cached size periodically
            var now = DateTime.UtcNow;
            if (now - _lastSizeCheck >= _sizeCheckInterval)
            {
                _cachedFileSize = GetDatabaseFileSize(dbPath);
                _lastSizeCheck = now;
            }

            if (_cachedFileSize < 0)
            {
                ImGui.TextColored(UiColors.Error, "Unable to read database");
            }
            else
            {
                var sizeStr = FormatUtils.FormatByteSize(_cachedFileSize);
                var color = UiColors.GetSizeColor(_cachedFileSize);

                ImGui.TextColored(UiColors.Info, "Size:");
                ImGui.SameLine();
                ImGui.TextColored(color, sizeStr);

                if (ShowDetails)
                {
                    ImGui.Spacing();
                    
                    // Show raw bytes
                    ImGui.TextColored(UiColors.Info, $"  {_cachedFileSize:N0} bytes");
                    
                    // Show size tier info
                    if (_cachedFileSize > 100 * 1024 * 1024) // > 100 MB
                    {
                        ImGui.TextColored(UiColors.Warning, "  Consider pruning old data");
                    }
                }
            }

            if (ImGui.IsWindowHovered() && !string.IsNullOrEmpty(dbPath))
            {
                ImGui.SetTooltip(dbPath);
            }

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DatabaseSizeTool] Draw error: {ex.Message}");
        }
    }

    private static long GetDatabaseFileSize(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath))
                return -1;

            var fileInfo = new FileInfo(dbPath);
            var totalSize = fileInfo.Length;

            // Also include WAL and SHM files if they exist
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";

            if (File.Exists(walPath))
                totalSize += new FileInfo(walPath).Length;

            if (File.Exists(shmPath))
                totalSize += new FileInfo(shmPath).Length;

            return totalSize;
        }
        catch
        {
            return -1;
        }
    }

    public override bool HasSettings => true;
    protected override bool HasToolSettings => true;
    
    protected override object? GetToolSettingsSchema() => Schema;
    
    protected override object? GetToolSettingsObject() => _settings;
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return Schema.ToDictionary(_settings)!;
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        Schema.FromDictionary(_settings, settings);
    }
}
