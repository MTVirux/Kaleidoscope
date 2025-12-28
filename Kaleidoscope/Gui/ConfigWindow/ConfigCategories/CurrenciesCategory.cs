using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Currency/resource color management and tracking category in the config window.
/// Provides options to set custom colors for tracked data types used across all tools,
/// and displays which currencies are being tracked (always enabled).
/// </summary>
public class CurrenciesCategory
{
    private readonly ConfigurationService _configService;
    private readonly TrackedDataRegistry _registry;
    private readonly ITextureProvider? _textureProvider;
    private readonly ItemDataService? _itemDataService;
    
    // Color editing state
    private TrackedDataType? _editingColorType = null;
    private Vector4 _colorEditBuffer = Vector4.One;
    
    // Search state
    private string _searchFilter = string.Empty;

    // Friendly display names for categories
    private static readonly Dictionary<TrackedDataCategory, string> CategoryDisplayNames = new()
    {
        { TrackedDataCategory.Gil, "Gil" },
        { TrackedDataCategory.Tomestone, "Tomestone" },
        { TrackedDataCategory.Scrip, "Scrip" },
        { TrackedDataCategory.GrandCompany, "Grand Company" },
        { TrackedDataCategory.PvP, "PvP" },
        { TrackedDataCategory.Hunt, "Hunt" },
        { TrackedDataCategory.GoldSaucer, "Gold Saucer" },
        { TrackedDataCategory.Tribal, "Tribal" },
        { TrackedDataCategory.Universalis, "Universalis / Value" },
    };

    private static string GetCategoryDisplayName(TrackedDataCategory category)
        => CategoryDisplayNames.TryGetValue(category, out var name) ? name : category.ToString();

    public CurrenciesCategory(
        ConfigurationService configService, 
        TrackedDataRegistry registry,
        ITextureProvider? textureProvider = null,
        ItemDataService? itemDataService = null)
    {
        _configService = configService;
        _registry = registry;
        _textureProvider = textureProvider;
        _itemDataService = itemDataService;
    }

    public void Draw()
    {
        // Currency Tracking Settings Section
        ImGui.TextUnformatted("Currency Tracking");
        ImGui.Separator();
        ImGui.TextWrapped("All currencies are automatically tracked. Historical data is recorded for all currency types below.");
        ImGui.Spacing();

        // Show tracking status (always enabled)
        var trackingEnabled = true;
        ImGui.BeginDisabled();
        ImGui.Checkbox("Currency tracking enabled", ref trackingEnabled);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Currency tracking is always enabled and cannot be turned off.");
        }

        ImGui.Spacing();
        ImGui.Spacing();

        // Currency & Resource Colors Section
        ImGui.TextUnformatted("Currency & Resource Colors");
        ImGui.Separator();
        ImGui.TextWrapped("Set a custom color for each tracked currency or resource. " +
            "These colors are used consistently across all tools (graphs, tables, etc.).");
        ImGui.Spacing();
        
        // Search bar
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##search", "Search currencies...", ref _searchFilter, 100);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _searchFilter = string.Empty;
        }
        ImGui.Spacing();

        var config = _configService.Config;
        var definitions = _registry.Definitions;

        if (definitions.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No tracked data types available.");
            return;
        }

        // Apply search filter
        var filteredDefinitions = string.IsNullOrWhiteSpace(_searchFilter)
            ? definitions.Values
            : definitions.Values.Where(d => 
                d.DisplayName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                d.Category.ToString().Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                (d.Description?.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ?? false));
        
        var categories = filteredDefinitions
            .GroupBy(d => d.Category)
            .OrderBy(g => g.Key)
            .ToList();
        
        if (categories.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No currencies match your search.");
            return;
        }

        // Draw table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        
        // Calculate available height for table
        var availableHeight = ImGui.GetContentRegionAvail().Y - 30;
        if (availableHeight < 100) availableHeight = 100;

        // Account for scrollbar width in fixed columns
        var scrollbarWidth = ImGui.GetStyle().ScrollbarSize;
        
        if (ImGui.BeginTable("ItemColorsTable", 6, tableFlags, new Vector2(0, availableHeight)))
        {
            // Setup columns
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("##Icon", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, ImGuiHelpers.IconSize + 4);
            ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Tracked", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 55 + scrollbarWidth);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var categoryGroup in categories)
            {
                // Custom sort order for Gil category: Gil > Retainer Gil > FC Gil
                var orderedItems = categoryGroup.Key == TrackedDataCategory.Gil
                    ? categoryGroup.OrderBy(d => d.Type switch
                    {
                        TrackedDataType.Gil => 0,
                        TrackedDataType.RetainerGil => 1,
                        TrackedDataType.FreeCompanyGil => 2,
                        _ => 99
                    })
                    : categoryGroup.OrderBy(d => d.DisplayName);
                
                foreach (var definition in orderedItems)
                {
                    ImGui.TableNextRow();

                    // Category column
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(GetCategoryDisplayName(categoryGroup.Key));

                    // Icon column
                    ImGui.TableNextColumn();
                    DrawCurrencyIcon(definition);

                    // Item name column
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(definition.DisplayName);
                    if (!string.IsNullOrEmpty(definition.Description) && ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(definition.Description);
                        ImGui.EndTooltip();
                    }

                    // Tracked column (always enabled, read-only)
                    ImGui.TableNextColumn();
                    ImGui.PushID($"tracked_{(int)definition.Type}");
                    var isTracked = true;
                    ImGui.BeginDisabled();
                    ImGui.Checkbox("##tracked", ref isTracked);
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip("Currency tracking is always enabled and cannot be turned off.");
                    }
                    ImGui.PopID();

                    // Color column
                    ImGui.TableNextColumn();
                    DrawColorCell(definition.Type, config);

                    // Actions column
                    ImGui.TableNextColumn();
                    DrawActionsCell(definition.Type, config);
                }
            }

            ImGui.EndTable();
        }

        // Summary
        ImGui.Spacing();
        var colorCount = config.ItemColors.Count;
        var totalCount = definitions.Count;
        var filteredCount = categories.Sum(c => c.Count());
        var summaryText = string.IsNullOrWhiteSpace(_searchFilter)
            ? $"{totalCount} currencies total, {colorCount} with custom colors"
            : $"Showing {filteredCount} of {totalCount} currencies, {colorCount} with custom colors";
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), summaryText);
    }

    private void DrawColorCell(TrackedDataType dataType, Configuration config)
    {
        ImGui.PushID((int)dataType);
        
        var hasColor = config.ItemColors.TryGetValue(dataType, out var colorUint);
        Vector4 colorValue;
        
        if (_editingColorType == dataType)
        {
            colorValue = _colorEditBuffer;
        }
        else if (hasColor)
        {
            colorValue = ColorUtils.UintToVector4(colorUint);
        }
        else
        {
            colorValue = new Vector4(0.5f, 0.5f, 0.5f, 1f);
        }
        
        if (!hasColor && _editingColorType != dataType)
        {
            // Draw placeholder color button
            if (ImGui.ColorButton("##colorPreview", new Vector4(0.3f, 0.3f, 0.3f, 0.5f),
                ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20)))
            {
                // Start editing with a default color
                _editingColorType = dataType;
                _colorEditBuffer = new Vector4(1f, 1f, 1f, 1f);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Click to set a custom color");
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Auto");
        }
        else
        {
            // Color picker for set colors or when actively editing
            if (ImGui.ColorEdit4("##color", ref colorValue,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar))
            {
                _colorEditBuffer = colorValue;
            }
            
            // Track when we start editing (for already-set colors)
            if (ImGui.IsItemActivated() && hasColor)
            {
                _editingColorType = dataType;
                _colorEditBuffer = colorValue;
            }
            
            // Save when the user finishes editing
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                SaveItemColor(dataType, ColorUtils.Vector4ToUint(_colorEditBuffer));
                _editingColorType = null;
            }
        }
        
        ImGui.PopID();
    }

    private void DrawActionsCell(TrackedDataType dataType, Configuration config)
    {
        var hasColor = config.ItemColors.ContainsKey(dataType);
        
        if (hasColor || _editingColorType == dataType)
        {
            ImGui.PushID((int)dataType);
            if (ImGui.SmallButton("X"))
            {
                SaveItemColor(dataType, null);
                _editingColorType = null;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Clear custom color");
                ImGui.EndTooltip();
            }
            ImGui.PopID();
        }
    }

    private void SaveItemColor(TrackedDataType dataType, uint? color)
    {
        try
        {
            var config = _configService.Config;
            
            if (color.HasValue)
            {
                config.ItemColors[dataType] = color.Value;
            }
            else
            {
                config.ItemColors.Remove(dataType);
            }
            
            _configService.Save();
            LogService.Debug($"[CurrenciesCategory] Saved color for {dataType}: {color?.ToString("X8") ?? "(cleared)"}");
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to save item color for {dataType}", ex);
        }
    }

    private void DrawCurrencyIcon(TrackedDataDefinition definition)
    {
        if (_textureProvider == null || _itemDataService == null || !definition.ItemId.HasValue)
        {
            ImGui.Dummy(new Vector2(ImGuiHelpers.IconSize));
            return;
        }

        try
        {
            var iconId = _itemDataService.GetItemIconId(definition.ItemId.Value);
            if (iconId > 0)
            {
                var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                if (icon.TryGetWrap(out var wrap, out _))
                {
                    ImGui.Image(wrap.Handle, new Vector2(ImGuiHelpers.IconSize));
                    return;
                }
            }
        }
        catch
        {
            // Ignore errors - use placeholder
        }

        // Placeholder if icon not loaded
        ImGui.Dummy(new Vector2(ImGuiHelpers.IconSize));
    }
}
