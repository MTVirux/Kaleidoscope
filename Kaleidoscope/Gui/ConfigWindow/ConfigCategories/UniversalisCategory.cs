using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Universalis Integration settings category in the config window.
/// </summary>
public class UniversalisCategory
{
    private readonly ConfigurationService _configService;

    private Configuration Config => _configService.Config;

    private static readonly string[] ScopeNames = { "World", "Data Center", "Region" };

    // Common region names for the dropdown
    private static readonly string[] RegionNames = 
    {
        "",
        "Japan",
        "North-America",
        "Europe",
        "Oceania"
    };

    public UniversalisCategory(ConfigurationService configService)
    {
        _configService = configService;
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
