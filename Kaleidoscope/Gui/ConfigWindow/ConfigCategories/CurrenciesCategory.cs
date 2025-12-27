using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Currency/resource color management category in the config window.
/// Provides options to set custom colors for tracked data types used across all tools.
/// </summary>
public class CurrenciesCategory
{
    private readonly ConfigurationService _configService;
    private readonly TrackedDataRegistry _registry;
    private readonly ITextureProvider? _textureProvider;
    private readonly ItemDataService? _itemDataService;
    
    // Icon size for currency icons
    private static float IconSize => ImGui.GetTextLineHeight();
    
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
        
        if (ImGui.BeginTable("ItemColorsTable", 5, tableFlags, new Vector2(0, availableHeight)))
        {
            // Setup columns
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("##Icon", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, IconSize + 4);
            ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthStretch, 1f);
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
            colorValue = UintToVector4(colorUint);
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
                SaveItemColor(dataType, Vector4ToUint(_colorEditBuffer));
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

    /// <summary>
    /// Converts a uint color (ABGR format from ImGui) to Vector4.
    /// </summary>
    private static Vector4 UintToVector4(uint color)
    {
        var r = (color & 0xFF) / 255f;
        var g = ((color >> 8) & 0xFF) / 255f;
        var b = ((color >> 16) & 0xFF) / 255f;
        var a = ((color >> 24) & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }

    /// <summary>
    /// Converts a Vector4 color to uint (ABGR format for ImGui).
    /// </summary>
    private static uint Vector4ToUint(Vector4 color)
    {
        var r = (uint)(Math.Clamp(color.X, 0f, 1f) * 255f);
        var g = (uint)(Math.Clamp(color.Y, 0f, 1f) * 255f);
        var b = (uint)(Math.Clamp(color.Z, 0f, 1f) * 255f);
        var a = (uint)(Math.Clamp(color.W, 0f, 1f) * 255f);
        return r | (g << 8) | (b << 16) | (a << 24);
    }

    private void DrawCurrencyIcon(TrackedDataDefinition definition)
    {
        if (_textureProvider == null || _itemDataService == null || !definition.ItemId.HasValue)
        {
            ImGui.Dummy(new Vector2(IconSize));
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
                    ImGui.Image(wrap.Handle, new Vector2(IconSize));
                    return;
                }
            }
        }
        catch
        {
            // Ignore errors - use placeholder
        }

        // Placeholder if icon not loaded
        ImGui.Dummy(new Vector2(IconSize));
    }
}
