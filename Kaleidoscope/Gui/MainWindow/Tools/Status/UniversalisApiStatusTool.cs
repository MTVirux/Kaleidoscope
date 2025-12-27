using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the Universalis REST API status and configuration.
/// </summary>
public class UniversalisApiStatusTool : StatusToolBase
{
    public override string ToolName => "Universalis API Status";
    
    private readonly ConfigurationService _configService;
    private readonly PriceTrackingService? _priceTrackingService;

    public UniversalisApiStatusTool(
        ConfigurationService configService,
        PriceTrackingService? priceTrackingService = null)
    {
        _configService = configService;
        _priceTrackingService = priceTrackingService;

        Title = "Universalis API Status";
        Size = new Vector2(250, 120);
    }

    public override void RenderToolContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            // The REST API is always available if we have the service
            UiColors.DrawStatusIndicator(true, "Available", "REST API endpoint");

            if (ShowDetails)
            {
                // Show configured query scope
                var scope = _configService.Config.UniversalisQueryScope;
                var scopeText = scope switch
                {
                    UniversalisScope.World => "World",
                    UniversalisScope.DataCenter => "Data Center",
                    UniversalisScope.Region => "Region",
                    _ => "Unknown"
                };
                ImGui.TextUnformatted($"  Query scope: {scopeText}");

                // Show price tracking mode if available
                if (_priceTrackingService != null)
                {
                    var worldData = _priceTrackingService.WorldData;
                    if (worldData != null)
                    {
                        ImGui.TextUnformatted($"  Worlds loaded: {worldData.Worlds?.Count ?? 0}");
                        ImGui.TextUnformatted($"  Data centers: {worldData.DataCenters?.Count ?? 0}");
                    }
                }
            }

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[UniversalisApiStatusTool] Draw error: {ex.Message}");
        }
    }

}
