using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the Universalis WebSocket connection status.
/// </summary>
public class UniversalisWebSocketStatusTool : ToolComponent
{
    private readonly UniversalisWebSocketService? _webSocketService;
    private readonly ConfigurationService _configService;

    private static readonly Vector4 ConnectedColor = new(0.2f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 DisconnectedColor = new(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 DisabledColor = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 WarningColor = new(0.9f, 0.7f, 0.2f, 1f);

    /// <summary>
    /// Whether to show extra details beyond the status indicator.
    /// </summary>
    public bool ShowDetails { get; set; } = true;

    public UniversalisWebSocketStatusTool(
        ConfigurationService configService,
        UniversalisWebSocketService? webSocketService = null)
    {
        _configService = configService;
        _webSocketService = webSocketService;

        Title = "WebSocket Status";
        Size = new Vector2(250, 100);
    }

    public override void DrawContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            if (_webSocketService == null)
            {
                DrawStatusIndicator(false, "Not Available", "Service not initialized");
                ImGui.PopTextWrapPos();
                return;
            }

            var priceTrackingEnabled = _configService.Config.PriceTracking.Enabled;
            var isConnected = _webSocketService.IsConnected;

            if (!priceTrackingEnabled)
            {
                DrawStatusIndicator(false, "Disabled", "Price tracking is disabled in settings", DisabledColor);
                if (ShowDetails)
                    ImGui.TextColored(DisabledColor, "Enable in Settings > Universalis to connect");
            }
            else if (isConnected)
            {
                DrawStatusIndicator(true, "Connected", "Real-time price feed active");
                
                if (ShowDetails)
                {
                    var feedCount = _webSocketService.LiveFeedCount;
                    ImGui.TextUnformatted($"  Feed entries: {feedCount:N0}");
                }
            }
            else
            {
                DrawStatusIndicator(false, "Disconnected", "Attempting to connect...");
                if (ShowDetails)
                    ImGui.TextColored(WarningColor, "Will auto-reconnect when available");
            }

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[UniversalisWebSocketStatusTool] Draw error: {ex.Message}");
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
