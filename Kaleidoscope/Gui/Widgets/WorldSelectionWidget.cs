using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models.Universalis;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Selection mode for the world selection widget.
/// </summary>
public enum WorldSelectionMode
{
    /// <summary>Select individual worlds.</summary>
    Worlds,
    /// <summary>Select data centers (selects all worlds in DC).</summary>
    DataCenters,
    /// <summary>Select regions (selects all worlds in region).</summary>
    Regions
}

/// <summary>
/// A reusable world/datacenter/region selection widget with a dropdown containing
/// nested checkboxes in a hierarchical structure (Region > DataCenter > World).
/// </summary>
public class WorldSelectionWidget
{
    private readonly UniversalisWorldData? _worldData;
    private readonly string _id;

    /// <summary>The selection mode (Worlds, DataCenters, or Regions).</summary>
    public WorldSelectionMode Mode { get; set; } = WorldSelectionMode.Worlds;

    /// <summary>Selected region names.</summary>
    public HashSet<string> SelectedRegions { get; } = new();

    /// <summary>Selected data center names.</summary>
    public HashSet<string> SelectedDataCenters { get; } = new();

    /// <summary>Selected world IDs.</summary>
    public HashSet<int> SelectedWorldIds { get; } = new();

    /// <summary>Width of the dropdown button.</summary>
    public float Width { get; set; } = 250f;

    /// <summary>Maximum height of the popup content.</summary>
    public float MaxPopupHeight { get; set; } = 300f;

    /// <summary>Raised when the selection changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>
    /// Creates a new WorldSelectionWidget.
    /// </summary>
    /// <param name="worldData">World data from Universalis. Can be null if not yet loaded.</param>
    /// <param name="id">Unique ImGui ID for this widget instance.</param>
    public WorldSelectionWidget(UniversalisWorldData? worldData, string id = "WorldSelection")
    {
        _worldData = worldData;
        _id = id;
    }

    /// <summary>
    /// Initializes the widget with existing selections from settings.
    /// </summary>
    public void InitializeFrom(HashSet<string> regions, HashSet<string> dataCenters, HashSet<int> worldIds)
    {
        SelectedRegions.Clear();
        foreach (var r in regions) SelectedRegions.Add(r);

        SelectedDataCenters.Clear();
        foreach (var dc in dataCenters) SelectedDataCenters.Add(dc);

        SelectedWorldIds.Clear();
        foreach (var w in worldIds) SelectedWorldIds.Add(w);
    }

    /// <summary>
    /// Draws the world selection dropdown widget.
    /// </summary>
    /// <returns>True if the selection changed.</returns>
    public bool Draw()
    {
        return Draw("Select Worlds");
    }

    /// <summary>
    /// Draws the world selection dropdown widget with a custom label.
    /// </summary>
    /// <param name="label">The label for the dropdown.</param>
    /// <returns>True if the selection changed.</returns>
    public bool Draw(string label)
    {
        bool changed = false;

        if (_worldData == null || _worldData.DataCenters.Count == 0)
        {
            ImGui.TextDisabled("World data not loaded...");
            return false;
        }

        // Build display text showing current selection summary
        var displayText = GetSelectionSummary();

        // Draw the dropdown button
        ImGui.SetNextItemWidth(Width);
        if (ImGui.BeginCombo($"{label}##{_id}", displayText))
        {
            // Draw selection mode tabs
            DrawModeSelector(ref changed);

            ImGui.Separator();

            // Draw scrollable content
            var contentHeight = Math.Min(MaxPopupHeight, ImGui.GetContentRegionAvail().Y);
            if (ImGui.BeginChild($"##{_id}_content", new Vector2(0, contentHeight), false))
            {
                switch (Mode)
                {
                    case WorldSelectionMode.Regions:
                        changed |= DrawRegionSelection();
                        break;
                    case WorldSelectionMode.DataCenters:
                        changed |= DrawDataCenterSelection();
                        break;
                    case WorldSelectionMode.Worlds:
                    default:
                        changed |= DrawWorldSelection();
                        break;
                }
                ImGui.EndChild();
            }

            // Quick actions
            ImGui.Separator();
            DrawQuickActions(ref changed);

            ImGui.EndCombo();
        }

        if (changed)
        {
            SelectionChanged?.Invoke();
        }

        return changed;
    }

    private void DrawModeSelector(ref bool changed)
    {
        ImGui.TextDisabled("Selection Mode:");
        ImGui.SameLine();

        var mode = (int)Mode;
        if (ImGui.RadioButton($"Worlds##{_id}_mode", ref mode, (int)WorldSelectionMode.Worlds))
        {
            Mode = WorldSelectionMode.Worlds;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton($"Data Centers##{_id}_mode", ref mode, (int)WorldSelectionMode.DataCenters))
        {
            Mode = WorldSelectionMode.DataCenters;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton($"Regions##{_id}_mode", ref mode, (int)WorldSelectionMode.Regions))
        {
            Mode = WorldSelectionMode.Regions;
        }
    }

    private bool DrawRegionSelection()
    {
        bool changed = false;
        var regions = _worldData!.Regions.ToList();

        foreach (var region in regions)
        {
            var isSelected = SelectedRegions.Contains(region);
            var dcCount = _worldData.GetDataCentersForRegion(region).Count();

            if (ImGui.Checkbox($"{region} ({dcCount} DCs)##{_id}_region_{region}", ref isSelected))
            {
                if (isSelected)
                    SelectedRegions.Add(region);
                else
                    SelectedRegions.Remove(region);
                changed = true;
            }

            // Show nested DCs as info (read-only in region mode)
            if (isSelected && ImGui.TreeNode($"##info_{region}"))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                foreach (var dc in _worldData.GetDataCentersForRegion(region))
                {
                    ImGui.TextUnformatted($"  • {dc.Name}");
                }
                ImGui.PopStyleColor();
                ImGui.TreePop();
            }
        }

        return changed;
    }

    private bool DrawDataCenterSelection()
    {
        bool changed = false;
        var regions = _worldData!.Regions.ToList();

        foreach (var region in regions)
        {
            // Region as collapsible header
            if (ImGui.TreeNode($"{region}##{_id}_region_{region}"))
            {
                foreach (var dc in _worldData.GetDataCentersForRegion(region))
                {
                    var dcName = dc.Name ?? "";
                    var isSelected = SelectedDataCenters.Contains(dcName);
                    var worldCount = dc.Worlds?.Count ?? 0;

                    if (ImGui.Checkbox($"{dcName} ({worldCount} worlds)##{_id}_dc_{dcName}", ref isSelected))
                    {
                        if (isSelected)
                            SelectedDataCenters.Add(dcName);
                        else
                            SelectedDataCenters.Remove(dcName);
                        changed = true;
                    }
                }
                ImGui.TreePop();
            }
        }

        return changed;
    }

    private bool DrawWorldSelection()
    {
        bool changed = false;
        var regions = _worldData!.Regions.ToList();

        foreach (var region in regions)
        {
            // Region header
            bool regionOpen = ImGui.TreeNode($"{region}##{_id}_region_{region}");

            // Add select all/none for region
            if (regionOpen)
            {
                foreach (var dc in _worldData.GetDataCentersForRegion(region))
                {
                    var dcName = dc.Name ?? "";
                    var dcWorldIds = dc.Worlds ?? new List<int>();

                    // DC with checkbox for select all in DC
                    bool allSelected = dcWorldIds.Count > 0 && dcWorldIds.All(w => SelectedWorldIds.Contains(w));
                    bool someSelected = dcWorldIds.Any(w => SelectedWorldIds.Contains(w));

                    // Mixed state indicator
                    bool mixed = someSelected && !allSelected;

                    // DC checkbox (selects/deselects all worlds in DC)
                    if (mixed)
                    {
                        ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.7f, 0.7f, 0.7f, 1f));
                    }

                    if (ImGui.Checkbox($"{dcName}##{_id}_dc_{dcName}", ref allSelected))
                    {
                        if (allSelected)
                        {
                            foreach (var wid in dcWorldIds)
                                SelectedWorldIds.Add(wid);
                        }
                        else
                        {
                            foreach (var wid in dcWorldIds)
                                SelectedWorldIds.Remove(wid);
                        }
                        changed = true;
                    }

                    if (mixed)
                    {
                        ImGui.PopStyleColor();
                    }

                    // Expand DC to show individual worlds
                    ImGui.SameLine();
                    if (ImGui.TreeNode($"##expand_{dcName}"))
                    {
                        foreach (var worldId in dcWorldIds)
                        {
                            var worldName = _worldData.GetWorldName(worldId) ?? $"World {worldId}";
                            var isWorldSelected = SelectedWorldIds.Contains(worldId);

                            ImGui.Indent(10f);
                            if (ImGui.Checkbox($"{worldName}##{_id}_world_{worldId}", ref isWorldSelected))
                            {
                                if (isWorldSelected)
                                    SelectedWorldIds.Add(worldId);
                                else
                                    SelectedWorldIds.Remove(worldId);
                                changed = true;
                            }
                            ImGui.Unindent(10f);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.TreePop();
            }
        }

        return changed;
    }

    private void DrawQuickActions(ref bool changed)
    {
        if (ImGui.Button($"Select All##{_id}_selectall"))
        {
            SelectAll();
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button($"Clear All##{_id}_clearall"))
        {
            ClearAll();
            changed = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"({GetSelectionCount()} selected)");
    }

    private void SelectAll()
    {
        if (_worldData == null) return;

        switch (Mode)
        {
            case WorldSelectionMode.Regions:
                foreach (var region in _worldData.Regions)
                    SelectedRegions.Add(region);
                break;

            case WorldSelectionMode.DataCenters:
                foreach (var dc in _worldData.DataCenters)
                    if (!string.IsNullOrEmpty(dc.Name))
                        SelectedDataCenters.Add(dc.Name);
                break;

            case WorldSelectionMode.Worlds:
                foreach (var world in _worldData.Worlds)
                    SelectedWorldIds.Add(world.Id);
                break;
        }
    }

    private void ClearAll()
    {
        switch (Mode)
        {
            case WorldSelectionMode.Regions:
                SelectedRegions.Clear();
                break;
            case WorldSelectionMode.DataCenters:
                SelectedDataCenters.Clear();
                break;
            case WorldSelectionMode.Worlds:
                SelectedWorldIds.Clear();
                break;
        }
    }

    private int GetSelectionCount()
    {
        return Mode switch
        {
            WorldSelectionMode.Regions => SelectedRegions.Count,
            WorldSelectionMode.DataCenters => SelectedDataCenters.Count,
            WorldSelectionMode.Worlds => SelectedWorldIds.Count,
            _ => 0
        };
    }

    private string GetSelectionSummary()
    {
        if (_worldData == null) return "Loading...";

        return Mode switch
        {
            WorldSelectionMode.Regions => SelectedRegions.Count switch
            {
                0 => "No regions selected",
                1 => SelectedRegions.First(),
                _ => $"{SelectedRegions.Count} regions"
            },
            WorldSelectionMode.DataCenters => SelectedDataCenters.Count switch
            {
                0 => "No data centers selected",
                1 => SelectedDataCenters.First(),
                2 => string.Join(", ", SelectedDataCenters.Take(2)),
                _ => $"{SelectedDataCenters.Count} data centers"
            },
            WorldSelectionMode.Worlds => SelectedWorldIds.Count switch
            {
                0 => "No worlds selected",
                1 => _worldData.GetWorldName(SelectedWorldIds.First()) ?? "1 world",
                2 or 3 => string.Join(", ", SelectedWorldIds.Take(3).Select(id => _worldData.GetWorldName(id) ?? $"{id}")),
                _ => $"{SelectedWorldIds.Count} worlds"
            },
            _ => "Select..."
        };
    }

    /// <summary>
    /// Gets all selected world IDs based on the current mode.
    /// In Region/DC mode, this expands to all worlds in selected regions/DCs.
    /// </summary>
    public HashSet<int> GetEffectiveWorldIds()
    {
        if (_worldData == null) return new HashSet<int>();

        var result = new HashSet<int>();

        switch (Mode)
        {
            case WorldSelectionMode.Regions:
                foreach (var region in SelectedRegions)
                {
                    foreach (var dc in _worldData.GetDataCentersForRegion(region))
                    {
                        if (dc.Worlds != null)
                        {
                            foreach (var wid in dc.Worlds)
                                result.Add(wid);
                        }
                    }
                }
                break;

            case WorldSelectionMode.DataCenters:
                foreach (var dcName in SelectedDataCenters)
                {
                    var dc = _worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
                    if (dc?.Worlds != null)
                    {
                        foreach (var wid in dc.Worlds)
                            result.Add(wid);
                    }
                }
                break;

            case WorldSelectionMode.Worlds:
                foreach (var wid in SelectedWorldIds)
                    result.Add(wid);
                break;
        }

        return result;
    }

    /// <summary>
    /// Checks if a specific world ID is selected (directly or via DC/region).
    /// </summary>
    public bool IsWorldSelected(int worldId)
    {
        return GetEffectiveWorldIds().Contains(worldId);
    }

    /// <summary>
    /// Draws a simple single-world selector dropdown.
    /// </summary>
    /// <param name="worldData">World data from Universalis.</param>
    /// <param name="label">Label for the combo box.</param>
    /// <param name="selectedWorldId">Currently selected world ID (0 = all/none).</param>
    /// <param name="width">Width of the dropdown.</param>
    /// <param name="includeAllOption">Whether to include an "All Worlds" option with ID 0.</param>
    /// <returns>True if selection changed, with the new world ID.</returns>
    public static bool DrawSingleWorldSelector(
        UniversalisWorldData? worldData, 
        string label, 
        ref int selectedWorldId,
        float width = 200f,
        bool includeAllOption = true)
    {
        if (worldData == null || worldData.Worlds.Count == 0)
        {
            ImGui.TextDisabled("World data not loaded...");
            return false;
        }

        bool changed = false;

        // Build world list
        var worldNames = new List<string>();
        var worldIds = new List<int>();

        if (includeAllOption)
        {
            worldNames.Add("All Worlds");
            worldIds.Add(0);
        }

        // Group by region and DC
        foreach (var region in worldData.Regions)
        {
            foreach (var dc in worldData.GetDataCentersForRegion(region))
            {
                if (dc.Worlds == null) continue;
                foreach (var wid in dc.Worlds)
                {
                    var name = worldData.GetWorldName(wid);
                    if (!string.IsNullOrEmpty(name))
                    {
                        worldNames.Add($"{name} ({dc.Name})");
                        worldIds.Add(wid);
                    }
                }
            }
        }

        // Find current selection index
        var currentIndex = worldIds.IndexOf(selectedWorldId);
        if (currentIndex < 0) currentIndex = 0;

        ImGui.SetNextItemWidth(width);
        if (ImGui.Combo(label, ref currentIndex, worldNames.ToArray(), worldNames.Count))
        {
            selectedWorldId = worldIds[currentIndex];
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Draws a simple single-datacenter selector dropdown.
    /// </summary>
    /// <param name="worldData">World data from Universalis.</param>
    /// <param name="label">Label for the combo box.</param>
    /// <param name="selectedDcName">Currently selected DC name (empty = all/none).</param>
    /// <param name="width">Width of the dropdown.</param>
    /// <param name="includeAllOption">Whether to include an "All Data Centers" option.</param>
    /// <returns>True if selection changed.</returns>
    public static bool DrawSingleDataCenterSelector(
        UniversalisWorldData? worldData,
        string label,
        ref string selectedDcName,
        float width = 200f,
        bool includeAllOption = true)
    {
        if (worldData == null || worldData.DataCenters.Count == 0)
        {
            ImGui.TextDisabled("World data not loaded...");
            return false;
        }

        bool changed = false;

        var dcNames = new List<string>();
        if (includeAllOption)
        {
            dcNames.Add("All Data Centers");
        }

        foreach (var region in worldData.Regions)
        {
            foreach (var dc in worldData.GetDataCentersForRegion(region))
            {
                if (!string.IsNullOrEmpty(dc.Name))
                {
                    dcNames.Add($"{dc.Name} ({region})");
                }
            }
        }

        // Find current index
        var currentIndex = 0;
        if (!string.IsNullOrEmpty(selectedDcName))
        {
            for (int i = 0; i < dcNames.Count; i++)
            {
                if (dcNames[i].StartsWith(selectedDcName))
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        ImGui.SetNextItemWidth(width);
        if (ImGui.Combo(label, ref currentIndex, dcNames.ToArray(), dcNames.Count))
        {
            if (includeAllOption && currentIndex == 0)
            {
                selectedDcName = "";
            }
            else
            {
                // Extract DC name from "DCName (Region)"
                var selected = dcNames[currentIndex];
                var parenIdx = selected.IndexOf(" (");
                selectedDcName = parenIdx > 0 ? selected[..parenIdx] : selected;
            }
            changed = true;
        }

        return changed;
    }
}
