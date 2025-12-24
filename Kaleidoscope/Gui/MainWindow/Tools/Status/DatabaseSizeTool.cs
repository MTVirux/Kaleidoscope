using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the current size of the SQLite database file.
/// </summary>
public class DatabaseSizeTool : ToolComponent
{
    private readonly SamplerService _samplerService;

    private static readonly Vector4 InfoColor = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ValueColor = new(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Vector4 WarningColor = new(0.9f, 0.7f, 0.2f, 1f);
    private static readonly Vector4 ErrorColor = new(0.8f, 0.2f, 0.2f, 1f);

    // Cached values to avoid hitting the file system every frame
    private long _cachedFileSize;
    private DateTime _lastSizeCheck = DateTime.MinValue;
    private readonly TimeSpan _sizeCheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether to show extra details beyond the size.
    /// </summary>
    public bool ShowDetails { get; set; } = true;

    public DatabaseSizeTool(SamplerService samplerService)
    {
        _samplerService = samplerService;

        Title = "Database Size";
        Size = new Vector2(220, 90);
    }

    public override void DrawContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            var dbPath = _samplerService.DbService?.DbPath;

            if (string.IsNullOrEmpty(dbPath))
            {
                ImGui.TextColored(ErrorColor, "Database not available");
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
                ImGui.TextColored(ErrorColor, "Unable to read database");
            }
            else
            {
                var sizeStr = FormatFileSize(_cachedFileSize);
                var color = GetSizeColor(_cachedFileSize);

                ImGui.TextColored(InfoColor, "Size:");
                ImGui.SameLine();
                ImGui.TextColored(color, sizeStr);

                if (ShowDetails)
                {
                    ImGui.Spacing();
                    
                    // Show raw bytes
                    ImGui.TextColored(InfoColor, $"  {_cachedFileSize:N0} bytes");
                    
                    // Show size tier info
                    if (_cachedFileSize > 100 * 1024 * 1024) // > 100 MB
                    {
                        ImGui.TextColored(WarningColor, "  Consider pruning old data");
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

    private static string FormatFileSize(long bytes)
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

    private static Vector4 GetSizeColor(long bytes)
    {
        // Color based on size thresholds
        if (bytes < 10 * 1024 * 1024) // < 10 MB
            return new Vector4(0.2f, 0.8f, 0.2f, 1f); // Green

        if (bytes < 50 * 1024 * 1024) // < 50 MB
            return new Vector4(0.9f, 0.9f, 0.9f, 1f); // White

        if (bytes < 100 * 1024 * 1024) // < 100 MB
            return new Vector4(0.9f, 0.7f, 0.2f, 1f); // Yellow

        return new Vector4(0.8f, 0.4f, 0.2f, 1f); // Orange
    }

    public override bool HasSettings => true;
    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        var showDetails = ShowDetails;
        if (ImGui.Checkbox("Show Details", ref showDetails))
        {
            ShowDetails = showDetails;
        }
    }
}
