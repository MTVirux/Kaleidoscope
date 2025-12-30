using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using MTGui.Combo;
using OtterGui.Raii;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// A currency combo widget using MTComboWidget from MTGui.
/// Provides the same public interface as the legacy CurrencyComboDropdown.
/// </summary>
public sealed class MTCurrencyComboDropdown : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly TrackedDataRegistry _registry;
    private readonly FavoritesService _favoritesService;
    private readonly ItemDataService? _itemDataService;
    
    private readonly MTComboWidget<MTCurrencyItem, TrackedDataType> _widget;
    private readonly MTComboState<TrackedDataType> _state;
    
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
    /// Gets the currently selected currency type (single-select mode).
    /// </summary>
    public TrackedDataType SelectedType => _widget.SelectedItem?.Id ?? default;
    
    /// <summary>
    /// Gets the set of selected currency types (multi-select mode).
    /// </summary>
    public IReadOnlySet<TrackedDataType> SelectedTypes => _state.SelectedIds;
    
    /// <summary>
    /// Gets the currently selected currency, or null if none (single-select mode).
    /// </summary>
    public ComboCurrency? SelectedCurrency => _widget.SelectedItem != null
        ? new ComboCurrency(
            _widget.SelectedItem.Id,
            _widget.SelectedItem.Name,
            _widget.SelectedItem.ShortName,
            _widget.SelectedItem.ItemId,
            _widget.SelectedItem.Category)
        : null;
    
    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public event Action<TrackedDataType>? SelectionChanged;
    
    /// <summary>
    /// Event fired when multi-selection changes.
    /// </summary>
    public event Action<IReadOnlySet<TrackedDataType>>? MultiSelectionChanged;
    
    public MTCurrencyComboDropdown(
        ITextureProvider textureProvider,
        TrackedDataRegistry registry,
        FavoritesService favoritesService,
        string label,
        ItemDataService? itemDataService = null,
        bool multiSelect = false)
    {
        _textureProvider = textureProvider;
        _registry = registry;
        _favoritesService = favoritesService;
        _itemDataService = itemDataService;
        Label = label;
        MultiSelectEnabled = multiSelect;
        
        // Create state
        _state = new MTComboState<TrackedDataType>
        {
            SortOrder = MTComboSortOrder.Custom
        };
        
        // Create config
        var config = new MTComboConfig
        {
            ComboId = label,
            Placeholder = "Select currency...",
            SearchPlaceholder = "Search currencies...",
            MultiSelect = multiSelect,
            ShowSearch = true,
            ShowFavorites = true,
            ShowIcons = true,
            ShowSortToggle = false, // Currencies have a specific sort order
            ShowGroupingToggle = true,
            ShowBulkActions = multiSelect,
            ShowFavoritesBulkAction = multiSelect,
            ShowInvertBulkAction = false,
            ShowAllOption = false,
            DefaultGroupMode = MTComboGroupDisplayMode.Flat
        };
        
        // Create widget
        _widget = new MTComboWidget<MTCurrencyItem, TrackedDataType>(config, _state);
        
        // Configure grouping by category
        _widget.WithGrouping(item => item.Category.ToString());
        
        // Configure icon renderer
        _widget.WithIconRenderer(DrawCurrencyIcon);
        
        // Configure filter
        _widget.WithFilter((item, filter) =>
            item.Name.ToLowerInvariant().Contains(filter) ||
            item.ShortName.ToLowerInvariant().Contains(filter) ||
            item.Category.ToString().ToLowerInvariant().Contains(filter));
        
        // Configure custom comparer for primary currency ordering
        _widget.WithComparer((a, b, favorites) =>
        {
            var aFav = favorites.Contains(a.Id);
            var bFav = favorites.Contains(b.Id);
            if (aFav != bFav)
                return bFav.CompareTo(aFav);
            
            // Primary currencies get priority
            var aPrimary = GetPrimaryCurrencyOrder(a.Id);
            var bPrimary = GetPrimaryCurrencyOrder(b.Id);
            if (aPrimary != bPrimary)
                return aPrimary.CompareTo(bPrimary);
            
            // Then by category
            var catCompare = a.Category.CompareTo(b.Category);
            if (catCompare != 0)
                return catCompare;
            
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        
        // Subscribe to events
        _widget.SelectionChanged += OnWidgetSelectionChanged;
        _widget.MultiSelectionChanged += OnWidgetMultiSelectionChanged;
        _widget.FavoriteToggled += OnWidgetFavoriteToggled;
        
        // Sync favorites from service (must be after widget is created)
        SyncFavoritesFromService();
        
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
    }
    
    private static int GetPrimaryCurrencyOrder(TrackedDataType type) => type switch
    {
        TrackedDataType.Gil => 0,
        TrackedDataType.RetainerGil => 1,
        TrackedDataType.FreeCompanyGil => 2,
        TrackedDataType.InventoryValueItems => 3,
        _ => int.MaxValue
    };
    
    private void SyncFavoritesFromService()
    {
        _widget.SyncFavorites(_favoritesService.FavoriteCurrencies);
    }
    
    private void OnFavoritesChanged()
    {
        SyncFavoritesFromService();
        _needsRebuild = true;
    }
    
    private void OnWidgetSelectionChanged(TrackedDataType type)
    {
        SelectionChanged?.Invoke(type);
    }
    
    private void OnWidgetMultiSelectionChanged(IReadOnlySet<TrackedDataType> types)
    {
        MultiSelectionChanged?.Invoke(types);
    }
    
    private void OnWidgetFavoriteToggled(TrackedDataType type, bool isFavorite)
    {
        if (isFavorite)
            _favoritesService.AddCurrency(type);
        else
            _favoritesService.RemoveCurrency(type);
    }
    
    private void DrawCurrencyIcon(MTCurrencyItem item, Vector2 size)
    {
        if (!item.ItemId.HasValue)
        {
            ImGui.Dummy(size);
            return;
        }
        
        try
        {
            ushort iconId = 0;
            
            if (_itemDataService != null)
            {
                iconId = _itemDataService.GetItemIconId(item.ItemId.Value);
            }
            
            if (iconId == 0)
            {
                iconId = (ushort)item.ItemId.Value;
            }
            
            var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            if (icon.TryGetWrap(out var wrap, out _))
            {
                ImGui.Image(wrap.Handle, size);
                return;
            }
        }
        catch
        {
            // Ignore errors - use placeholder
        }
        
        ImGui.Dummy(size);
    }
    
    private void EnsureCurrenciesLoaded()
    {
        if (!_needsRebuild)
            return;
        
        var items = BuildCurrencyList();
        _widget.SetItems(items);
        _needsRebuild = false;
    }
    
    private List<MTCurrencyItem> BuildCurrencyList()
    {
        var items = new List<MTCurrencyItem>();
        
        foreach (var (type, def) in _registry.Definitions)
        {
            items.Add(new MTCurrencyItem
            {
                Id = type,
                Name = def.DisplayName,
                ShortName = def.ShortName,
                ItemId = def.ItemId,
                Category = def.Category
            });
        }
        
        return items;
    }
    
    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    public bool Draw(float width)
    {
        EnsureCurrenciesLoaded();
        return _widget.Draw(width);
    }
    
    /// <summary>
    /// Draws the combo in multi-select mode at the specified width.
    /// </summary>
    public bool DrawMultiSelect(float width)
    {
        EnsureCurrenciesLoaded();
        return _widget.Draw(width);
    }
    
    /// <summary>
    /// Sets the current selection by type.
    /// </summary>
    public void SetSelection(TrackedDataType type)
    {
        _widget.SetSelection(type);
    }
    
    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _widget.ClearSelection();
    }
    
    /// <summary>
    /// Sets the multi-selection to the specified currency types.
    /// </summary>
    public void SetMultiSelection(IEnumerable<TrackedDataType> types)
    {
        _widget.SetMultiSelection(types);
    }
    
    /// <summary>
    /// Gets the current multi-selection.
    /// </summary>
    public IReadOnlySet<TrackedDataType> GetMultiSelection() => _state.SelectedIds;
    
    /// <summary>
    /// Clears the multi-selection.
    /// </summary>
    public void ClearMultiSelection()
    {
        _state.SelectedIds.Clear();
    }
    
    /// <summary>
    /// Consumes the current multi-selection, returning and clearing the selected types.
    /// </summary>
    public HashSet<TrackedDataType> ConsumeMultiSelection()
    {
        var result = new HashSet<TrackedDataType>(_state.SelectedIds);
        _state.SelectedIds.Clear();
        return result;
    }
    
    /// <summary>
    /// Gets whether there are any currencies selected in multi-select mode.
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
