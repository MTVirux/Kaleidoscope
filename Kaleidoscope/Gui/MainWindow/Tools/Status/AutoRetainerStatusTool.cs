using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the AutoRetainer IPC connection status.
/// </summary>
public class AutoRetainerStatusTool : ToolComponent
{
    private readonly AutoRetainerIpcService? _autoRetainerIpc;

    private static readonly Vector4 ConnectedColor = new(0.2f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 DisconnectedColor = new(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 DisabledColor = new(0.5f, 0.5f, 0.5f, 1f);

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

    public override void DrawContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            if (_autoRetainerIpc == null)
            {
                DrawStatusIndicator(false, "Not Available", "Service not initialized");
                ImGui.PopTextWrapPos();
                return;
            }

            var isAvailable = _autoRetainerIpc.IsAvailable;

            if (isAvailable)
            {
                DrawStatusIndicator(true, "Connected", "AutoRetainer plugin detected");
            }
            else
            {
                DrawStatusIndicator(false, "Not Connected", "AutoRetainer plugin not detected");
                if (ShowDetails)
                    ImGui.TextColored(DisabledColor, "Install AutoRetainer for multi-char data");
            }

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[AutoRetainerStatusTool] Draw error: {ex.Message}");
        }
    }

    private void DrawStatusIndicator(bool isConnected, string status, string tooltip, Vector4? overrideColor = null)
    {
        var color = overrideColor ?? (isConnected ? ConnectedColor : DisconnectedColor);
        var icon = isConnected ? "●" : "○";
        
        ImGui.TextColored(color, icon);
        ImGui.SameLine();
        ImGui.TextUnformatted(status);
        
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(tooltip))
        {
            ImGui.SetTooltip(tooltip);
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
        }
    }
}
