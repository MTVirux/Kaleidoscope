using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the Universalis WebSocket connection status.
/// </summary>
public class UniversalisWebSocketStatusTool : ToolComponent
{
    public override string ToolName => "Universalis WebSocket Status";
    
    private readonly UniversalisWebSocketService? _webSocketService;
    private readonly ConfigurationService _configService;

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

    public override void RenderToolContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            if (_webSocketService == null)
            {
                UiColors.DrawStatusIndicator(false, "Not Available", "Service not initialized");
                ImGui.PopTextWrapPos();
                return;
            }

            var priceTrackingEnabled = _configService.Config.PriceTracking.Enabled;
            var isConnected = _webSocketService.IsConnected;

            if (!priceTrackingEnabled)
            {
                UiColors.DrawStatusIndicator(false, "Disabled", "Price tracking is disabled in settings", UiColors.Disabled);
                if (ShowDetails)
                    ImGui.TextColored(UiColors.Disabled, "Enable in Settings > Universalis to connect");
            }
            else if (isConnected)
            {
                UiColors.DrawStatusIndicator(true, "Connected", "Real-time price feed active");
                
                if (ShowDetails)
                {
                    var feedCount = _webSocketService.LiveFeedCount;
                    ImGui.TextUnformatted($"  Feed entries: {feedCount:N0}");
                }
            }
            else
            {
                UiColors.DrawStatusIndicator(false, "Disconnected", "Attempting to connect...");
                if (ShowDetails)
                    ImGui.TextColored(UiColors.Warning, "Will auto-reconnect when available");
            }

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[UniversalisWebSocketStatusTool] Draw error: {ex.Message}");
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
