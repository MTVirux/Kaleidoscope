using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using MTGui.Combo;
using OtterGui.Raii;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// Sort order for items in the combo.
/// </summary>
public enum MTItemSortOrder
{
    /// <summary>Sort alphabetically by name.</summary>
    Alphabetical,
    /// <summary>Sort by numeric item ID.</summary>
    ById
}

/// <summary>
/// An item combo widget using MTComboWidget from MTGui.
/// Provides the same public interface as the legacy ItemComboDropdown.
/// </summary>
public sealed class MTItemComboDropdown : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly IDataManager _dataManager;
    private readonly FavoritesService _favoritesService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly ConfigurationService? _configService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly bool _marketableOnly;
    private readonly bool _excludeCurrencies;
    
    private readonly MTComboWidget<MTGameItem, uint> _widget;
    private readonly MTComboState<uint> _state;
    
    private bool _disposed;
    private bool _needsRebuild = true;
    
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
    public uint SelectedItemId => _widget.SelectedItem?.Id ?? 0;
    
    /// <summary>
    /// Gets the set of selected item IDs (multi-select mode).
    /// </summary>
    public IReadOnlySet<uint> SelectedItemIds => _state.SelectedIds;
    
    /// <summary>
    /// Gets the currently selected item, or null if none (single-select mode).
    /// </summary>
    public ComboItem? SelectedItem => _widget.SelectedItem != null 
        ? new ComboItem(_widget.SelectedItem.Id, _widget.SelectedItem.Name, _widget.SelectedItem.IconId)
        : null;
    
    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public event Action<uint>? SelectionChanged;
    
    /// <summary>
    /// Event fired when multi-selection changes.
    /// </summary>
    public event Action<IReadOnlySet<uint>>? MultiSelectionChanged;
    
    public MTItemComboDropdown(
        ITextureProvider textureProvider,
        IDataManager dataManager,
        FavoritesService favoritesService,
        PriceTrackingService? priceTrackingService,
        string label,
        bool marketableOnly = false,
        ConfigurationService? configService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        bool excludeCurrencies = false,
        bool multiSelect = false)
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
        MultiSelectEnabled = multiSelect;
        
        // Create state
        _state = new MTComboState<uint>
        {
            SortOrder = MTComboSortOrder.Alphabetical
        };
        
        // Create config
        var config = new MTComboConfig
        {
            ComboId = label,
            Placeholder = "Select item...",
            SearchPlaceholder = "Search items...",
            MultiSelect = multiSelect,
            ShowSearch = true,
            ShowFavorites = true,
            ShowIcons = true,
            ShowSortToggle = true,
            ShowGroupingToggle = false, // Items don't have natural grouping
            ShowBulkActions = true,
            ShowFavoritesBulkAction = true,
            ShowInvertBulkAction = false,
            ShowAllOption = false,
            ShowItemIds = true,
            ItemDisplayFormat = "{0}  ({1})",
            MaxDisplayedItems = 100
        };
        
        // Create widget
        _widget = new MTComboWidget<MTGameItem, uint>(config, _state);
        
        // Configure icon renderer
        _widget.WithIconRenderer(DrawItemIcon);
        
        // Configure filter
        _widget.WithFilter((item, filter) =>
            item.Name.ToLowerInvariant().Contains(filter) ||
            item.Id.ToString().Contains(filter));
        
        // Subscribe to events
        _widget.SelectionChanged += OnWidgetSelectionChanged;
        _widget.MultiSelectionChanged += OnWidgetMultiSelectionChanged;
        _widget.FavoriteToggled += OnWidgetFavoriteToggled;
        
        // Sync favorites from service (must be after widget is created)
        SyncFavoritesFromService();
        
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
    }
    
    private void SyncFavoritesFromService()
    {
        _widget.SyncFavorites(_favoritesService.FavoriteItems);
    }
    
    private void OnFavoritesChanged()
    {
        SyncFavoritesFromService();
        _needsRebuild = true;
    }
    
    private void OnWidgetSelectionChanged(uint id)
    {
        SelectionChanged?.Invoke(id);
    }
    
    private void OnWidgetMultiSelectionChanged(IReadOnlySet<uint> ids)
    {
        MultiSelectionChanged?.Invoke(ids);
    }
    
    private void OnWidgetFavoriteToggled(uint id, bool isFavorite)
    {
        if (isFavorite)
            _favoritesService.AddItem(id);
        else
            _favoritesService.RemoveItem(id);
    }
    
    private void DrawItemIcon(MTGameItem item, Vector2 size)
    {
        var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(item.IconId));
        if (icon.TryGetWrap(out var wrap, out _))
        {
            ImGui.Image(wrap.Handle, size);
        }
        else
        {
            ImGui.Dummy(size);
        }
    }
    
    private void EnsureItemsLoaded()
    {
        if (!_needsRebuild)
            return;
        
        var items = BuildItemList();
        _widget.SetItems(items);
        _needsRebuild = false;
    }
    
    private List<MTGameItem> BuildItemList()
    {
        var items = new List<MTGameItem>();
        var marketable = _priceTrackingService?.MarketableItems;
        
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
                var name = row.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                
                if (_marketableOnly && marketable != null && !marketable.Contains((int)row.RowId))
                    continue;
                
                if (currencyItemIds != null && currencyItemIds.Contains(row.RowId))
                    continue;
                
                items.Add(new MTGameItem
                {
                    Id = row.RowId,
                    Name = name,
                    IconId = row.Icon
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"[MTItemComboDropdown] Error building item list: {ex.Message}");
        }
        
        return items;
    }
    
    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    public bool Draw(float width)
    {
        EnsureItemsLoaded();
        return _widget.Draw(width);
    }
    
    /// <summary>
    /// Draws the combo in multi-select mode at the specified width.
    /// </summary>
    public bool DrawMultiSelect(float width)
    {
        EnsureItemsLoaded();
        return _widget.Draw(width);
    }
    
    /// <summary>
    /// Sets the current selection by item ID.
    /// </summary>
    public void SetSelection(uint itemId)
    {
        _widget.SetSelection(itemId);
    }
    
    /// <summary>
    /// Sets the multi-selection to the specified item IDs.
    /// </summary>
    public void SetMultiSelection(IEnumerable<uint> itemIds)
    {
        _widget.SetMultiSelection(itemIds);
    }
    
    /// <summary>
    /// Gets the current multi-selection.
    /// </summary>
    public IReadOnlySet<uint> GetMultiSelection() => _state.SelectedIds;
    
    /// <summary>
    /// Clears the current selection (both single and multi-select).
    /// </summary>
    public void ClearSelection()
    {
        _widget.ClearSelection();
    }
    
    /// <summary>
    /// Clears the multi-selection only.
    /// </summary>
    public void ClearMultiSelection()
    {
        _state.SelectedIds.Clear();
    }
    
    /// <summary>
    /// Gets the selected item IDs and clears the selection (for add-and-clear workflow).
    /// </summary>
    public List<uint> ConsumeMultiSelection()
    {
        var result = _state.SelectedIds.ToList();
        _state.SelectedIds.Clear();
        return result;
    }
    
    /// <summary>
    /// Checks if any items are selected in multi-select mode.
    /// </summary>
    public bool HasMultiSelection => _state.SelectedIds.Count > 0;
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _widget.SelectionChanged -= OnWidgetSelectionChanged;
        _widget.MultiSelectionChanged -= OnWidgetMultiSelectionChanged;
        _widget.FavoriteToggled -= OnWidgetFavoriteToggled;
        
        _favoritesService.OnFavoritesChanged -= OnFavoritesChanged;
    }
}
