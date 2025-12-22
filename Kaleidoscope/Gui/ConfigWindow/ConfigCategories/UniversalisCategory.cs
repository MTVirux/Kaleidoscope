using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Universalis Integration settings category in the config window.
/// </summary>
public class UniversalisCategory
{
    private readonly ConfigurationService _configService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly UniversalisWebSocketService? _webSocketService;

    private Configuration Config => _configService.Config;

    private static readonly string[] ScopeNames = { "World", "Data Center", "Region" };
    private static readonly string[] RetentionTypeNames = { "By Time (Days)", "By Size (MB)" };

    // World selection widget for price tracking scope
    private WorldSelectionWidget? _worldSelectionWidget;
    private bool _worldSelectionWidgetInitialized = false;

    // Common region names for the dropdown (for override settings)
    private static readonly string[] RegionNames = 
    {
        "",
        "Japan",
        "North-America",
        "Europe",
        "Oceania"
    };

    private bool _resetConfirmPending = false;

    public UniversalisCategory(
        ConfigurationService configService, 
        PriceTrackingService? priceTrackingService = null,
        UniversalisWebSocketService? webSocketService = null)
    {
        _configService = configService;
        _priceTrackingService = priceTrackingService;
        _webSocketService = webSocketService;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Universalis Integration");
        ImGui.Separator();
        ImGui.Spacing();

        // WebSocket Status at the top
        DrawWebSocketStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Query Settings in collapsible header
        if (ImGui.CollapsingHeader("Query Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            DrawQuerySettings();
            ImGui.Unindent();
        }

        // Override Settings in collapsible header
        if (ImGui.CollapsingHeader("Override Settings"))
        {
            ImGui.Indent();
            DrawOverrideSettings();
            ImGui.Unindent();
        }

        // Price Tracking Section in collapsible header
        if (ImGui.CollapsingHeader("Price Tracking (WebSocket)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            DrawPriceTrackingSection();
            ImGui.Unindent();
        }

        // Data Management in collapsible header
        if (ImGui.CollapsingHeader("Data Management"))
        {
            ImGui.Indent();
            DrawDataManagement();
            ImGui.Unindent();
        }
    }

    private void DrawWebSocketStatus()
    {
        ImGui.TextUnformatted("WebSocket Status:");
        ImGui.SameLine();

        if (_webSocketService != null && _webSocketService.IsConnected)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.9f, 0.3f, 1f), "Connected");
        }
        else if (_priceTrackingService != null && Config.PriceTracking.Enabled)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), "Connecting...");
        }
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f), "Disconnected");
        }

        // Show additional status info
        if (_priceTrackingService != null && _priceTrackingService.IsInitialized)
        {
            var worldData = _priceTrackingService.WorldData;
            if (worldData != null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({worldData.Worlds?.Count ?? 0} worlds, {worldData.DataCenters?.Count ?? 0} DCs)");
            }
            
            var marketable = _priceTrackingService.MarketableItems;
            if (marketable != null)
            {
                ImGui.TextDisabled($"{marketable.Count} marketable items loaded");
            }
        }
    }

    private void DrawQuerySettings()
    {
        ImGui.TextWrapped("Configure how market data is fetched from Universalis. " +
            "A wider scope (Region > Data Center > World) will show more listings but may include items from other worlds.");
        ImGui.Spacing();

        // Scope selector
        var scopeIndex = (int)Config.UniversalisQueryScope;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Query Scope##UniversalisScope", ref scopeIndex, ScopeNames, ScopeNames.Length))
        {
            Config.UniversalisQueryScope = (UniversalisScope)scopeIndex;
            _configService.Save();
        }
        ImGui.SameLine();
        HelpMarker("World: Only your current world\nData Center: All worlds in your DC\nRegion: All worlds in your region");

        ImGui.Spacing();

        // Display current effective scope
        ImGui.TextUnformatted("Effective Query Target:");
        ImGui.SameLine();
        var effectiveTarget = GetEffectiveTarget();
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.8f, 1.0f, 1.0f), effectiveTarget);
    }

    private void DrawOverrideSettings()
    {
        ImGui.TextWrapped("Leave empty to use your current character's location. " +
            "Use these to query a specific world/DC/region regardless of where you are.");
        ImGui.Spacing();

        var worldData = _priceTrackingService?.WorldData;

        // World override - use dropdown if world data available
        if (worldData != null && worldData.Worlds.Count > 0)
        {
            // Convert world name to ID for the selector
            var currentWorldId = worldData.GetWorldId(Config.UniversalisWorldOverride) ?? 0;
            if (WorldSelectionWidget.DrawSingleWorldSelector(worldData, "World Override##UniversalisWorld", ref currentWorldId, 200f, true))
            {
                Config.UniversalisWorldOverride = currentWorldId == 0 ? "" : worldData.GetWorldName(currentWorldId) ?? "";
                _configService.Save();
            }
        }
        else
        {
            // Fallback to text input if data not loaded
            var worldOverride = Config.UniversalisWorldOverride;
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("World Override##UniversalisWorld", ref worldOverride, 64))
            {
                Config.UniversalisWorldOverride = worldOverride.Trim();
                _configService.Save();
            }
        }
        ImGui.SameLine();
        HelpMarker("e.g., Gilgamesh, Cactuar, Tonberry");

        // Data Center override - use dropdown if world data available
        if (worldData != null && worldData.DataCenters.Count > 0)
        {
            var dcOverride = Config.UniversalisDataCenterOverride;
            if (WorldSelectionWidget.DrawSingleDataCenterSelector(worldData, "Data Center Override##UniversalisDC", ref dcOverride, 200f, true))
            {
                Config.UniversalisDataCenterOverride = dcOverride;
                _configService.Save();
            }
        }
        else
        {
            // Fallback to text input if data not loaded
            var dcOverride = Config.UniversalisDataCenterOverride;
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("Data Center Override##UniversalisDC", ref dcOverride, 64))
            {
                Config.UniversalisDataCenterOverride = dcOverride.Trim();
                _configService.Save();
            }
        }
        ImGui.SameLine();
        HelpMarker("e.g., Aether, Crystal, Primal, Chaos, Light, Elemental, Gaia, Mana, Meteor, Dynamis, Materia");

        // Region override (combo for convenience)
        ImGui.SetNextItemWidth(200);
        var regionOverride = Config.UniversalisRegionOverride;
        var regionIndex = Array.IndexOf(RegionNames, regionOverride);
        if (regionIndex < 0) regionIndex = 0;
        if (ImGui.Combo("Region Override##UniversalisRegion", ref regionIndex, RegionNames, RegionNames.Length))
        {
            Config.UniversalisRegionOverride = RegionNames[regionIndex];
            _configService.Save();
        }
        ImGui.SameLine();
        HelpMarker("Select a region or leave empty to use your character's region");
    }

    private void DrawPriceTrackingSection()
    {
        var settings = Config.PriceTracking;

        ImGui.TextWrapped("Enable real-time price tracking via Universalis WebSocket. " +
            "This allows tracking item prices over time and calculating inventory value.");
        ImGui.Spacing();

        // Enable toggle
        var enabled = settings.Enabled;
        if (ImGui.Checkbox("Enable Price Tracking##PriceTrackingEnabled", ref enabled))
        {
            settings.Enabled = enabled;
            _configService.Save();
            
            // Start/stop the service if available
            if (_priceTrackingService != null)
            {
                _ = _priceTrackingService.SetEnabledAsync(enabled);
            }
        }
        ImGui.SameLine();
        HelpMarker("When enabled, connects to Universalis WebSocket for real-time price updates.");

        if (!settings.Enabled)
        {
            ImGui.TextDisabled("Price tracking is disabled.");
            return;
        }

        ImGui.Spacing();

        // Data Retention sub-section
        ImGui.TextUnformatted("Data Retention");
        ImGui.Spacing();

        var retentionType = (int)settings.RetentionType;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Retention Policy##PriceRetention", ref retentionType, RetentionTypeNames, RetentionTypeNames.Length))
        {
            settings.RetentionType = (PriceRetentionType)retentionType;
            _configService.Save();
        }
        ImGui.SameLine();
        HelpMarker("How to limit stored price data:\n" +
            "By Time: Keep data for N days\n" +
            "By Size: Keep data up to N MB");

        if (settings.RetentionType == PriceRetentionType.ByTime)
        {
            var retentionDays = settings.RetentionDays;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Days to retain##RetentionDays", ref retentionDays, 1, 7))
            {
                settings.RetentionDays = Math.Max(1, retentionDays);
                _configService.Save();
            }
        }
        else
        {
            var retentionMb = settings.RetentionSizeMb;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Max size (MB)##RetentionSize", ref retentionMb, 10, 50))
            {
                settings.RetentionSizeMb = Math.Max(10, retentionMb);
                _configService.Save();
            }
        }

        ImGui.Spacing();

        // Tracking Scope sub-section using WorldSelectionWidget
        ImGui.TextUnformatted("Tracking Scope");
        ImGui.Spacing();

        DrawWorldSelectionWidget(settings);

        ImGui.Spacing();

        var autoFetch = settings.AutoFetchInventoryPrices;
        if (ImGui.Checkbox("Auto-fetch inventory prices on startup##AutoFetch", ref autoFetch))
        {
            settings.AutoFetchInventoryPrices = autoFetch;
            _configService.Save();
        }
        ImGui.SameLine();
        HelpMarker("Automatically fetch prices from API for items in your inventory when the plugin starts.");
    }

    private void DrawWorldSelectionWidget(PriceTrackingSettings settings)
    {
        // Initialize or update the widget if world data is available
        var worldData = _priceTrackingService?.WorldData;
        
        if (worldData == null || worldData.DataCenters.Count == 0)
        {
            ImGui.TextDisabled("World data not yet loaded...");
            return;
        }

        // Create widget if needed
        if (_worldSelectionWidget == null)
        {
            _worldSelectionWidget = new WorldSelectionWidget(worldData, "PriceTrackingScope");
            _worldSelectionWidget.Width = 300f;
        }

        // Initialize widget from settings on first draw
        if (!_worldSelectionWidgetInitialized)
        {
            _worldSelectionWidget.InitializeFrom(
                settings.SelectedRegions,
                settings.SelectedDataCenters,
                settings.SelectedWorldIds);

            // Set the mode based on current scope mode
            _worldSelectionWidget.Mode = settings.ScopeMode switch
            {
                PriceTrackingScopeMode.ByRegion => WorldSelectionMode.Regions,
                PriceTrackingScopeMode.ByDataCenter => WorldSelectionMode.DataCenters,
                PriceTrackingScopeMode.ByWorld => WorldSelectionMode.Worlds,
                _ => WorldSelectionMode.Worlds
            };

            _worldSelectionWidgetInitialized = true;
        }

        // Draw the widget
        if (_worldSelectionWidget.Draw("Track Markets##WorldSelection"))
        {
            // Sync widget selections back to settings
            settings.SelectedRegions.Clear();
            foreach (var r in _worldSelectionWidget.SelectedRegions)
                settings.SelectedRegions.Add(r);

            settings.SelectedDataCenters.Clear();
            foreach (var dc in _worldSelectionWidget.SelectedDataCenters)
                settings.SelectedDataCenters.Add(dc);

            settings.SelectedWorldIds.Clear();
            foreach (var w in _worldSelectionWidget.SelectedWorldIds)
                settings.SelectedWorldIds.Add(w);

            // Update scope mode based on widget mode
            settings.ScopeMode = _worldSelectionWidget.Mode switch
            {
                WorldSelectionMode.Regions => PriceTrackingScopeMode.ByRegion,
                WorldSelectionMode.DataCenters => PriceTrackingScopeMode.ByDataCenter,
                WorldSelectionMode.Worlds => PriceTrackingScopeMode.ByWorld,
                _ => PriceTrackingScopeMode.ByWorld
            };

            _configService.Save();
        }

        // Show warning if nothing selected
        var hasSelection = _worldSelectionWidget.Mode switch
        {
            WorldSelectionMode.Regions => _worldSelectionWidget.SelectedRegions.Count > 0,
            WorldSelectionMode.DataCenters => _worldSelectionWidget.SelectedDataCenters.Count > 0,
            WorldSelectionMode.Worlds => _worldSelectionWidget.SelectedWorldIds.Count > 0,
            _ => false
        };

        if (!hasSelection && settings.ScopeMode != PriceTrackingScopeMode.All)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), "No selection - no data will be tracked!");
        }

        ImGui.SameLine();
        HelpMarker("Select which worlds, data centers, or regions to track prices for.\n" +
            "Use the mode selector inside the dropdown to switch between selection types.");
    }

    private void DrawDataManagement()
    {
        ImGui.TextWrapped("Manage cached Universalis data. This includes item prices, price history, and inventory value snapshots.");
        ImGui.Spacing();

        // Reset button with confirmation
        if (!_resetConfirmPending)
        {
            if (ImGui.Button("Reset Universalis Data##ResetButton"))
            {
                _resetConfirmPending = true;
            }
            ImGui.SameLine();
            HelpMarker("Clears all cached price data, price history, and inventory value history. This cannot be undone.");
        }
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), "Are you sure? This cannot be undone!");
            
            if (ImGui.Button("Yes, Reset All Data##ConfirmReset"))
            {
                if (_priceTrackingService != null)
                {
                    var result = _priceTrackingService.ResetAllData();
                    if (result)
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.9f, 0.3f, 1f), "Data reset successfully!");
                    }
                }
                _resetConfirmPending = false;
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel##CancelReset"))
            {
                _resetConfirmPending = false;
            }
        }
    }

    private string GetEffectiveTarget()
    {
        return Config.UniversalisQueryScope switch
        {
            UniversalisScope.World => string.IsNullOrEmpty(Config.UniversalisWorldOverride)
                ? "(Current character's world)"
                : Config.UniversalisWorldOverride,
            UniversalisScope.DataCenter => string.IsNullOrEmpty(Config.UniversalisDataCenterOverride)
                ? "(Current character's data center)"
                : Config.UniversalisDataCenterOverride,
            UniversalisScope.Region => string.IsNullOrEmpty(Config.UniversalisRegionOverride)
                ? "(Current character's region)"
                : Config.UniversalisRegionOverride,
            _ => "(Unknown)"
        };
    }

    private static void HelpMarker(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
