using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the Universalis REST API status and configuration.
/// </summary>
public class UniversalisApiStatusTool : ToolComponent
{
    private readonly ConfigurationService _configService;
    private readonly PriceTrackingService? _priceTrackingService;

    private static readonly Vector4 ConnectedColor = new(0.2f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 DisconnectedColor = new(0.8f, 0.2f, 0.2f, 1f);

    /// <summary>
    /// Whether to show extra details beyond the status indicator.
    /// </summary>
    public bool ShowDetails { get; set; } = true;

    public UniversalisApiStatusTool(
        ConfigurationService configService,
        PriceTrackingService? priceTrackingService = null)
    {
        _configService = configService;
        _priceTrackingService = priceTrackingService;

        Title = "Universalis API Status";
        Size = new Vector2(250, 120);
    }

    public override void DrawContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            // The REST API is always available if we have the service
            DrawStatusIndicator(true, "Available", "REST API endpoint");

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
