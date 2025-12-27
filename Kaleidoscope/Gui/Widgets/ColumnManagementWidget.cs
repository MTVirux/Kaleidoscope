using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Centralized widget for managing item/currency columns (or series).
/// Used by DataTool and related tools to ensure consistent behavior.
/// </summary>
public static class ColumnManagementWidget
{
    /// <summary>
    /// Draws the column/series management UI.
    /// </summary>
    /// <param name="columns">The list of columns to manage.</param>
    /// <param name="getDefaultName">Function to get the default display name for a column.</param>
    /// <param name="onSettingsChanged">Callback when any setting changes.</param>
    /// <param name="onRefreshNeeded">Callback when data refresh is needed.</param>
    /// <param name="sectionTitle">Title for the section (e.g., "Column Management" or "Series Management").</param>
    /// <param name="emptyMessage">Message to show when no columns/series are configured.</param>
    /// <param name="mergedColumnIndices">Optional set of column indices that are merged (will be skipped).</param>
    /// <param name="itemLabel">Label for items, e.g., "Item" or "Item (historical data)".</param>
    /// <param name="currencyLabel">Label for currencies, e.g., "Currency" or "Currency (historical data)".</param>
    /// <returns>True if any changes were made.</returns>
    public static bool Draw(
        List<ItemColumnConfig> columns,
        Func<ItemColumnConfig, string> getDefaultName,
        Action? onSettingsChanged = null,
        Action? onRefreshNeeded = null,
        string sectionTitle = "Column Management",
        string emptyMessage = "No columns configured.",
        HashSet<int>? mergedColumnIndices = null,
        string itemLabel = "Item",
        string currencyLabel = "Currency")
    {
        var changed = false;
        
        ImGui.TextUnformatted(sectionTitle);
        ImGui.Separator();
        
        if (columns.Count == 0)
        {
            ImGui.TextDisabled(emptyMessage);
            return false;
        }
        
        // Track which column to delete or swap (can't modify list during iteration)
        int deleteIndex = -1;
        int swapUpIndex = -1;
        int swapDownIndex = -1;
        
        for (int i = 0; i < columns.Count; i++)
        {
            // Skip columns that are part of a merged group
            if (mergedColumnIndices?.Contains(i) == true)
                continue;
            
            var column = columns[i];
            var defaultName = getDefaultName(column);
            
            ImGui.PushID(i);
            
            // Color picker
            var color = column.Color ?? new Vector4(0.5f, 0.5f, 0.5f, 1f);
            if (ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf))
            {
                column.Color = color;
                changed = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Color");
            }
            
            ImGui.SameLine();
            
            // Custom name input
            var customName = column.CustomName ?? string.Empty;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputTextWithHint("##name", defaultName, ref customName, 64))
            {
                column.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName;
                changed = true;
            }
            
            ImGui.SameLine();
            
            // Type label
            ImGui.TextDisabled(column.IsCurrency ? "[C]" : "[I]");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(column.IsCurrency ? currencyLabel : itemLabel);
            }
            
            ImGui.SameLine();
            
            // Store history checkbox (only for items, not currencies)
            if (!column.IsCurrency)
            {
                var storeHistory = column.StoreHistory;
                if (ImGui.Checkbox("##history", ref storeHistory))
                {
                    column.StoreHistory = storeHistory;
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Store historical time-series data for this item");
                }
                ImGui.SameLine();
            }
            
            // Move up button
            ImGui.BeginDisabled(i == 0);
            if (ImGui.Button("▲##up", new Vector2(20, 0)))
            {
                swapUpIndex = i;
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Move up");
            }
            
            ImGui.SameLine();
            
            // Move down button
            ImGui.BeginDisabled(i == columns.Count - 1);
            if (ImGui.Button("▼##down", new Vector2(20, 0)))
            {
                swapDownIndex = i;
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Move down");
            }
            
            ImGui.SameLine();
            
            // Delete button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.15f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.2f, 0.2f, 1f));
            if (ImGui.Button("×##del", new Vector2(20, 0)))
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
        
        // Process reordering and deletion after iteration
        if (swapUpIndex > 0)
        {
            (columns[swapUpIndex - 1], columns[swapUpIndex]) = (columns[swapUpIndex], columns[swapUpIndex - 1]);
            changed = true;
        }
        else if (swapDownIndex >= 0 && swapDownIndex < columns.Count - 1)
        {
            (columns[swapDownIndex + 1], columns[swapDownIndex]) = (columns[swapDownIndex], columns[swapDownIndex + 1]);
            changed = true;
        }
        else if (deleteIndex >= 0)
        {
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
            ["StoreHistory"] = c.StoreHistory
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
            StoreHistory = jObj["StoreHistory"]?.ToObject<bool>() ?? false
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
            StoreHistory = element.TryGetProperty("StoreHistory", out var histProp) && histProp.GetBoolean()
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
            StoreHistory = dict.TryGetValue("StoreHistory", out var histVal) && histVal is bool h && h
        };
        
        if (dict.TryGetValue("Color", out var colorVal) && colorVal is float[] colorArr && colorArr.Length >= 4)
        {
            item.Color = new Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
        }
        
        return item;
    }
}
