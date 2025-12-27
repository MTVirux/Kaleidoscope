using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Models;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Centralized widget for drawing special grouping settings UI.
/// Used by DataTool and related tools to ensure consistent behavior.
/// </summary>
public static class SpecialGroupingWidget
{
    /// <summary>
    /// Information about all possible special groupings.
    /// </summary>
    private static readonly List<(SpecialGroupingType type, string name, string tooltip)> AllGroupings = new()
    {
        (
            SpecialGroupingType.AllCrystals,
            "All Crystals",
            "Requires all 18 crystal types:\n" +
            "• Fire, Ice, Wind, Earth, Lightning, Water Shards\n" +
            "• Fire, Ice, Wind, Earth, Lightning, Water Crystals\n" +
            "• Fire, Ice, Wind, Earth, Lightning, Water Clusters\n\n" +
            "Unlocks element and tier filtering."
        )
    };

    /// <summary>
    /// Draws the complete special grouping settings section.
    /// </summary>
    /// <param name="settings">The special grouping settings to display and modify.</param>
    /// <param name="columns">The current column configurations to detect available groupings.</param>
    /// <param name="onSettingsChanged">Callback when any setting changes.</param>
    /// <param name="onRefreshNeeded">Callback when data refresh is needed.</param>
    /// <returns>True if any setting was changed.</returns>
    public static bool Draw(
        SpecialGroupingSettings settings,
        IEnumerable<ItemColumnConfig> columns,
        Action? onSettingsChanged = null,
        Action? onRefreshNeeded = null,
        Action<uint, bool>? onAddColumn = null)
    {
        var changed = false;
        
        // Detect which special grouping is available
        var detectedGrouping = SpecialGroupingHelper.DetectSpecialGrouping(columns);
        
        // Update the active grouping setting if detection changed
        if (settings.ActiveGrouping != detectedGrouping)
        {
            settings.ActiveGrouping = detectedGrouping;
            
            // If grouping was lost, disable filtering
            if (detectedGrouping == SpecialGroupingType.None)
            {
                settings.Enabled = false;
            }
            
            changed = true;
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        if (!ImGui.CollapsingHeader("Special Grouping", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (changed)
            {
                onSettingsChanged?.Invoke();
            }
            return changed;
        }
        
        // Sort so unlocked groupings appear first
        var sortedGroupings = AllGroupings
            .Select(g => (g.type, g.name, g.tooltip, unlocked: detectedGrouping.HasFlag(g.type)))
            .OrderByDescending(g => g.unlocked)
            .ToList();
        
        // Draw each grouping
        foreach (var (type, name, tooltip, unlocked) in sortedGroupings)
        {
            if (DrawSpecialGroupingItem(settings, type, unlocked, name, tooltip, onSettingsChanged, onRefreshNeeded, onAddColumn))
            {
                changed = true;
            }
        }
        
        if (changed)
        {
            onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws a single special grouping item with collapsible header (if unlocked) or disabled text with Add button (if locked).
    /// </summary>
    private static bool DrawSpecialGroupingItem(
        SpecialGroupingSettings settings,
        SpecialGroupingType type,
        bool unlocked,
        string name,
        string tooltip,
        Action? onSettingsChanged,
        Action? onRefreshNeeded,
        Action<uint, bool>? onAddColumn)
    {
        var changed = false;
        
        if (unlocked)
        {
            // Draw as collapsible header with green checkmark (indented since it's nested)
            ImGui.Indent();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1.0f, 0.4f, 1.0f));
            var headerOpen = ImGui.CollapsingHeader($"✓ {name}##special_{type}", ImGuiTreeNodeFlags.DefaultOpen);
            ImGui.PopStyleColor();
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tooltip);
            }
            
            // Draw the filters inside the collapsible header
            if (headerOpen)
            {
                switch (type)
                {
                    case SpecialGroupingType.AllCrystals:
                        if (DrawCrystalFilters(settings, onSettingsChanged, onRefreshNeeded))
                            changed = true;
                        break;
                    case SpecialGroupingType.AllGil:
                        if (DrawGilFilters(settings, onSettingsChanged, onRefreshNeeded))
                            changed = true;
                        break;
                }
            }
            ImGui.Unindent();
        }
        else
        {
            // Show locked grouping with lock icon (indented to match unlocked items)
            ImGui.Indent();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            ImGui.TextUnformatted("🔒");
            ImGui.SameLine();
            ImGui.TextDisabled(name);
            ImGui.PopStyleColor();
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{tooltip}\n\n[LOCKED] Add the required items to unlock this filter.");
            }
            
            // Show "Add All" button for crystals when locked
            if (type == SpecialGroupingType.AllCrystals && onAddColumn != null)
            {
                ImGui.SameLine();
                if (ImGuiHelpers.ButtonAutoWidth("Add All##addAllCrystals"))
                {
                    // Add all 18 crystal types
                    var allCrystalIds = SpecialGroupingHelper.GetAllCrystalItemIds();
                    foreach (var crystalId in allCrystalIds)
                    {
                        onAddColumn(crystalId, false); // false = not a currency
                    }
                    onRefreshNeeded?.Invoke();
                    onSettingsChanged?.Invoke();
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Add all 18 crystal types to the table:\n\n" +
                        "• Fire, Ice, Wind, Earth, Lightning, Water Shards\n" +
                        "• Fire, Ice, Wind, Earth, Lightning, Water Crystals\n" +
                        "• Fire, Ice, Wind, Earth, Lightning, Water Clusters");
                }
            }
            ImGui.Unindent();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws the gil merge option checkbox.
    /// </summary>
    private static bool DrawGilFilters(
        SpecialGroupingSettings settings,
        Action? onSettingsChanged,
        Action? onRefreshNeeded)
    {
        var changed = false;
        
        ImGui.Indent();
        
        var mergeGil = settings.MergeGilCurrencies;
        if (ImGui.Checkbox("Merge into Gil##mergeGil", ref mergeGil))
        {
            settings.MergeGilCurrencies = mergeGil;
            onRefreshNeeded?.Invoke();
            onSettingsChanged?.Invoke();
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When enabled, Free Company Gil and Retainer Gil columns\nare hidden and their values are added to the Gil column.");
        }
        
        if (mergeGil)
        {
            ImGui.TextDisabled("FC Gil + Retainer Gil → Gil");
        }
        
        ImGui.Unindent();
        
        return changed;
    }

    /// <summary>
    /// Draws the crystal element and tier filter checkboxes.
    /// </summary>
    private static bool DrawCrystalFilters(
        SpecialGroupingSettings settings,
        Action? onSettingsChanged,
        Action? onRefreshNeeded)
    {
        var changed = false;
        
        ImGui.Indent();
        
        // Element filters
        ImGui.TextDisabled("Elements:");
        ImGui.SameLine();
        
        // Select All / Deselect All for elements
        if (ImGui.SmallButton("All##elements"))
        {
            foreach (CrystalElement element in Enum.GetValues<CrystalElement>())
            {
                settings.EnabledElements.Add(element);
            }
            onRefreshNeeded?.Invoke();
            onSettingsChanged?.Invoke();
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("None##elements"))
        {
            settings.EnabledElements.Clear();
            onRefreshNeeded?.Invoke();
            onSettingsChanged?.Invoke();
            changed = true;
        }
        
        // Draw element checkboxes in a row with colored text
        foreach (CrystalElement element in Enum.GetValues<CrystalElement>())
        {
            var elementEnabled = settings.EnabledElements.Contains(element);
            var elementColor = SpecialGroupingHelper.GetElementColor(element);
            
            ImGui.PushStyleColor(ImGuiCol.Text, elementColor);
            if (ImGui.Checkbox($"{SpecialGroupingHelper.GetElementName(element)}##element", ref elementEnabled))
            {
                if (elementEnabled)
                    settings.EnabledElements.Add(element);
                else
                    settings.EnabledElements.Remove(element);
                onRefreshNeeded?.Invoke();
                onSettingsChanged?.Invoke();
                changed = true;
            }
            ImGui.PopStyleColor();
            
            // Put elements on same line (except last)
            if (element != CrystalElement.Water)
                ImGui.SameLine();
        }
        
        ImGui.Spacing();
        
        // Tier filters
        ImGui.TextDisabled("Tiers:");
        ImGui.SameLine();
        
        // Select All / Deselect All for tiers
        if (ImGui.SmallButton("All##tiers"))
        {
            foreach (CrystalTier tier in Enum.GetValues<CrystalTier>())
            {
                settings.EnabledTiers.Add(tier);
            }
            onRefreshNeeded?.Invoke();
            onSettingsChanged?.Invoke();
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("None##tiers"))
        {
            settings.EnabledTiers.Clear();
            onRefreshNeeded?.Invoke();
            onSettingsChanged?.Invoke();
            changed = true;
        }
        
        // Draw tier checkboxes in a row
        foreach (CrystalTier tier in Enum.GetValues<CrystalTier>())
        {
            var tierEnabled = settings.EnabledTiers.Contains(tier);
            if (ImGui.Checkbox($"{SpecialGroupingHelper.GetTierName(tier)}##tier", ref tierEnabled))
            {
                if (tierEnabled)
                    settings.EnabledTiers.Add(tier);
                else
                    settings.EnabledTiers.Remove(tier);
                onRefreshNeeded?.Invoke();
                onSettingsChanged?.Invoke();
                changed = true;
            }
            
            // Put tiers on same line (except last)
            if (tier != CrystalTier.Cluster)
                ImGui.SameLine();
        }
        
        // Show count of visible crystals
        var visibleCount = settings.EnabledElements.Count * settings.EnabledTiers.Count;
        ImGui.TextDisabled($"Showing {visibleCount}/18 crystal types");
        
        ImGui.Unindent();
        
        return changed;
    }
    
    /// <summary>
    /// Exports special grouping settings to a dictionary for serialization.
    /// </summary>
    public static Dictionary<string, object?> ExportSettings(SpecialGroupingSettings settings)
    {
        return new Dictionary<string, object?>
        {
            ["Enabled"] = settings.Enabled,
            ["ActiveGrouping"] = (int)settings.ActiveGrouping,
            ["AllCrystalsEnabled"] = settings.AllCrystalsEnabled,
            ["AllGilEnabled"] = settings.AllGilEnabled,
            ["MergeGilCurrencies"] = settings.MergeGilCurrencies,
            ["EnabledElements"] = settings.EnabledElements.Select(e => (int)e).ToList(),
            ["EnabledTiers"] = settings.EnabledTiers.Select(t => (int)t).ToList()
        };
    }
    
    /// <summary>
    /// Imports special grouping settings from a dictionary.
    /// </summary>
    public static void ImportSettings(SpecialGroupingSettings settings, Dictionary<string, object?>? data)
    {
        if (data == null) return;
        
        settings.Enabled = SettingsImportHelper.GetSetting(data, "Enabled", settings.Enabled);
        settings.ActiveGrouping = (SpecialGroupingType)SettingsImportHelper.GetSetting(data, "ActiveGrouping", (int)settings.ActiveGrouping);
        settings.AllCrystalsEnabled = SettingsImportHelper.GetSetting(data, "AllCrystalsEnabled", settings.AllCrystalsEnabled);
        settings.AllGilEnabled = SettingsImportHelper.GetSetting(data, "AllGilEnabled", settings.AllGilEnabled);
        settings.MergeGilCurrencies = SettingsImportHelper.GetSetting(data, "MergeGilCurrencies", settings.MergeGilCurrencies);
        
        // Import enabled elements
        var elements = SettingsImportHelper.ImportIntList(data, "EnabledElements");
        if (elements != null)
        {
            settings.EnabledElements.Clear();
            foreach (var e in elements)
            {
                settings.EnabledElements.Add((CrystalElement)e);
            }
        }
        
        // Import enabled tiers
        var tiers = SettingsImportHelper.ImportIntList(data, "EnabledTiers");
        if (tiers != null)
        {
            settings.EnabledTiers.Clear();
            foreach (var t in tiers)
            {
                settings.EnabledTiers.Add((CrystalTier)t);
            }
        }
    }
}
