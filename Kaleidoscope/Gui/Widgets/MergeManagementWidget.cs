using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Widget for managing source merging (characters/rows).
/// Item/currency merge is now integrated directly into ColumnManagementWidget.
/// </summary>
public static class MergeManagementWidget
{
    // Selection state for character merge operations (keyed by widget instance ID)
    private static readonly Dictionary<string, HashSet<ulong>> _selectedCharacterIds = new();
    
    // Selection state for group key merge operations (keyed by widget instance ID)
    private static readonly Dictionary<string, HashSet<string>> _selectedGroupKeys = new();
    
    /// <summary>
    /// Draws the merged sources management section.
    /// Now supports both Character-mode (character IDs) and grouped-mode (group keys).
    /// </summary>
    /// <param name="mergedRowGroups">The list of merged source groups to manage.</param>
    /// <param name="groupingMode">The current table grouping mode.</param>
    /// <param name="getCharacterName">Function to get the display name for a character ID.</param>
    /// <param name="availableCharacterIds">Optional list of available character IDs (for Character mode).</param>
    /// <param name="availableGroupKeys">Optional list of available group keys (for non-Character modes).</param>
    /// <param name="onSettingsChanged">Callback when settings change.</param>
    /// <param name="onRefreshNeeded">Callback when a refresh is needed.</param>
    /// <param name="widgetId">Unique identifier for this widget instance.</param>
    /// <returns>True if any changes were made.</returns>
    public static bool DrawMergedRows(
        List<MergedRowGroup> mergedRowGroups,
        TableGroupingMode groupingMode,
        Func<ulong, string> getCharacterName,
        IEnumerable<ulong>? availableCharacterIds = null,
        IEnumerable<string>? availableGroupKeys = null,
        Action? onSettingsChanged = null,
        Action? onRefreshNeeded = null,
        string widgetId = "default")
    {
        var changed = false;
        var isCharacterMode = groupingMode == TableGroupingMode.Character;
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        if (!ImGui.CollapsingHeader("Source Merging", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return false;
        }
        
        if (isCharacterMode)
        {
            changed = DrawCharacterMergeMode(
                mergedRowGroups, getCharacterName, availableCharacterIds,
                onSettingsChanged, onRefreshNeeded, widgetId);
        }
        else
        {
            changed = DrawGroupKeyMergeMode(
                mergedRowGroups, groupingMode, availableGroupKeys,
                onSettingsChanged, onRefreshNeeded, widgetId);
        }
        
        if (changed)
        {
            onSettingsChanged?.Invoke();
            onRefreshNeeded?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws merge UI for Character grouping mode (uses character IDs).
    /// </summary>
    private static bool DrawCharacterMergeMode(
        List<MergedRowGroup> mergedRowGroups,
        Func<ulong, string> getCharacterName,
        IEnumerable<ulong>? availableCharacterIds,
        Action? onSettingsChanged,
        Action? onRefreshNeeded,
        string widgetId)
    {
        var changed = false;
        
        // Ensure selection state exists for this widget
        if (!_selectedCharacterIds.TryGetValue(widgetId, out var selectedIds))
        {
            selectedIds = new HashSet<ulong>();
            _selectedCharacterIds[widgetId] = selectedIds;
        }
        
        // Build set of merged character IDs (only from Character-mode groups)
        var mergedCharIds = new HashSet<ulong>();
        foreach (var group in mergedRowGroups.Where(g => g.GroupingMode == TableGroupingMode.Character))
        {
            foreach (var id in group.CharacterIds)
            {
                mergedCharIds.Add(id);
            }
        }
        
        // Get all available character IDs
        var allCharIds = availableCharacterIds?.ToList() ?? new List<ulong>();
        
        // Clean up selection - remove IDs that are now merged or no longer available
        selectedIds.RemoveWhere(id => mergedCharIds.Contains(id) || !allCharIds.Contains(id));
        
        if (mergedRowGroups.Count(g => g.GroupingMode == TableGroupingMode.Character) == 0 && allCharIds.Count < 2)
        {
            ImGui.TextDisabled("Need at least 2 sources to enable merging.");
            return false;
        }
        
        int? groupToUnmerge = null;
        
        // === Draw Merged Groups for Character Mode ===
        for (int g = 0; g < mergedRowGroups.Count; g++)
        {
            var group = mergedRowGroups[g];
            
            // Only show Character-mode groups
            if (group.GroupingMode != TableGroupingMode.Character)
                continue;
            
            changed |= DrawMergedGroup(group, g, getCharacterName, ref groupToUnmerge);
        }
        
        // Show count of groups from other modes
        var otherModeCount = mergedRowGroups.Count(g => g.GroupingMode != TableGroupingMode.Character);
        if (otherModeCount > 0)
        {
            ImGui.TextDisabled($"({otherModeCount} group(s) hidden - created in other Group By modes)");
        }
        
        // Add separator if there are merged groups
        if (mergedRowGroups.Any(g => g.GroupingMode == TableGroupingMode.Character) && allCharIds.Count > mergedCharIds.Count)
        {
            ImGui.Spacing();
        }
        
        // === Draw Individual (Unmerged) Sources ===
        foreach (var charId in allCharIds)
        {
            // Skip characters that are part of a merged group
            if (mergedCharIds.Contains(charId))
                continue;
            
            var displayName = getCharacterName(charId);
            var isSelected = selectedIds.Contains(charId);
            
            ImGui.PushID((int)charId);
            
            // Selection checkbox for merging
            if (ImGui.Checkbox("##select", ref isSelected))
            {
                if (isSelected)
                    selectedIds.Add(charId);
                else
                    selectedIds.Remove(charId);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Select for merging");
            }
            
            ImGui.SameLine();
            ImGui.TextUnformatted(displayName);
            
            ImGui.PopID();
        }
        
        // === Merge Action Bar ===
        if (selectedIds.Count >= 2)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 0.4f, 1f));
            if (ImGui.Button($"Merge {selectedIds.Count} Selected"))
            {
                // Create new merged group for Character mode
                var newGroup = new MergedRowGroup
                {
                    Name = "Merged",
                    GroupingMode = TableGroupingMode.Character,
                    CharacterIds = selectedIds.OrderBy(x => x).ToList()
                };
                mergedRowGroups.Add(newGroup);
                selectedIds.Clear();
                changed = true;
                onRefreshNeeded?.Invoke();
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
            {
                // Show what will be merged
                var charNames = selectedIds.Select(getCharacterName).ToList();
                ImGui.SetTooltip($"Merge:\n{string.Join("\n", charNames)}");
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Clear Selection"))
            {
                selectedIds.Clear();
            }
        }
        else if (selectedIds.Count == 1)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select at least 2 sources to merge");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                selectedIds.Clear();
            }
        }
        
        // === Process Actions After Iteration ===
        if (groupToUnmerge.HasValue)
        {
            mergedRowGroups.RemoveAt(groupToUnmerge.Value);
            changed = true;
            onRefreshNeeded?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws merge UI for non-Character grouping modes (uses group keys).
    /// </summary>
    private static bool DrawGroupKeyMergeMode(
        List<MergedRowGroup> mergedRowGroups,
        TableGroupingMode groupingMode,
        IEnumerable<string>? availableGroupKeys,
        Action? onSettingsChanged,
        Action? onRefreshNeeded,
        string widgetId)
    {
        var changed = false;
        var keyId = $"{widgetId}_{groupingMode}";
        
        // Ensure selection state exists for this widget and mode
        if (!_selectedGroupKeys.TryGetValue(keyId, out var selectedKeys))
        {
            selectedKeys = new HashSet<string>();
            _selectedGroupKeys[keyId] = selectedKeys;
        }
        
        // Build set of merged keys (only from matching mode groups)
        var mergedKeys = new HashSet<string>();
        foreach (var group in mergedRowGroups.Where(g => g.GroupingMode == groupingMode))
        {
            foreach (var key in group.GroupKeys)
            {
                mergedKeys.Add(key);
            }
        }
        
        // Get all available group keys
        var allKeys = availableGroupKeys?.ToList() ?? new List<string>();
        
        // Clean up selection
        selectedKeys.RemoveWhere(k => mergedKeys.Contains(k) || !allKeys.Contains(k));
        
        // Show mode indicator
        var modeName = groupingMode switch
        {
            TableGroupingMode.World => "World",
            TableGroupingMode.DataCenter => "Data Center",
            TableGroupingMode.Region => "Region",
            TableGroupingMode.All => "All",
            _ => groupingMode.ToString()
        };
        ImGui.TextDisabled($"Grouping by: {modeName}");
        
        // Special case for "All" mode - only one row, no merge possible
        if (groupingMode == TableGroupingMode.All)
        {
            ImGui.TextDisabled("Cannot merge when grouped to 'All' (single row).");
            return false;
        }
        
        if (mergedRowGroups.Count(g => g.GroupingMode == groupingMode) == 0 && allKeys.Count < 2)
        {
            ImGui.TextDisabled("Need at least 2 sources to enable merging.");
            return false;
        }
        
        int? groupToUnmerge = null;
        
        // === Draw Merged Groups for this Mode ===
        for (int g = 0; g < mergedRowGroups.Count; g++)
        {
            var group = mergedRowGroups[g];
            
            // Only show groups matching current mode
            if (group.GroupingMode != groupingMode)
                continue;
            
            changed |= DrawMergedGroupByKeys(group, g, ref groupToUnmerge);
        }
        
        // Show count of groups from other modes
        var otherModeCount = mergedRowGroups.Count(g => g.GroupingMode != groupingMode);
        if (otherModeCount > 0)
        {
            ImGui.TextDisabled($"({otherModeCount} group(s) hidden - created in other Group By modes)");
        }
        
        // Add separator if there are merged groups
        if (mergedRowGroups.Any(g => g.GroupingMode == groupingMode) && allKeys.Count > mergedKeys.Count)
        {
            ImGui.Spacing();
        }
        
        // === Draw Individual (Unmerged) Sources ===
        foreach (var key in allKeys)
        {
            // Skip keys that are part of a merged group
            if (mergedKeys.Contains(key))
                continue;
            
            var isSelected = selectedKeys.Contains(key);
            
            ImGui.PushID(key.GetHashCode());
            
            // Selection checkbox for merging
            if (ImGui.Checkbox("##select", ref isSelected))
            {
                if (isSelected)
                    selectedKeys.Add(key);
                else
                    selectedKeys.Remove(key);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Select for merging");
            }
            
            ImGui.SameLine();
            ImGui.TextUnformatted(key);
            
            ImGui.PopID();
        }
        
        // === Merge Action Bar ===
        if (selectedKeys.Count >= 2)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 0.4f, 1f));
            if (ImGui.Button($"Merge {selectedKeys.Count} Selected"))
            {
                // Create new merged group for group key mode
                var newGroup = new MergedRowGroup
                {
                    Name = "Merged",
                    GroupingMode = groupingMode,
                    GroupKeys = selectedKeys.OrderBy(x => x).ToList()
                };
                mergedRowGroups.Add(newGroup);
                selectedKeys.Clear();
                changed = true;
                onRefreshNeeded?.Invoke();
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Merge:\n{string.Join("\n", selectedKeys)}");
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Clear Selection"))
            {
                selectedKeys.Clear();
            }
        }
        else if (selectedKeys.Count == 1)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select at least 2 sources to merge");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                selectedKeys.Clear();
            }
        }
        
        // === Process Actions After Iteration ===
        if (groupToUnmerge.HasValue)
        {
            mergedRowGroups.RemoveAt(groupToUnmerge.Value);
            changed = true;
            onRefreshNeeded?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws a single merged group (Character mode - by IDs).
    /// </summary>
    private static bool DrawMergedGroup(
        MergedRowGroup group,
        int groupIndex,
        Func<ulong, string> getCharacterName,
        ref int? groupToUnmerge)
    {
        var changed = false;
        
        ImGui.PushID($"mergedrow_{groupIndex}");
        
        // Merge indicator
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
        ImGui.TextUnformatted("⊕");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Merged group");
        }
        ImGui.SameLine();
        
        // Color picker
        var (colorChanged, newColor) = ImGuiHelpers.ColorPickerWithClear(
            "##color", group.Color, ImGuiHelpers.DefaultColor, "Merged group color");
        if (colorChanged)
        {
            group.Color = newColor;
            changed = true;
        }
        
        ImGui.SameLine();
        
        // Editable name
        ImGui.SetNextItemWidth(120);
        var name = group.Name;
        if (ImGui.InputTextWithHint("##name", "Merged", ref name, 64))
        {
            group.Name = name;
            changed = true;
        }
        
        ImGui.SameLine();
        
        // Show merged sources count
        ImGui.TextDisabled($"[{group.CharacterIds.Count} merged]");
        if (ImGui.IsItemHovered())
        {
            // Build tooltip with character names
            var charNames = group.CharacterIds.Select(getCharacterName).ToList();
            ImGui.SetTooltip(string.Join("\n", charNames));
        }
        
        ImGui.SameLine(0, 16);
        
        // Unmerge button
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.5f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.6f, 1f));
        if (ImGuiHelpers.ButtonAutoWidth("Unmerge##unmerge"))
        {
            groupToUnmerge = groupIndex;
        }
        ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Unmerge back to individual sources");
        }
        
        ImGui.PopID();
        
        return changed;
    }
    
    /// <summary>
    /// Draws a single merged group (Group key mode).
    /// </summary>
    private static bool DrawMergedGroupByKeys(
        MergedRowGroup group,
        int groupIndex,
        ref int? groupToUnmerge)
    {
        var changed = false;
        
        ImGui.PushID($"mergedrow_{groupIndex}");
        
        // Merge indicator
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
        ImGui.TextUnformatted("⊕");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Merged group");
        }
        ImGui.SameLine();
        
        // Color picker
        var (colorChanged, newColor) = ImGuiHelpers.ColorPickerWithClear(
            "##color", group.Color, ImGuiHelpers.DefaultColor, "Merged group color");
        if (colorChanged)
        {
            group.Color = newColor;
            changed = true;
        }
        
        ImGui.SameLine();
        
        // Editable name
        ImGui.SetNextItemWidth(120);
        var name = group.Name;
        if (ImGui.InputTextWithHint("##name", "Merged", ref name, 64))
        {
            group.Name = name;
            changed = true;
        }
        
        ImGui.SameLine();
        
        // Show merged sources count
        ImGui.TextDisabled($"[{group.GroupKeys.Count} merged]");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(string.Join("\n", group.GroupKeys));
        }
        
        ImGui.SameLine(0, 16);
        
        // Unmerge button
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.5f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.6f, 1f));
        if (ImGuiHelpers.ButtonAutoWidth("Unmerge##unmerge"))
        {
            groupToUnmerge = groupIndex;
        }
        ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Unmerge back to individual sources");
        }
        
        ImGui.PopID();
        
        return changed;
    }
}
