using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Centralized widget for managing item/currency columns (or series) with integrated merge support.
/// Used by DataTool and related tools to ensure consistent behavior.
/// </summary>
public static class ColumnManagementWidget
{
    // Selection state for merge operations (keyed by widget instance ID)
    private static readonly Dictionary<string, HashSet<int>> _selectedIndices = new();
    
    /// <summary>
    /// Draws the column/series management UI with integrated merge functionality.
    /// </summary>
    /// <param name="columns">The list of columns to manage.</param>
    /// <param name="mergedColumnGroups">The list of merged column groups.</param>
    /// <param name="getDefaultName">Function to get the default display name for a column.</param>
    /// <param name="onSettingsChanged">Callback when any setting changes.</param>
    /// <param name="onRefreshNeeded">Callback when data refresh is needed.</param>
    /// <param name="sectionTitle">Title for the section (e.g., "Item / Currency Management").</param>
    /// <param name="emptyMessage">Message to show when no columns/series are configured.</param>
    /// <param name="itemLabel">Label for items, e.g., "Item" or "Item (historical data)".</param>
    /// <param name="currencyLabel">Label for currencies, e.g., "Currency" or "Currency (historical data)".</param>
    /// <param name="widgetId">Unique identifier for this widget instance (for selection state).</param>
    /// <param name="isItemHistoricalTrackingEnabled">Function to check if a specific item has historical tracking enabled.</param>
    /// <param name="onItemHistoricalTrackingToggled">Callback when historical tracking is toggled for a specific item (itemId, enabled).</param>
    /// <param name="isCurrencyHistoricalTrackingEnabled">Function to check if a specific currency (TrackedDataType as uint) has historical tracking enabled.</param>
    /// <param name="onCurrencyHistoricalTrackingToggled">Callback when historical tracking is toggled for a specific currency (TrackedDataType as uint, enabled).</param>
    /// <returns>True if any changes were made.</returns>
    public static bool Draw(
        List<ItemColumnConfig> columns,
        List<MergedColumnGroup> mergedColumnGroups,
        Func<ItemColumnConfig, string> getDefaultName,
        Action? onSettingsChanged = null,
        Action? onRefreshNeeded = null,
        string sectionTitle = "Column Management",
        string emptyMessage = "No columns configured.",
        string itemLabel = "Item",
        string currencyLabel = "Currency",
        string widgetId = "default",
        Func<uint, bool>? isItemHistoricalTrackingEnabled = null,
        Action<uint, bool>? onItemHistoricalTrackingToggled = null,
        Func<uint, bool>? isCurrencyHistoricalTrackingEnabled = null,
        Action<uint, bool>? onCurrencyHistoricalTrackingToggled = null)
    {
        var changed = false;
        
        // Ensure selection state exists for this widget
        if (!_selectedIndices.TryGetValue(widgetId, out var selectedIndices))
        {
            selectedIndices = new HashSet<int>();
            _selectedIndices[widgetId] = selectedIndices;
        }
        
        // Build set of merged column indices
        var mergedIndices = new HashSet<int>();
        foreach (var group in mergedColumnGroups)
        {
            foreach (var idx in group.ColumnIndices)
            {
                mergedIndices.Add(idx);
            }
        }
        
        // Clean up selection - remove indices that are now merged
        selectedIndices.RemoveWhere(i => mergedIndices.Contains(i) || i >= columns.Count);
        
        ImGui.TextUnformatted(sectionTitle);
        ImGui.Separator();
        
        if (columns.Count == 0 && mergedColumnGroups.Count == 0)
        {
            ImGui.TextDisabled(emptyMessage);
            return false;
        }
        
        // Track which column/group to delete or swap (can't modify list during iteration)
        int deleteIndex = -1;
        int swapUpIndex = -1;
        int swapDownIndex = -1;
        int groupToUnmerge = -1;
        
        // Use an invisible table for alignment
        // Columns: Select/Indicator | Color | Name | Type | History | Table | Graph | Up | Down | Delete/Unmerge
        var tableFlags = ImGuiTableFlags.None;
        if (ImGui.BeginTable("##columnTable", 10, tableFlags))
        {
            // Setup columns with appropriate widths
            ImGui.TableSetupColumn("##sel", ImGuiTableColumnFlags.WidthFixed, 24f);      // Checkbox or ⊕
            ImGui.TableSetupColumn("##clr", ImGuiTableColumnFlags.WidthFixed, 28f);      // Color picker
            ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch);         // Name input
            ImGui.TableSetupColumn("##type", ImGuiTableColumnFlags.WidthFixed, 70f);     // Type label or merged count
            ImGui.TableSetupColumn("##hist", ImGuiTableColumnFlags.WidthFixed, 24f);     // History checkbox
            ImGui.TableSetupColumn("##tbl", ImGuiTableColumnFlags.WidthFixed, 24f);      // Show in Table checkbox
            ImGui.TableSetupColumn("##grph", ImGuiTableColumnFlags.WidthFixed, 24f);     // Show in Graph checkbox
            ImGui.TableSetupColumn("##up", ImGuiTableColumnFlags.WidthFixed, 24f);       // Move up
            ImGui.TableSetupColumn("##dn", ImGuiTableColumnFlags.WidthFixed, 24f);       // Move down
            ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 80f);      // Delete or Unmerge
            
            // === Draw Merged Groups First ===
            for (int g = 0; g < mergedColumnGroups.Count; g++)
            {
                var group = mergedColumnGroups[g];
                ImGui.PushID($"merged_{g}");
                ImGui.TableNextRow();
                
                // Column 0: Merge indicator
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
                ImGui.TextUnformatted("⊕");
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Merged group");
                }
                
                // Column 1: Color picker
                ImGui.TableNextColumn();
                var (colorChanged, newColor) = ImGuiHelpers.ColorPickerWithClear(
                    "##color", group.Color, ImGuiHelpers.DefaultColor, "Merged group color");
                if (colorChanged)
                {
                    group.Color = newColor;
                    changed = true;
                    onRefreshNeeded?.Invoke();
                }
                
                // Column 2: Editable name
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var name = group.Name;
                if (ImGui.InputTextWithHint("##name", "Merged", ref name, 64))
                {
                    group.Name = name;
                    changed = true;
                }
                
                // Column 3: Show merged items count
                ImGui.TableNextColumn();
                ImGui.TextDisabled($"[{group.ColumnIndices.Count} merged]");
                if (ImGui.IsItemHovered())
                {
                    // Build tooltip with item names
                    var itemNames = group.ColumnIndices
                        .Where(idx => idx >= 0 && idx < columns.Count)
                        .Select(idx => getDefaultName(columns[idx]))
                        .ToList();
                    ImGui.SetTooltip(string.Join("\n", itemNames));
                }
                
                // Column 4: Historical tracking indicator for merged groups
                ImGui.TableNextColumn();
                {
                    // Get member columns and check their historical tracking status
                    var memberColumns = group.ColumnIndices
                        .Where(idx => idx >= 0 && idx < columns.Count)
                        .Select(idx => columns[idx])
                        .ToList();
                    
                    // Count items with historical tracking (check currencies against currency tracking settings)
                    var trackedCount = memberColumns.Count(c => 
                        c.IsCurrency 
                            ? (isCurrencyHistoricalTrackingEnabled?.Invoke(c.Id) ?? true)
                            : (isItemHistoricalTrackingEnabled?.Invoke(c.Id) ?? c.StoreHistory));
                    var totalCount = memberColumns.Count;
                    
                    // Determine state: all, some, or none
                    bool allTracked = trackedCount == totalCount;
                    bool noneTracked = trackedCount == 0;
                    bool someTracked = !allTracked && !noneTracked;
                    
                    // Draw a visual indicator based on state
                    if (allTracked)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.9f, 0.4f, 1.0f));
                        ImGui.TextUnformatted("✓");
                        ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("All items in this group have historical tracking enabled.");
                        }
                    }
                    else if (noneTracked)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
                        ImGui.TextUnformatted("○");
                        ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("No items in this group have historical tracking enabled.");
                        }
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.2f, 1.0f));
                        ImGui.TextUnformatted("◐");
                        ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                        {
                            // Build list of items without tracking
                            var untrackedItems = memberColumns
                                .Where(c => c.IsCurrency
                                    ? !(isCurrencyHistoricalTrackingEnabled?.Invoke(c.Id) ?? true)
                                    : !(isItemHistoricalTrackingEnabled?.Invoke(c.Id) ?? c.StoreHistory))
                                .Select(c => getDefaultName(c))
                                .ToList();
                            ImGui.SetTooltip($"Some items have historical tracking enabled ({trackedCount}/{totalCount}).\n\nItems without tracking:\n{string.Join("\n", untrackedItems)}");
                        }
                    }
                }
                
                // Column 5: Show in Table checkbox for merged groups
                ImGui.TableNextColumn();
                {
                    var showInTable = group.ShowInTable;
                    if (ImGui.Checkbox("##showInTable", ref showInTable))
                    {
                        group.ShowInTable = showInTable;
                        changed = true;
                        onRefreshNeeded?.Invoke();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Show this merged group in table view");
                    }
                }
                
                // Column 6: Show in Graph - enabled only when all items have historical tracking
                ImGui.TableNextColumn();
                {
                    // Get member columns and check their historical tracking status
                    var memberColumns = group.ColumnIndices
                        .Where(idx => idx >= 0 && idx < columns.Count)
                        .Select(idx => columns[idx])
                        .ToList();
                    
                    // Check if all items have historical tracking (check currencies against currency tracking settings)
                    var untrackedItems = memberColumns
                        .Where(c => c.IsCurrency
                            ? !(isCurrencyHistoricalTrackingEnabled?.Invoke(c.Id) ?? true)
                            : !(isItemHistoricalTrackingEnabled?.Invoke(c.Id) ?? c.StoreHistory))
                        .Select(c => getDefaultName(c))
                        .ToList();
                    
                    bool hasUntrackedItems = untrackedItems.Count > 0;
                    
                    // Enable the checkbox only when all items have historical tracking
                    ImGui.BeginDisabled(hasUntrackedItems);
                    var showInGraph = group.ShowInGraph;
                    if (ImGui.Checkbox("##showInGraph", ref showInGraph))
                    {
                        group.ShowInGraph = showInGraph;
                        changed = true;
                        onRefreshNeeded?.Invoke();
                    }
                    ImGui.EndDisabled();
                    
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        if (hasUntrackedItems)
                        {
                            ImGui.SetTooltip($"Graph view is disabled because the following items don't have historical tracking enabled:\n{string.Join("\n", untrackedItems)}");
                        }
                        else
                        {
                            ImGui.SetTooltip("Show this merged group as a combined series in graph view.\nThe graph will display the sum of all items in this group.");
                        }
                    }
                };
                
                // Column 7: Move up button for merged groups
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(g == 0);
                if (ImGui.Button("▲##up"))
                {
                    // Move merged group up
                    var temp = mergedColumnGroups[g];
                    mergedColumnGroups[g] = mergedColumnGroups[g - 1];
                    mergedColumnGroups[g - 1] = temp;
                    changed = true;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Move up");
                }
                
                // Column 8: Move down button for merged groups
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(g == mergedColumnGroups.Count - 1);
                if (ImGui.Button("▼##down"))
                {
                    // Move merged group down
                    var temp = mergedColumnGroups[g];
                    mergedColumnGroups[g] = mergedColumnGroups[g + 1];
                    mergedColumnGroups[g + 1] = temp;
                    changed = true;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Move down");
                }
                
                // Column 9: Unmerge button
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.5f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.6f, 1f));
                if (ImGuiHelpers.ButtonAutoWidth("Unmerge##unmerge"))
                {
                    groupToUnmerge = g;
                }
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Unmerge back to individual items");
                }
                
                ImGui.PopID();
            }
            
            // === Draw Individual (Unmerged) Columns ===
            for (int i = 0; i < columns.Count; i++)
            {
                // Skip columns that are part of a merged group
                if (mergedIndices.Contains(i))
                    continue;
                
                var column = columns[i];
                var defaultName = getDefaultName(column);
                var isSelected = selectedIndices.Contains(i);
                
                ImGui.PushID(i);
                ImGui.TableNextRow();
                
                // Column 0: Selection checkbox
                ImGui.TableNextColumn();
                if (ImGui.Checkbox("##select", ref isSelected))
                {
                    if (isSelected)
                        selectedIndices.Add(i);
                    else
                        selectedIndices.Remove(i);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Select for merging");
                }
                
                // Column 1: Color picker
                ImGui.TableNextColumn();
                var (colorChanged, newColor) = ImGuiHelpers.ColorPickerWithClear(
                    "##color", column.Color, ImGuiHelpers.DefaultColor, "Color");
                if (colorChanged)
                {
                    column.Color = newColor;
                    changed = true;
                    onRefreshNeeded?.Invoke();
                }
                
                // Column 2: Custom name input
                ImGui.TableNextColumn();
                var customName = column.CustomName ?? string.Empty;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputTextWithHint("##name", defaultName, ref customName, 64))
                {
                    column.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName;
                    changed = true;
                }
                
                // Column 3: Type label
                ImGui.TableNextColumn();
                ImGui.TextDisabled(column.IsCurrency ? "[Currency]" : "[Item]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(column.IsCurrency ? currencyLabel : itemLabel);
                }
                
                // Column 4: Store history checkbox (per-item global setting) or currency tracking setting
                ImGui.TableNextColumn();
                if (column.IsCurrency)
                {
                    // Currencies have history tracking controlled by the currency tracking settings
                    var currencyHistory = isCurrencyHistoricalTrackingEnabled?.Invoke(column.Id) ?? column.StoreHistory;
                    if (onCurrencyHistoricalTrackingToggled != null)
                    {
                        if (ImGui.Checkbox("##history", ref currencyHistory))
                        {
                            onCurrencyHistoricalTrackingToggled(column.Id, currencyHistory);
                            changed = true;
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("Currency tracking is always enabled and cannot be turned off.");
                        }
                    }
                    else
                    {
                        // No callback provided - show disabled checkbox
                        ImGui.BeginDisabled(true);
                        ImGui.Checkbox("##history", ref currencyHistory);
                        ImGui.EndDisabled();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        {
                            ImGui.SetTooltip("Currency historical tracking is controlled globally.\nThis can be changed in Kaleidoscope settings.");
                        }
                    }
                }
                else
                {
                    // Use per-item global historical tracking setting if available
                    var storeHistory = isItemHistoricalTrackingEnabled?.Invoke(column.Id) ?? column.StoreHistory;
                    if (ImGui.Checkbox("##history", ref storeHistory))
                    {
                        // Toggle per-item global setting if callback is provided, otherwise use per-column
                        if (onItemHistoricalTrackingToggled != null)
                        {
                            onItemHistoricalTrackingToggled(column.Id, storeHistory);
                        }
                        else
                        {
                            column.StoreHistory = storeHistory;
                        }
                        changed = true;
                    }
                    if (ImGui.IsItemHovered())
                    {
                        if (onItemHistoricalTrackingToggled != null)
                        {
                            ImGui.SetTooltip("Enable/disable historical time-series tracking for this item.\nThis setting applies across all tools in the project.");
                        }
                        else
                        {
                            ImGui.SetTooltip("Store historical time-series data for this item");
                        }
                    }
                }
                
                // Column 5: Show in Table checkbox
                ImGui.TableNextColumn();
                var showInTable = column.ShowInTable;
                if (ImGui.Checkbox("##showInTable", ref showInTable))
                {
                    column.ShowInTable = showInTable;
                    changed = true;
                    onRefreshNeeded?.Invoke();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Show this item/currency in table view");
                }
                
                // Column 6: Show in Graph checkbox
                ImGui.TableNextColumn();
                var showInGraph = column.ShowInGraph;
                if (ImGui.Checkbox("##showInGraph", ref showInGraph))
                {
                    column.ShowInGraph = showInGraph;
                    changed = true;
                    onRefreshNeeded?.Invoke();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Show this item/currency in graph view");
                }
                
                // Column 7: Move up button
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(i == 0);
                if (ImGui.Button("▲##up"))
                {
                    swapUpIndex = i;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Move up");
                }
                
                // Column 8: Move down button
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(i == columns.Count - 1);
                if (ImGui.Button("▼##down"))
                {
                    swapDownIndex = i;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Move down");
                }
                
                // Column 9: Delete button
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.15f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.2f, 0.2f, 1f));
                if (ImGui.Button("×##del"))
                {
                    deleteIndex = i;
                }
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Remove");
                }
                
                ImGui.PopID();
            }
            
            ImGui.EndTable();
        }
        
        // === Merge Action Bar ===
        if (selectedIndices.Count >= 2)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 0.4f, 1f));
            if (ImGui.Button($"Merge {selectedIndices.Count} Selected"))
            {
                // Create new merged group
                var newGroup = new MergedColumnGroup
                {
                    Name = "Merged",
                    ColumnIndices = selectedIndices.OrderBy(x => x).ToList(),
                    Width = 80f
                };
                mergedColumnGroups.Add(newGroup);
                selectedIndices.Clear();
                changed = true;
                onRefreshNeeded?.Invoke();
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
            {
                // Show what will be merged
                var itemNames = selectedIndices
                    .Where(idx => idx >= 0 && idx < columns.Count)
                    .Select(idx => getDefaultName(columns[idx]))
                    .ToList();
                ImGui.SetTooltip($"Merge:\n{string.Join("\n", itemNames)}");
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Clear Selection"))
            {
                selectedIndices.Clear();
            }
        }
        else if (selectedIndices.Count == 1)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select at least 2 items to merge");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                selectedIndices.Clear();
            }
        }
        
        // === Process Actions After Iteration ===
        
        // Handle unmerge
        if (groupToUnmerge >= 0)
        {
            mergedColumnGroups.RemoveAt(groupToUnmerge);
            changed = true;
            onRefreshNeeded?.Invoke();
        }
        
        // Process reordering and deletion
        if (swapUpIndex > 0)
        {
            (columns[swapUpIndex - 1], columns[swapUpIndex]) = (columns[swapUpIndex], columns[swapUpIndex - 1]);
            
            // Update merged group indices
            UpdateMergedIndicesAfterSwap(mergedColumnGroups, swapUpIndex - 1, swapUpIndex);
            
            // Update selection
            if (selectedIndices.Contains(swapUpIndex))
            {
                selectedIndices.Remove(swapUpIndex);
                selectedIndices.Add(swapUpIndex - 1);
            }
            else if (selectedIndices.Contains(swapUpIndex - 1))
            {
                selectedIndices.Remove(swapUpIndex - 1);
                selectedIndices.Add(swapUpIndex);
            }
            
            changed = true;
        }
        else if (swapDownIndex >= 0 && swapDownIndex < columns.Count - 1)
        {
            (columns[swapDownIndex + 1], columns[swapDownIndex]) = (columns[swapDownIndex], columns[swapDownIndex + 1]);
            
            // Update merged group indices
            UpdateMergedIndicesAfterSwap(mergedColumnGroups, swapDownIndex, swapDownIndex + 1);
            
            // Update selection
            if (selectedIndices.Contains(swapDownIndex))
            {
                selectedIndices.Remove(swapDownIndex);
                selectedIndices.Add(swapDownIndex + 1);
            }
            else if (selectedIndices.Contains(swapDownIndex + 1))
            {
                selectedIndices.Remove(swapDownIndex + 1);
                selectedIndices.Add(swapDownIndex);
            }
            
            changed = true;
        }
        else if (deleteIndex >= 0)
        {
            // Remove from selection
            selectedIndices.Remove(deleteIndex);
            
            // Update merged group indices (shift down all indices > deleteIndex)
            UpdateMergedIndicesAfterDelete(mergedColumnGroups, deleteIndex);
            
            // Update selection (shift down all indices > deleteIndex)
            var updatedSelection = selectedIndices
                .Select(idx => idx > deleteIndex ? idx - 1 : idx)
                .ToHashSet();
            selectedIndices.Clear();
            foreach (var idx in updatedSelection)
                selectedIndices.Add(idx);
            
            columns.RemoveAt(deleteIndex);
            changed = true;
            onRefreshNeeded?.Invoke();
        }
        
        if (changed)
        {
            onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
    private static void UpdateMergedIndicesAfterSwap(List<MergedColumnGroup> groups, int idx1, int idx2)
    {
        foreach (var group in groups)
        {
            for (int i = 0; i < group.ColumnIndices.Count; i++)
            {
                if (group.ColumnIndices[i] == idx1)
                    group.ColumnIndices[i] = idx2;
                else if (group.ColumnIndices[i] == idx2)
                    group.ColumnIndices[i] = idx1;
            }
        }
    }
    
    private static void UpdateMergedIndicesAfterDelete(List<MergedColumnGroup> groups, int deletedIndex)
    {
        for (int g = groups.Count - 1; g >= 0; g--)
        {
            var group = groups[g];
            
            // Remove the deleted index from the group
            group.ColumnIndices.Remove(deletedIndex);
            
            // Shift down all indices > deletedIndex
            for (int i = 0; i < group.ColumnIndices.Count; i++)
            {
                if (group.ColumnIndices[i] > deletedIndex)
                    group.ColumnIndices[i]--;
            }
            
            // Remove the group if it has less than 2 members
            if (group.ColumnIndices.Count < 2)
            {
                groups.RemoveAt(g);
            }
        }
    }
    
    /// <summary>
    /// Backward-compatible overload that ignores merged groups.
    /// </summary>
    public static bool Draw(
        List<ItemColumnConfig> columns,
        Func<ItemColumnConfig, string> getDefaultName,
        Action? onSettingsChanged = null,
        Action? onRefreshNeeded = null,
        string sectionTitle = "Item / Currency Management",
        string emptyMessage = "No columns configured.",
        HashSet<int>? mergedColumnIndices = null,
        string itemLabel = "Item",
        string currencyLabel = "Currency")
    {
        // Create a temporary empty list for backward compatibility
        var emptyMergedGroups = new List<MergedColumnGroup>();
        return Draw(
            columns,
            emptyMergedGroups,
            getDefaultName,
            onSettingsChanged,
            onRefreshNeeded,
            sectionTitle,
            emptyMessage,
            itemLabel,
            currencyLabel,
            "legacy");
    }
    
    /// <summary>
    /// Adds a column/series if it doesn't already exist.
    /// </summary>
    /// <returns>True if the column was added.</returns>
    public static bool AddColumn(List<ItemColumnConfig> columns, uint id, bool isCurrency)
    {
        if (columns.Any(c => c.Id == id && c.IsCurrency == isCurrency))
            return false;
        
        columns.Add(new ItemColumnConfig
        {
            Id = id,
            IsCurrency = isCurrency
        });
        
        return true;
    }
    
    /// <summary>
    /// Exports column configurations to a list of dictionaries for serialization.
    /// </summary>
    public static List<Dictionary<string, object?>> ExportColumns(IEnumerable<ItemColumnConfig> columns)
    {
        return columns.Select(c => new Dictionary<string, object?>
        {
            ["Id"] = c.Id,
            ["CustomName"] = c.CustomName,
            ["IsCurrency"] = c.IsCurrency,
            ["Color"] = c.Color.HasValue 
                ? new float[] { c.Color.Value.X, c.Color.Value.Y, c.Color.Value.Z, c.Color.Value.W } 
                : null,
            ["Width"] = c.Width,
            ["StoreHistory"] = c.StoreHistory,
            ["ShowInTable"] = c.ShowInTable,
            ["ShowInGraph"] = c.ShowInGraph
        }).ToList();
    }
    
    /// <summary>
    /// Imports column configurations from serialized data.
    /// </summary>
    public static List<ItemColumnConfig> ImportColumns(object? columnsObj)
    {
        var result = new List<ItemColumnConfig>();
        if (columnsObj == null) return result;
        
        try
        {
            // Handle Newtonsoft.Json JArray (used by ConfigManager)
            if (columnsObj is Newtonsoft.Json.Linq.JArray jArray)
            {
                foreach (var token in jArray)
                {
                    if (token is not Newtonsoft.Json.Linq.JObject jObj) continue;
                    result.Add(ImportColumnFromJObject(jObj));
                }
                return result;
            }
            
            // Handle System.Text.Json.JsonElement
            if (columnsObj is System.Text.Json.JsonElement jsonElement && 
                jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in jsonElement.EnumerateArray())
                {
                    result.Add(ImportColumnFromJsonElement(element));
                }
                return result;
            }
            
            // Handle in-memory List<Dictionary<string, object?>>
            if (columnsObj is System.Collections.IEnumerable enumerable)
            {
                foreach (var obj in enumerable)
                {
                    if (obj is IDictionary<string, object?> dict)
                    {
                        result.Add(ImportColumnFromDictionary(dict));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ColumnManagementWidget] Error importing columns: {ex.Message}");
        }
        
        return result;
    }
    
    private static ItemColumnConfig ImportColumnFromJObject(Newtonsoft.Json.Linq.JObject jObj)
    {
        var item = new ItemColumnConfig
        {
            Id = jObj["Id"]?.ToObject<uint>() ?? 0,
            CustomName = jObj["CustomName"]?.ToObject<string>(),
            IsCurrency = jObj["IsCurrency"]?.ToObject<bool>() ?? false,
            Width = jObj["Width"]?.ToObject<float>() ?? 80f,
            StoreHistory = jObj["StoreHistory"]?.ToObject<bool>() ?? false,
            ShowInTable = jObj["ShowInTable"]?.ToObject<bool>() ?? true,
            ShowInGraph = jObj["ShowInGraph"]?.ToObject<bool>() ?? true
        };
        
        var colorToken = jObj["Color"];
        if (colorToken is Newtonsoft.Json.Linq.JArray colorArr && colorArr.Count >= 4)
        {
            item.Color = new Vector4(
                colorArr[0].ToObject<float>(),
                colorArr[1].ToObject<float>(),
                colorArr[2].ToObject<float>(),
                colorArr[3].ToObject<float>());
        }
        
        return item;
    }
    
    private static ItemColumnConfig ImportColumnFromJsonElement(System.Text.Json.JsonElement element)
    {
        var item = new ItemColumnConfig
        {
            Id = element.TryGetProperty("Id", out var idProp) ? idProp.GetUInt32() : 0,
            CustomName = element.TryGetProperty("CustomName", out var nameProp) && 
                         nameProp.ValueKind != System.Text.Json.JsonValueKind.Null 
                         ? nameProp.GetString() : null,
            IsCurrency = element.TryGetProperty("IsCurrency", out var currProp) && currProp.GetBoolean(),
            Width = element.TryGetProperty("Width", out var widthProp) ? widthProp.GetSingle() : 80f,
            StoreHistory = element.TryGetProperty("StoreHistory", out var histProp) && histProp.GetBoolean(),
            ShowInTable = !element.TryGetProperty("ShowInTable", out var tableProp) || tableProp.GetBoolean(),
            ShowInGraph = !element.TryGetProperty("ShowInGraph", out var graphProp) || graphProp.GetBoolean()
        };
        
        if (element.TryGetProperty("Color", out var colorProp) && 
            colorProp.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var colorArr = colorProp.EnumerateArray().Select(v => v.GetSingle()).ToArray();
            if (colorArr.Length >= 4)
                item.Color = new Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
        }
        
        return item;
    }
    
    private static ItemColumnConfig ImportColumnFromDictionary(IDictionary<string, object?> dict)
    {
        var item = new ItemColumnConfig
        {
            Id = dict.TryGetValue("Id", out var idVal) && idVal != null ? Convert.ToUInt32(idVal) : 0,
            CustomName = dict.TryGetValue("CustomName", out var nameVal) ? nameVal?.ToString() : null,
            IsCurrency = dict.TryGetValue("IsCurrency", out var currVal) && currVal is bool b && b,
            Width = dict.TryGetValue("Width", out var widthVal) && widthVal != null ? Convert.ToSingle(widthVal) : 80f,
            StoreHistory = dict.TryGetValue("StoreHistory", out var histVal) && histVal is bool h && h,
            ShowInTable = !dict.TryGetValue("ShowInTable", out var tableVal) || (tableVal is bool t && t),
            ShowInGraph = !dict.TryGetValue("ShowInGraph", out var graphVal) || (graphVal is bool g && g)
        };
        
        if (dict.TryGetValue("Color", out var colorVal) && colorVal is float[] colorArr && colorArr.Length >= 4)
        {
            item.Color = new Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
        }
        
        return item;
    }
}
