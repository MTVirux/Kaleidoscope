using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models.Universalis;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A tree-based widget for configuring price match modes hierarchically.
/// Shows Region > DC > World hierarchy with cascading dropdowns.
/// </summary>
public class PriceMatchTreeWidget
{
    private readonly UniversalisWorldData _worldData;
    private readonly string _id;

    /// <summary>Width of the widget.</summary>
    public float Width { get; set; } = 400f;

    /// <summary>
    /// Reference to the settings being edited. Changes are applied directly to this object.
    /// </summary>
    public InventoryValueSettings? Settings { get; set; }

    /// <summary>Event fired when any setting changes.</summary>
    public event Action? OnSettingsChanged;

    // Display names for price match modes
    private static readonly string[] PriceMatchModeNames =
    {
        "World",
        "Data Center",
        "Region",
        "Region + Oceania",
        "Global"
    };

    private const string CustomFilteringLabel = "Custom";

    public PriceMatchTreeWidget(UniversalisWorldData worldData, string id = "PriceMatchTree")
    {
        _worldData = worldData;
        _id = id;
    }

    /// <summary>
    /// Draws the tree widget. Returns true if any setting was changed.
    /// </summary>
    public bool Draw()
    {
        if (Settings == null) return false;

        var changed = false;

        // Default setting at top
        ImGui.TextUnformatted("Default Price Match Mode:");
        ImGui.SameLine();
        
        var defaultMode = (int)Settings.DefaultPriceMatchMode;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo($"##DefaultMode{_id}", ref defaultMode, PriceMatchModeNames, PriceMatchModeNames.Length))
        {
            Settings.DefaultPriceMatchMode = (PriceMatchMode)defaultMode;
            changed = true;
        }
        ImGui.SameLine();
        HelpMarker("The default price match mode used when no specific override is set.\n\n" +
            "• World: Only use prices from the character's world\n" +
            "• Data Center: Use prices from all worlds in the DC\n" +
            "• Region: Use prices from all worlds in the region\n" +
            "• Region + Oceania: Region prices plus Oceania\n" +
            "• Global: Use prices from all worlds");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Tree view by region
        var regions = _worldData.Regions.OrderBy(r => r).ToList();

        foreach (var region in regions)
        {
            changed |= DrawRegionNode(region);
        }

        if (changed)
        {
            OnSettingsChanged?.Invoke();
        }

        return changed;
    }

    private bool DrawRegionNode(string region)
    {
        var changed = false;

        // Get effective mode for this region
        var effectiveMode = GetEffectiveModeForRegion(region, out var isCustom);

        // Region node with dropdown
        var nodeFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
        
        var nodeOpen = ImGui.TreeNodeEx($"{region}##{_id}", nodeFlags);

        // Draw dropdown on same line
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 160);
        changed |= DrawPriceMatchDropdown($"Region_{region}", region, null, null, effectiveMode, isCustom, 
            mode => {
                if (mode.HasValue)
                    Settings!.RegionPriceMatchModes[region] = mode.Value;
                else
                    Settings!.RegionPriceMatchModes.Remove(region);
            },
            Settings!.RegionPriceMatchModes.ContainsKey(region));

        if (nodeOpen)
        {
            var dataCenters = _worldData.GetDataCentersForRegion(region).OrderBy(dc => dc.Name).ToList();

            foreach (var dc in dataCenters)
            {
                if (dc.Name != null)
                {
                    changed |= DrawDataCenterNode(dc.Name, region);
                }
            }

            ImGui.TreePop();
        }

        return changed;
    }

    private bool DrawDataCenterNode(string dcName, string region)
    {
        var changed = false;

        // Get effective mode for this DC
        var effectiveMode = GetEffectiveModeForDataCenter(dcName, region, out var isCustom);

        var nodeFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
        
        var nodeOpen = ImGui.TreeNodeEx($"{dcName}##{_id}", nodeFlags);

        // Draw dropdown on same line
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 160);
        changed |= DrawPriceMatchDropdown($"DC_{dcName}", dcName, region, null, effectiveMode, isCustom,
            mode => {
                if (mode.HasValue)
                    Settings!.DataCenterPriceMatchModes[dcName] = mode.Value;
                else
                    Settings!.DataCenterPriceMatchModes.Remove(dcName);
            },
            Settings!.DataCenterPriceMatchModes.ContainsKey(dcName));

        if (nodeOpen)
        {
            var worlds = _worldData.GetWorldsForDataCenter(dcName).OrderBy(w => w.Name).ToList();

            foreach (var world in worlds)
            {
                if (world.Name != null)
                {
                    changed |= DrawWorldNode(world.Id, world.Name, dcName, region);
                }
            }

            ImGui.TreePop();
        }

        return changed;
    }

    private bool DrawWorldNode(int worldId, string worldName, string dcName, string region)
    {
        var changed = false;

        // Get effective mode for this world
        var effectiveMode = GetEffectiveModeForWorld(worldId, dcName, region);

        // World leaf node (no expansion)
        ImGui.TreeNodeEx($"{worldName}##{_id}", ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);

        // Draw dropdown on same line
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 160);
        changed |= DrawPriceMatchDropdown($"World_{worldId}", worldName, region, dcName, effectiveMode, false,
            mode => {
                if (mode.HasValue)
                    Settings!.WorldPriceMatchModes[worldId] = mode.Value;
                else
                    Settings!.WorldPriceMatchModes.Remove(worldId);
            },
            Settings!.WorldPriceMatchModes.ContainsKey(worldId));

        return changed;
    }

    /// <summary>
    /// Draws a price match mode dropdown.
    /// </summary>
    /// <param name="id">Unique ID for the dropdown.</param>
    /// <param name="displayName">Name to show in preview (when inherited).</param>
    /// <param name="parentRegion">Parent region name (for inheritance info).</param>
    /// <param name="parentDc">Parent DC name (for inheritance info).</param>
    /// <param name="effectiveMode">The currently effective mode.</param>
    /// <param name="isCustom">Whether children have mixed modes.</param>
    /// <param name="onModeChanged">Callback when mode is selected. null = inherit.</param>
    /// <param name="hasOverride">Whether this node has an explicit override set.</param>
    private bool DrawPriceMatchDropdown(
        string id, 
        string displayName,
        string? parentRegion,
        string? parentDc,
        PriceMatchMode effectiveMode, 
        bool isCustom,
        Action<PriceMatchMode?> onModeChanged,
        bool hasOverride)
    {
        var changed = false;

        // Build preview text
        string previewText;
        Vector4 previewColor;

        if (isCustom)
        {
            previewText = CustomFilteringLabel;
            previewColor = new Vector4(1f, 0.8f, 0.3f, 1f); // Yellow for custom
        }
        else if (hasOverride)
        {
            previewText = PriceMatchModeNames[(int)effectiveMode];
            previewColor = new Vector4(0.5f, 0.8f, 1f, 1f); // Blue for override
        }
        else
        {
            previewText = $"{PriceMatchModeNames[(int)effectiveMode]} (inherited)";
            previewColor = new Vector4(0.7f, 0.7f, 0.7f, 1f); // Gray for inherited
        }

        ImGui.SetNextItemWidth(155);
        ImGui.PushStyleColor(ImGuiCol.Text, previewColor);
        
        if (ImGui.BeginCombo($"##{id}{_id}", previewText))
        {
            ImGui.PopStyleColor();

            // "Inherit" option (clear override)
            if (hasOverride)
            {
                if (ImGui.Selectable("Inherit from parent"))
                {
                    onModeChanged(null);
                    changed = true;
                }
                ImGui.Separator();
            }

            // All mode options
            for (int i = 0; i < PriceMatchModeNames.Length; i++)
            {
                var isSelected = hasOverride && (int)effectiveMode == i;
                if (ImGui.Selectable(PriceMatchModeNames[i], isSelected))
                {
                    onModeChanged((PriceMatchMode)i);
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }
        else
        {
            ImGui.PopStyleColor();
        }

        return changed;
    }

    private PriceMatchMode GetEffectiveModeForRegion(string region, out bool isCustom)
    {
        isCustom = false;

        // Check if region has an explicit override
        if (Settings!.RegionPriceMatchModes.TryGetValue(region, out var regionMode))
        {
            // Check if all children have the same effective mode
            isCustom = !AreAllChildrenSameMode(region, regionMode);
            return regionMode;
        }

        // Check if all DCs and worlds under this region have consistent settings
        var childModes = new HashSet<PriceMatchMode>();
        var dataCenters = _worldData.GetDataCentersForRegion(region);

        foreach (var dc in dataCenters)
        {
            if (dc.Name == null) continue;

            if (Settings.DataCenterPriceMatchModes.TryGetValue(dc.Name, out var dcMode))
            {
                childModes.Add(dcMode);
            }
            else if (dc.Worlds != null)
            {
                foreach (var worldId in dc.Worlds)
                {
                    if (Settings.WorldPriceMatchModes.TryGetValue(worldId, out var worldMode))
                    {
                        childModes.Add(worldMode);
                    }
                }
            }
        }

        if (childModes.Count > 1)
        {
            isCustom = true;
        }
        else if (childModes.Count == 1)
        {
            // One child mode but not all children have it - could be custom
            isCustom = true;
        }

        return Settings.DefaultPriceMatchMode;
    }

    private PriceMatchMode GetEffectiveModeForDataCenter(string dcName, string region, out bool isCustom)
    {
        isCustom = false;

        // Check if DC has an explicit override
        if (Settings!.DataCenterPriceMatchModes.TryGetValue(dcName, out var dcMode))
        {
            // Check if all children have the same effective mode
            var dc = _worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
            if (dc?.Worlds != null)
            {
                foreach (var worldId in dc.Worlds)
                {
                    if (Settings.WorldPriceMatchModes.TryGetValue(worldId, out var worldMode) && worldMode != dcMode)
                    {
                        isCustom = true;
                        break;
                    }
                }
            }
            return dcMode;
        }

        // Check region override
        if (Settings.RegionPriceMatchModes.TryGetValue(region, out var regionMode))
        {
            // Check if any world overrides
            var dc = _worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
            if (dc?.Worlds != null)
            {
                foreach (var worldId in dc.Worlds)
                {
                    if (Settings.WorldPriceMatchModes.TryGetValue(worldId, out var worldMode) && worldMode != regionMode)
                    {
                        isCustom = true;
                        break;
                    }
                }
            }
            return regionMode;
        }

        // Check if worlds under this DC have mixed settings
        var dc2 = _worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
        if (dc2?.Worlds != null)
        {
            var worldModes = new HashSet<PriceMatchMode>();
            foreach (var worldId in dc2.Worlds)
            {
                if (Settings.WorldPriceMatchModes.TryGetValue(worldId, out var worldMode))
                {
                    worldModes.Add(worldMode);
                }
            }
            if (worldModes.Count > 1 || (worldModes.Count == 1 && dc2.Worlds.Count > worldModes.Count))
            {
                isCustom = true;
            }
        }

        return Settings.DefaultPriceMatchMode;
    }

    private PriceMatchMode GetEffectiveModeForWorld(int worldId, string dcName, string region)
    {
        // Check world override
        if (Settings!.WorldPriceMatchModes.TryGetValue(worldId, out var worldMode))
            return worldMode;

        // Check DC override
        if (Settings.DataCenterPriceMatchModes.TryGetValue(dcName, out var dcMode))
            return dcMode;

        // Check region override
        if (Settings.RegionPriceMatchModes.TryGetValue(region, out var regionMode))
            return regionMode;

        // Default
        return Settings.DefaultPriceMatchMode;
    }

    private bool AreAllChildrenSameMode(string region, PriceMatchMode expectedMode)
    {
        var dataCenters = _worldData.GetDataCentersForRegion(region);

        foreach (var dc in dataCenters)
        {
            if (dc.Name == null) continue;

            // Check DC override
            if (Settings!.DataCenterPriceMatchModes.TryGetValue(dc.Name, out var dcMode) && dcMode != expectedMode)
                return false;

            // Check world overrides
            if (dc.Worlds != null)
            {
                foreach (var worldId in dc.Worlds)
                {
                    if (Settings.WorldPriceMatchModes.TryGetValue(worldId, out var worldMode) && worldMode != expectedMode)
                        return false;
                }
            }
        }

        return true;
    }

    private static void HelpMarker(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
