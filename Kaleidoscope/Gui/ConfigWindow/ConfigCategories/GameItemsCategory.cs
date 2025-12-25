using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Game item color management category in the config window.
/// Allows users to set custom colors for game items used in Item Table and other tools.
/// </summary>
public class GameItemsCategory
{
    private readonly ConfigurationService _configService;
    private readonly ItemDataService? _itemDataService;
    private readonly IDataManager? _dataManager;
    private readonly ITextureProvider? _textureProvider;
    private readonly FavoritesService? _favoritesService;
    
    // Color editing state
    private uint? _editingColorItemId = null;
    private Vector4 _colorEditBuffer = Vector4.One;
    
    // Search state
    private string _searchFilter = string.Empty;
    
    // Item picker for adding new items
    private readonly ItemIconCombo? _itemCombo;
    
    // Cached item names for display
    private readonly Dictionary<uint, string> _itemNameCache = new();

    public GameItemsCategory(
        ConfigurationService configService,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        ITextureProvider? textureProvider = null,
        FavoritesService? favoritesService = null)
    {
        _configService = configService;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _textureProvider = textureProvider;
        _favoritesService = favoritesService;
        
        // Create item picker if we have the required services
        if (_dataManager != null && _textureProvider != null && _favoritesService != null)
        {
            _itemCombo = new ItemIconCombo(
                _textureProvider,
                _dataManager,
                _favoritesService,
                null, // No price tracking service - include all items
                "GameItemsAdd",
                marketableOnly: false);
        }
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Game Item Colors");
        ImGui.Separator();
        ImGui.TextWrapped("Set custom colors for game items tracked in the Item Table tool. " +
            "These colors are applied to item columns in the table.");
        ImGui.Spacing();
        
        var config = _configService.Config;
        var gameItemColors = config.GameItemColors;
        
        // Add new item section
        DrawAddItemSection(gameItemColors);
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Search bar
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##search", "Search items...", ref _searchFilter, 100);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _searchFilter = string.Empty;
        }
        ImGui.Spacing();

        if (gameItemColors.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No game items with custom colors yet.");
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Use the item picker above to add items.");
            return;
        }
        
        // Filter items by search
        var filteredItems = string.IsNullOrWhiteSpace(_searchFilter)
            ? gameItemColors.Keys.ToList()
            : gameItemColors.Keys.Where(id => 
            {
                var name = GetItemName(id);
                return name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        
        if (filteredItems.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No items match your search.");
            return;
        }

        // Draw table
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Sortable;
        
        // Calculate available height for table
        var availableHeight = ImGui.GetContentRegionAvail().Y - 30;
        if (availableHeight < 100) availableHeight = 100;

        if (ImGui.BeginTable("GameItemColorsTable", 4, tableFlags, new Vector2(0, availableHeight)))
        {
            // Setup columns
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort, 60);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 80);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 40);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            // Get sort specs and apply sorting
            var sortSpecs = ImGui.TableGetSortSpecs();
            IEnumerable<uint> sortedItems = filteredItems;
            
            if (sortSpecs.SpecsCount > 0)
            {
                var spec = sortSpecs.Specs;
                var ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
                
                sortedItems = spec.ColumnIndex switch
                {
                    0 => ascending ? filteredItems.OrderBy(id => id) : filteredItems.OrderByDescending(id => id),
                    1 => ascending ? filteredItems.OrderBy(id => GetItemName(id)) : filteredItems.OrderByDescending(id => GetItemName(id)),
                    _ => filteredItems.OrderBy(id => GetItemName(id))
                };
            }
            else
            {
                // Default sort by item name
                sortedItems = filteredItems.OrderBy(id => GetItemName(id));
            }

            foreach (var itemId in sortedItems)
            {
                ImGui.TableNextRow();

                // ID column
                ImGui.TableNextColumn();
                ImGui.TextDisabled($"{itemId}");

                // Item name column
                ImGui.TableNextColumn();
                var itemName = GetItemName(itemId);
                ImGui.TextUnformatted(itemName);

                // Color column
                ImGui.TableNextColumn();
                DrawColorCell(itemId, config);

                // Actions column
                ImGui.TableNextColumn();
                DrawActionsCell(itemId, config);
            }

            ImGui.EndTable();
        }

        // Summary
        ImGui.Spacing();
        var totalCount = gameItemColors.Count;
        var filteredCount = filteredItems.Count;
        var summaryText = string.IsNullOrWhiteSpace(_searchFilter)
            ? $"{totalCount} items with custom colors"
            : $"Showing {filteredCount} of {totalCount} items";
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), summaryText);
    }

    private void DrawAddItemSection(Dictionary<uint, uint> gameItemColors)
    {
        ImGui.TextUnformatted("Add Item");
        
        if (_itemCombo != null)
        {
            if (_itemCombo.Draw(_itemCombo.SelectedItem?.Name ?? "Select item to add...", _itemCombo.SelectedItemId, 300, 300))
            {
                // Item selected - add it with a default white color
                if (_itemCombo.SelectedItemId > 0)
                {
                    var itemId = _itemCombo.SelectedItemId;
                    if (!gameItemColors.ContainsKey(itemId))
                    {
                        // Add with white color (0xFFFFFFFF in ABGR)
                        gameItemColors[itemId] = 0xFFFFFFFF;
                        _configService.Save();
                        LogService.Debug($"[GameItemsCategory] Added item {itemId} with default color");
                    }
                    _itemCombo.ClearSelection();
                }
            }
        }
        else
        {
            ImGui.TextDisabled("Item picker not available.");
        }
    }

    private void DrawColorCell(uint itemId, Configuration config)
    {
        ImGui.PushID((int)itemId);
        
        var hasColor = config.GameItemColors.TryGetValue(itemId, out var colorUint);
        Vector4 colorValue;
        
        if (_editingColorItemId == itemId)
        {
            colorValue = _colorEditBuffer;
        }
        else if (hasColor)
        {
            colorValue = UintToVector4(colorUint);
        }
        else
        {
            colorValue = new Vector4(1f, 1f, 1f, 1f);
        }
        
        // Color picker
        if (ImGui.ColorEdit4("##color", ref colorValue,
            ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar))
        {
            _colorEditBuffer = colorValue;
        }
        
        // Track when we start editing
        if (ImGui.IsItemActivated())
        {
            _editingColorItemId = itemId;
            _colorEditBuffer = colorValue;
        }
        
        // Save when the user finishes editing
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            SaveGameItemColor(itemId, Vector4ToUint(_colorEditBuffer));
            _editingColorItemId = null;
        }
        
        ImGui.PopID();
    }

    private void DrawActionsCell(uint itemId, Configuration config)
    {
        ImGui.PushID((int)itemId);
        if (ImGui.SmallButton("X"))
        {
            SaveGameItemColor(itemId, null);
            _editingColorItemId = null;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Remove item");
            ImGui.EndTooltip();
        }
        ImGui.PopID();
    }

    private string GetItemName(uint itemId)
    {
        if (_itemNameCache.TryGetValue(itemId, out var cached))
            return cached;
        
        var name = _itemDataService?.GetItemName(itemId) ?? $"Item #{itemId}";
        _itemNameCache[itemId] = name;
        return name;
    }

    private void SaveGameItemColor(uint itemId, uint? color)
    {
        try
        {
            var config = _configService.Config;
            
            if (color.HasValue)
            {
                config.GameItemColors[itemId] = color.Value;
            }
            else
            {
                config.GameItemColors.Remove(itemId);
                _itemNameCache.Remove(itemId);
            }
            
            _configService.Save();
            LogService.Debug($"[GameItemsCategory] Saved color for item {itemId}: {color?.ToString("X8") ?? "(removed)"}");
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to save game item color for {itemId}", ex);
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
}
