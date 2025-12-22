using Dalamud.Bindings.ImGui;
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

    private Configuration Config => _configService.Config;

    private static readonly string[] ScopeNames = { "World", "Data Center", "Region" };
    private static readonly string[] RetentionTypeNames = { "By Time (Days)", "By Size (MB)" };
    private static readonly string[] PriceScopeModeNames = { "All", "By Region", "By Data Center", "By World" };

    // Common region names for the dropdown
    private static readonly string[] RegionNames = 
    {
        "",
        "Japan",
        "North-America",
        "Europe",
        "Oceania"
    };

    public UniversalisCategory(ConfigurationService configService, PriceTrackingService? priceTrackingService = null)
    {
        _configService = configService;
        _priceTrackingService = priceTrackingService;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Universalis Integration");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped("Configure how market data is fetched from Universalis. " +
            "A wider scope (Region > Data Center > World) will show more listings but may include items from other worlds.");
        ImGui.Spacing();
        ImGui.Spacing();

        // Scope selector
        ImGui.TextUnformatted("Query Scope");
        var scopeIndex = (int)Config.UniversalisQueryScope;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("##UniversalisScope", ref scopeIndex, ScopeNames, ScopeNames.Length))
        {
            Config.UniversalisQueryScope = (UniversalisScope)scopeIndex;
            _configService.Save();
        }
        ImGui.SameLine();
        HelpMarker("World: Only your current world\nData Center: All worlds in your DC\nRegion: All worlds in your region");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Override Settings");
        ImGui.TextWrapped("Leave empty to use your current character's location. " +
            "Use these to query a specific world/DC/region regardless of where you are.");
        ImGui.Spacing();

        // World override
        var worldOverride = Config.UniversalisWorldOverride;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("World Override##UniversalisWorld", ref worldOverride, 64))
        {
            Config.UniversalisWorldOverride = worldOverride.Trim();
            _configService.Save();
        }
        ImGui.SameLine();
        HelpMarker("e.g., Gilgamesh, Cactuar, Tonberry");

        // Data Center override
        var dcOverride = Config.UniversalisDataCenterOverride;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Data Center Override##UniversalisDC", ref dcOverride, 64))
        {
            Config.UniversalisDataCenterOverride = dcOverride.Trim();
            _configService.Save();
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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Display current effective scope
        ImGui.TextUnformatted("Effective Query Target:");
        var effectiveTarget = GetEffectiveTarget();
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.8f, 1.0f, 1.0f), effectiveTarget);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Price Tracking Section
        DrawPriceTrackingSection();
    }

    private void DrawPriceTrackingSection()
    {
        var settings = Config.PriceTracking;

        ImGui.TextUnformatted("Price Tracking (WebSocket)");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped("Enable real-time price tracking via Universalis WebSocket. " +
            "This allows tracking item prices over time and calculating inventory value.");
        ImGui.Spacing();

        // Enable toggle
        var enabled = settings.Enabled;
        if (ImGui.Checkbox("Enable Price Tracking", ref enabled))
        {
            settings.Enabled = enabled;
            _configService.Save();
            
            // Start/stop the service if available
            if (_priceTrackingService != null)
            {
                _ = _priceTrackingService.SetEnabledAsync(enabled);
            }
        }
        HelpMarker("When enabled, connects to Universalis WebSocket for real-time price updates.");

        if (!settings.Enabled)
        {
            ImGui.TextDisabled("Price tracking is disabled.");
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Retention Settings
        ImGui.TextUnformatted("Data Retention");
        ImGui.Spacing();

        var retentionType = (int)settings.RetentionType;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Retention Policy##PriceRetention", ref retentionType, RetentionTypeNames, RetentionTypeNames.Length))
        {
            settings.RetentionType = (PriceRetentionType)retentionType;
            _configService.Save();
        }
        HelpMarker("How to limit stored price data:\n" +
            "By Time: Keep data for N days\n" +
            "By Size: Keep data up to N MB");

        if (settings.RetentionType == PriceRetentionType.ByTime)
        {
            var retentionDays = settings.RetentionDays;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Days to retain", ref retentionDays, 1, 7))
            {
                settings.RetentionDays = Math.Max(1, retentionDays);
                _configService.Save();
            }
        }
        else
        {
            var retentionMb = settings.RetentionSizeMb;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Max size (MB)", ref retentionMb, 10, 50))
            {
                settings.RetentionSizeMb = Math.Max(10, retentionMb);
                _configService.Save();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Scope Settings
        ImGui.TextUnformatted("Tracking Scope");
        ImGui.Spacing();

        var scopeMode = (int)settings.ScopeMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Scope Mode##PriceScope", ref scopeMode, PriceScopeModeNames, PriceScopeModeNames.Length))
        {
            settings.ScopeMode = (PriceTrackingScopeMode)scopeMode;
            _configService.Save();
        }
        HelpMarker("Which worlds/DCs/regions to track prices for:\n" +
            "All: Track all markets\n" +
            "By Region: Select specific regions\n" +
            "By Data Center: Select specific DCs\n" +
            "By World: Select specific worlds");

        // Show selected items based on scope mode
        switch (settings.ScopeMode)
        {
            case PriceTrackingScopeMode.ByRegion:
                ImGui.TextDisabled($"Selected regions: {string.Join(", ", settings.SelectedRegions)}");
                break;
            case PriceTrackingScopeMode.ByDataCenter:
                ImGui.TextDisabled($"Selected DCs: {string.Join(", ", settings.SelectedDataCenters)}");
                break;
            case PriceTrackingScopeMode.ByWorld:
                ImGui.TextDisabled($"Selected worlds: {settings.SelectedWorldIds.Count} world(s)");
                break;
        }

        ImGui.Spacing();

        var autoFetch = settings.AutoFetchInventoryPrices;
        if (ImGui.Checkbox("Auto-fetch inventory prices on startup", ref autoFetch))
        {
            settings.AutoFetchInventoryPrices = autoFetch;
            _configService.Save();
        }
        HelpMarker("Automatically fetch prices from API for items in your inventory when the plugin starts.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Status display
        if (_priceTrackingService != null)
        {
            ImGui.TextUnformatted("Status:");
            if (_priceTrackingService.IsInitialized)
            {
                ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.9f, 0.3f, 1f), "Initialized");
                var worldData = _priceTrackingService.WorldData;
                if (worldData != null)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({worldData.Worlds?.Count ?? 0} worlds, {worldData.DataCenters?.Count ?? 0} DCs)");
                }
                var marketable = _priceTrackingService.MarketableItems;
                if (marketable != null)
                {
                    ImGui.TextDisabled($"{marketable.Count} marketable items");
                }
            }
            else
            {
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), "Initializing...");
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
