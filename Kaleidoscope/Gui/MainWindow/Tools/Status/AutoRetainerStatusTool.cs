using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the AutoRetainer IPC connection status.
/// </summary>
public class AutoRetainerStatusTool : ToolComponent
{
    public override string ToolName => "AutoRetainer Status";
    
    private readonly AutoRetainerIpcService? _autoRetainerIpc;

    /// <summary>
    /// Whether to show extra details beyond the status indicator.
    /// </summary>
    public bool ShowDetails { get; set; } = true;

    public AutoRetainerStatusTool(AutoRetainerIpcService? autoRetainerIpc = null)
    {
        _autoRetainerIpc = autoRetainerIpc;

        Title = "AutoRetainer Status";
        Size = new Vector2(250, 80);
    }

    public override void RenderToolContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            if (_autoRetainerIpc == null)
            {
                UiColors.DrawStatusIndicator(false, "Not Available", "Service not initialized");
                ImGui.PopTextWrapPos();
                return;
            }

            var isAvailable = _autoRetainerIpc.IsAvailable;

            if (isAvailable)
            {
                UiColors.DrawStatusIndicator(true, "Connected", "AutoRetainer plugin detected");
            }
            else
            {
                UiColors.DrawStatusIndicator(false, "Not Connected", "AutoRetainer plugin not detected");
                if (ShowDetails)
                    ImGui.TextColored(UiColors.Disabled, "Install AutoRetainer for multi-char data");
            }

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[AutoRetainerStatusTool] Draw error: {ex.Message}");
        }
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
