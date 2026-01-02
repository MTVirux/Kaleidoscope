using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the Universalis WebSocket connection status.
/// </summary>
public class UniversalisWebSocketStatusTool : StatusToolBase
{
    public override string ToolName => "Universalis WebSocket Status";
    
    private readonly UniversalisWebSocketService? _webSocketService;
    private readonly ConfigurationService _configService;

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
            LogDebug($"Draw error: {ex.Message}");
        }
    }

}
