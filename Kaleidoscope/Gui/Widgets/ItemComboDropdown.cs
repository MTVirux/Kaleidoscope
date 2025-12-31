using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using OtterGui.Raii;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A dropdown combo box for selecting items with icons and favorites support.
/// Supports both single-select and multi-select modes.
/// </summary>
public sealed class ItemComboDropdown : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly IDataManager _dataManager;
    private readonly FavoritesService _favoritesService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly ConfigurationService? _configService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly bool _marketableOnly;
    private readonly bool _excludeCurrencies;
    
    // Cached items
    private List<ComboItem>? _items;
    private bool _needsRebuild = true;
    
    // Single-select state
    private uint _selectedItemId;
    
    // Multi-select state
    private readonly HashSet<uint> _selectedItemIds = new();
    
    // Shared state
    private string _filterText = string.Empty;
    
    // Sort order - uses configuration service if available, otherwise local fallback
    private ItemSortOrder _localSortOrder = ItemSortOrder.Alphabetical;
    private ItemSortOrder SortOrder
    {
        get => _configService?.Config.ItemPickerSortOrder ?? _localSortOrder;
        set
        {
            if (_configService != null)
            {
                _configService.Config.ItemPickerSortOrder = value;
                _configService.MarkDirty();
            }
            else
            {
                _localSortOrder = value;
            }
        }
    }

    /// <summary>
    /// The label for this combo (used for ImGui ID).
    /// </summary>
    public string Label { get; }
    
    /// <summary>
    /// Whether multi-select mode is enabled.
    /// </summary>
    public bool MultiSelectEnabled { get; set; }

    /// <summary>
    /// Gets the currently selected item ID, or 0 if none (single-select mode).
    /// </summary>
    public uint SelectedItemId => _selectedItemId;
    
    /// <summary>
    /// Gets the set of selected item IDs (multi-select mode).
    /// </summary>
    public IReadOnlySet<uint> SelectedItemIds => _selectedItemIds;

    /// <summary>
    /// Gets the currently selected item, or null if none (single-select mode).
    /// </summary>
    public ComboItem? SelectedItem
    {
        get
        {
            if (_items == null || _selectedItemId == 0) return null;
            var match = _items.FirstOrDefault(i => i.Id == _selectedItemId);
            return match.Id != 0 ? match : null;
        }
    }

    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public event Action<uint>? SelectionChanged;
    
    /// <summary>
    /// Event fired when multi-selection changes.
    /// The parameter is the set of newly added item IDs.
    /// </summary>
    public event Action<IReadOnlySet<uint>>? MultiSelectionChanged;

    public ItemComboDropdown(
        ITextureProvider textureProvider,
        IDataManager dataManager,
        FavoritesService favoritesService,
        PriceTrackingService? priceTrackingService,
        string label,
        bool marketableOnly = false,
        ConfigurationService? configService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        bool excludeCurrencies = false)
    {
        _textureProvider = textureProvider;
        _dataManager = dataManager;
        _favoritesService = favoritesService;
        _priceTrackingService = priceTrackingService;
        _configService = configService;
        _trackedDataRegistry = trackedDataRegistry;
        _marketableOnly = marketableOnly;
        _excludeCurrencies = excludeCurrencies;
        Label = label;
        
        // Subscribe to favorites changes to rebuild list
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
    }

    private void OnFavoritesChanged()
    {
        _needsRebuild = true;
    }

    private void EnsureItemsLoaded()
    {
        if (!_needsRebuild && _items != null)
            return;
        
        _items = BuildItemList();
        _needsRebuild = false;
    }

    private List<ComboItem> BuildItemList()
    {
        var items = new List<ComboItem>();
        var favorites = _favoritesService.FavoriteItems;
        var marketable = _priceTrackingService?.MarketableItems;
        
        // Build set of currency item IDs to exclude
        HashSet<uint>? currencyItemIds = null;
        if (_excludeCurrencies && _trackedDataRegistry != null)
        {
            currencyItemIds = new HashSet<uint>();
            foreach (var def in _trackedDataRegistry.Definitions.Values)
            {
                if (def.ItemId.HasValue && def.ItemId.Value > 0)
                    currencyItemIds.Add(def.ItemId.Value);
            }
        }

        try
        {
            var sheet = _dataManager.GetExcelSheet<Item>();
            if (sheet == null) return items;

            foreach (var row in sheet)
            {
                // Skip items with empty names
                var name = row.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Skip non-marketable if filter is enabled
                if (_marketableOnly && marketable != null && !marketable.Contains((int)row.RowId))
                    continue;
                
                // Skip currency items if exclusion is enabled
                if (currencyItemIds != null && currencyItemIds.Contains(row.RowId))
                    continue;

                items.Add(new ComboItem(row.RowId, name, row.Icon));
            }

            // Sort: favorites first, then by sort order (alphabetically or by ID)
            var currentSortOrder = SortOrder;
            items.Sort((a, b) =>
            {
                var aFav = favorites.Contains(a.Id);
                var bFav = favorites.Contains(b.Id);
                if (aFav != bFav)
                    return bFav.CompareTo(aFav); // Favorites first
                
                // Within same favorite status, sort by selected order
                return currentSortOrder == ItemSortOrder.ById 
                    ? a.Id.CompareTo(b.Id)
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemComboDropdown] Error building item list: {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    /// <param name="width">The combo width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(float width)
    {
        EnsureItemsLoaded();
        
        if (MultiSelectEnabled)
            return DrawMultiSelect(width);
        
        var changed = false;
        var preview = SelectedItem?.Name ?? "Select item...";
        
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo($"##{Label}", preview, ImGuiComboFlags.HeightRegular))
        {
            changed = DrawComboContent();
            ImGui.EndCombo();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws the combo in multi-select mode at the specified width.
    /// </summary>
    /// <param name="width">The combo width.</param>
    /// <returns>True if selection changed.</returns>
    public bool DrawMultiSelect(float width)
    {
        EnsureItemsLoaded();
        
        var changed = false;
        var preview = BuildMultiSelectPreview();
        
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo($"##{Label}", preview, ImGuiComboFlags.HeightLarge))
        {
            changed = DrawMultiSelectComboContent();
            ImGui.EndCombo();
        }
        
        return changed;
    }
    
    private string BuildMultiSelectPreview()
    {
        if (_selectedItemIds.Count == 0)
            return "Select items...";
        return $"{_selectedItemIds.Count} items selected";
    }
    
    private bool DrawMultiSelectComboContent()
    {
        var changed = false;
        
        // Filter input with fixed width to leave room for Clear and Sort buttons
        var buttonWidth = ImGui.CalcTextSize("Clear").X + ImGui.GetStyle().FramePadding.X * 2;
        var sortButtonWidth = ImGui.CalcTextSize("A-Z").X + ImGui.GetStyle().FramePadding.X * 2;
        var totalButtonsWidth = buttonWidth + sortButtonWidth + ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SetNextItemWidth(300 - totalButtonsWidth);
        ImGui.InputTextWithHint("##filter", "Search items...", ref _filterText, 100);
        
        // Sort order toggle button
        ImGui.SameLine();
        var sortLabel = SortOrder == ItemSortOrder.Alphabetical ? "A-Z" : "ID";
        if (ImGui.SmallButton(sortLabel))
        {
            SortOrder = SortOrder == ItemSortOrder.Alphabetical ? ItemSortOrder.ById : ItemSortOrder.Alphabetical;
            _needsRebuild = true; // Force rebuild with new sort order
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(SortOrder == ItemSortOrder.Alphabetical 
                ? "Sorting alphabetically. Click to sort by Item ID." 
                : "Sorting by Item ID. Click to sort alphabetically.");
        }
        
        // Quick action buttons
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _selectedItemIds.Clear();
            changed = true;
        }
        
        ImGui.Separator();
        
        // Filter items
        var filterLower = _filterText.ToLowerInvariant();
        var filteredItems = _items!.Where(item =>
        {
            if (string.IsNullOrEmpty(filterLower)) return true;
            return item.Name.ToLowerInvariant().Contains(filterLower) ||
                   item.Id.ToString().Contains(filterLower);
        }).Take(100); // Limit to 100 items for performance
        
        // Use child window with fixed height for scrolling
        var childHeight = Math.Min(300, ImGui.GetTextLineHeightWithSpacing() * 15);
        if (ImGui.BeginChild("##itemlist", new Vector2(300, childHeight), false, ImGuiWindowFlags.None))
        {
            foreach (var item in filteredItems)
            {
                var isSelected = _selectedItemIds.Contains(item.Id);
                
                ImGui.PushID((int)item.Id);
                
                // Draw checkbox
                if (ImGui.Checkbox("##check", ref isSelected))
                {
                    if (isSelected)
                        _selectedItemIds.Add(item.Id);
                    else
                        _selectedItemIds.Remove(item.Id);
                    changed = true;
                }
                ImGui.SameLine();
                
                // Draw favorite star
                DrawFavoriteStar(item.Id);
                ImGui.SameLine();
                
                // Draw item icon
                DrawItemIcon(item.IconId);
                ImGui.SameLine();
                
                // Draw item name and ID
                var displayText = $"{item.Name}  ({item.Id})";
                ImGui.TextUnformatted(displayText);
                
                // Allow clicking the row to toggle selection
                if (ImGui.IsItemClicked())
                {
                    if (_selectedItemIds.Contains(item.Id))
                        _selectedItemIds.Remove(item.Id);
                    else
                        _selectedItemIds.Add(item.Id);
                    changed = true;
                }
                
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        
        if (changed)
        {
            MultiSelectionChanged?.Invoke(_selectedItemIds);
        }
        
        return changed;
    }

    private bool DrawComboContent()
    {
        var changed = false;
        
        // Filter input with space for sort button
        ImGui.SetNextItemWidth(-55);
        ImGui.InputTextWithHint("##filter", "Search items...", ref _filterText, 100);
        
        // Sort order toggle button
        ImGui.SameLine();
        var sortLabel = SortOrder == ItemSortOrder.Alphabetical ? "A-Z" : "ID";
        if (ImGui.SmallButton(sortLabel))
        {
            SortOrder = SortOrder == ItemSortOrder.Alphabetical ? ItemSortOrder.ById : ItemSortOrder.Alphabetical;
            _needsRebuild = true; // Force rebuild with new sort order
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(SortOrder == ItemSortOrder.Alphabetical 
                ? "Sorting alphabetically. Click to sort by Item ID." 
                : "Sorting by Item ID. Click to sort alphabetically.");
        }
        
        ImGui.Separator();
        
        // Filter items
        var filterLower = _filterText.ToLowerInvariant();
        var filteredItems = _items!.Where(item =>
        {
            if (string.IsNullOrEmpty(filterLower)) return true;
            return item.Name.ToLowerInvariant().Contains(filterLower) ||
                   item.Id.ToString().Contains(filterLower);
        }).Take(100); // Limit to 100 items for performance
        
        foreach (var item in filteredItems)
        {
            var isSelected = item.Id == _selectedItemId;
            
            ImGui.PushID((int)item.Id);
            
            // Draw favorite star
            DrawFavoriteStar(item.Id);
            ImGui.SameLine();
            
            // Draw item icon
            DrawItemIcon(item.IconId);
            ImGui.SameLine();
            
            // Build display text with ID
            var displayText = $"{item.Name}  ({item.Id})";
            
            // Draw full-width selectable
            if (ImGui.Selectable(displayText, isSelected, ImGuiSelectableFlags.None, new System.Numerics.Vector2(0, 0)))
            {
                _selectedItemId = item.Id;
                changed = true;
                SelectionChanged?.Invoke(item.Id);
            }
            
            ImGui.PopID();
        }
        
        return changed;
    }

    private void DrawFavoriteStar(uint itemId)
    {
        var isFavorite = _favoritesService.ContainsItem(itemId);
        var starSize = new Vector2(ImGui.GetTextLineHeight());
        var hovering = ImGui.IsMouseHoveringRect(
            ImGui.GetCursorScreenPos(),
            ImGui.GetCursorScreenPos() + starSize);

        var color = hovering ? UiColors.FavoriteStarHovered : isFavorite ? UiColors.FavoriteStarOn : UiColors.FavoriteStarOff;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.TextUnformatted(FontAwesomeIcon.Star.ToIconString());
        }

        if (ImGui.IsItemClicked())
        {
            _favoritesService.ToggleItem(itemId);
        }
    }

    private void DrawItemIcon(ushort iconId)
    {
        var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        var size = new Vector2(ImGuiHelpers.IconSize);
        if (icon.TryGetWrap(out var wrap, out _))
        {
            ImGui.Image(wrap.Handle, size);
        }
        else
        {
            // Placeholder if icon not loaded
            ImGui.Dummy(size);
        }
    }

    /// <summary>
    /// Sets the current selection by item ID.
    /// </summary>
    public void SetSelection(uint itemId)
    {
        _selectedItemId = itemId;
    }

    /// <summary>
    /// Sets the multi-selection to the specified item IDs.
    /// </summary>
    public void SetMultiSelection(IEnumerable<uint> itemIds)
    {
        _selectedItemIds.Clear();
        foreach (var id in itemIds)
            _selectedItemIds.Add(id);
    }

    /// <summary>
    /// Gets the current multi-selection.
    /// </summary>
    public IReadOnlySet<uint> GetMultiSelection() => _selectedItemIds;

    /// <summary>
    /// Clears the current selection (both single and multi-select).
    /// </summary>
    public void ClearSelection()
    {
        _selectedItemId = 0;
        _selectedItemIds.Clear();
    }
    
    /// <summary>
    /// Clears the multi-selection only.
    /// </summary>
    public void ClearMultiSelection()
    {
        _selectedItemIds.Clear();
    }
    
    /// <summary>
    /// Gets the selected item IDs and clears the selection (for add-and-clear workflow).
    /// </summary>
    /// <returns>The list of selected item IDs.</returns>
    public List<uint> ConsumeMultiSelection()
    {
        var result = _selectedItemIds.ToList();
        _selectedItemIds.Clear();
        return result;
    }
    
    /// <summary>
    /// Checks if any items are selected in multi-select mode.
    /// </summary>
    public bool HasMultiSelection => _selectedItemIds.Count > 0;

    public void Dispose()
    {
        _favoritesService.OnFavoritesChanged -= OnFavoritesChanged;
    }
}
