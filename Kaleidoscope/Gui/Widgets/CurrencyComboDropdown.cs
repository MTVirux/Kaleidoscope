using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using OtterGui.Raii;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A dropdown combo box for selecting currencies (TrackedDataTypes) with icons and favorites support.
/// Supports both single-select and multi-select modes.
/// </summary>
public sealed class CurrencyComboDropdown : IDisposable
{
    private readonly ITextureProvider _textureProvider;
    private readonly TrackedDataRegistry _registry;
    private readonly FavoritesService _favoritesService;
    private readonly ItemDataService? _itemDataService;
    
    // Cached currencies
    private List<ComboCurrency>? _currencies;
    private bool _needsRebuild = true;
    
    // Single-select state
    private TrackedDataType _selectedType;
    
    // Multi-select state
    private readonly HashSet<TrackedDataType> _selectedTypes = new();
    
    // Shared state
    private string _filterText = string.Empty;
    
    // Icon sizes
    private static readonly Vector2 IconSize = new(20, 20);
    private static readonly Vector2 StarSize = new(16, 16);

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
    public TrackedDataType SelectedType => _selectedType;
    
    /// <summary>
    /// Gets the set of selected currency types (multi-select mode).
    /// </summary>
    public IReadOnlySet<TrackedDataType> SelectedTypes => _selectedTypes;

    /// <summary>
    /// Gets the currently selected currency, or null if none (single-select mode).
    /// </summary>
    public ComboCurrency? SelectedCurrency
    {
        get
        {
            if (_currencies == null || _selectedType == default) return null;
            var match = _currencies.FirstOrDefault(c => c.Type == _selectedType);
            return match.Type != default ? match : null;
        }
    }

    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public event Action<TrackedDataType>? SelectionChanged;
    
    /// <summary>
    /// Event fired when multi-selection changes.
    /// </summary>
    public event Action<IReadOnlySet<TrackedDataType>>? MultiSelectionChanged;

    public CurrencyComboDropdown(
        ITextureProvider textureProvider,
        TrackedDataRegistry registry,
        FavoritesService favoritesService,
        string label,
        ItemDataService? itemDataService = null)
    {
        _textureProvider = textureProvider;
        _registry = registry;
        _favoritesService = favoritesService;
        _itemDataService = itemDataService;
        Label = label;
        
        // Subscribe to favorites changes to rebuild list
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
    }

    private void OnFavoritesChanged()
    {
        _needsRebuild = true;
    }

    private void EnsureCurrenciesLoaded()
    {
        if (!_needsRebuild && _currencies != null)
            return;
        
        _currencies = BuildCurrencyList();
        _needsRebuild = false;
    }

    private List<ComboCurrency> BuildCurrencyList()
    {
        var currencies = new List<ComboCurrency>();
        var favorites = _favoritesService.FavoriteCurrencies;

        foreach (var (type, def) in _registry.Definitions)
        {
            currencies.Add(new ComboCurrency(
                type,
                def.DisplayName,
                def.ShortName,
                def.ItemId,
                def.Category));
        }

        // Sort: favorites first, then by category, then alphabetically
        currencies.Sort((a, b) =>
        {
            var aFav = favorites.Contains(a.Type);
            var bFav = favorites.Contains(b.Type);
            if (aFav != bFav)
                return bFav.CompareTo(aFav); // Favorites first
            
            // Then by category
            var catCompare = a.Category.CompareTo(b.Category);
            if (catCompare != 0)
                return catCompare;
            
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return currencies;
    }

    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    /// <param name="width">The combo width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(float width)
    {
        EnsureCurrenciesLoaded();
        
        var changed = false;
        var preview = SelectedCurrency?.Name ?? "Select currency...";
        
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo($"##{Label}", preview, ImGuiComboFlags.HeightRegular))
        {
            changed = DrawComboContent();
            ImGui.EndCombo();
        }
        
        return changed;
    }

    private bool DrawComboContent()
    {
        var changed = false;
        
        // Filter input
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Search currencies...", ref _filterText, 100);
        
        ImGui.Separator();
        
        // Filter currencies
        var filterLower = _filterText.ToLowerInvariant();
        var filteredCurrencies = _currencies!.Where(currency =>
        {
            if (string.IsNullOrEmpty(filterLower)) return true;
            return currency.Name.ToLowerInvariant().Contains(filterLower) ||
                   currency.ShortName.ToLowerInvariant().Contains(filterLower) ||
                   currency.Category.ToString().ToLowerInvariant().Contains(filterLower);
        });
        
        foreach (var currency in filteredCurrencies)
        {
            var isSelected = currency.Type == _selectedType;
            
            ImGui.PushID((int)currency.Type);
            
            // Draw favorite star
            DrawFavoriteStar(currency.Type);
            ImGui.SameLine();
            
            // Draw icon (always reserve space even if no icon)
            if (currency.ItemId.HasValue)
            {
                DrawCurrencyIcon(currency.ItemId.Value);
            }
            else
            {
                ImGui.Dummy(IconSize);
            }
            ImGui.SameLine();
            
            // Draw full-width selectable
            if (ImGui.Selectable(currency.Name, isSelected, ImGuiSelectableFlags.None, new System.Numerics.Vector2(0, 0)))
            {
                _selectedType = currency.Type;
                changed = true;
                SelectionChanged?.Invoke(currency.Type);
            }
            
            ImGui.PopID();
        }
        
        return changed;
    }

    private void DrawFavoriteStar(TrackedDataType type)
    {
        var isFavorite = _favoritesService.ContainsCurrency(type);
        var hovering = ImGui.IsMouseHoveringRect(
            ImGui.GetCursorScreenPos(),
            ImGui.GetCursorScreenPos() + StarSize);

        var color = hovering ? UiColors.FavoriteStarHovered : isFavorite ? UiColors.FavoriteStarOn : UiColors.FavoriteStarOff;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.TextUnformatted(FontAwesomeIcon.Star.ToIconString());
        }

        if (ImGui.IsItemClicked())
        {
            _favoritesService.ToggleCurrency(type);
        }
    }

    private void DrawCurrencyIcon(uint itemId)
    {
        // Get icon from ItemDataService or use game icon lookup
        try
        {
            ushort iconId = 0;
            
            // Try to get icon from ItemDataService if available
            if (_itemDataService != null)
            {
                iconId = _itemDataService.GetItemIconId(itemId);
            }
            
            // Fall back to direct lookup if no icon found
            if (iconId == 0)
            {
                iconId = (ushort)itemId; // Some currencies use itemId as iconId
            }
            
            var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            if (icon.TryGetWrap(out var wrap, out _))
            {
                ImGui.Image(wrap.Handle, IconSize);
                return;
            }
        }
        catch
        {
            // Ignore errors - use placeholder
        }
        
        // Placeholder if icon not loaded
        ImGui.Dummy(IconSize);
    }

    /// <summary>
    /// Sets the current selection by type.
    /// </summary>
    public void SetSelection(TrackedDataType type)
    {
        _selectedType = type;
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _selectedType = default;
    }

    #region Multi-Select Support

    /// <summary>
    /// Draws the combo in multi-select mode at the specified width.
    /// </summary>
    /// <param name="width">The combo width.</param>
    /// <returns>True if selection changed.</returns>
    public bool DrawMultiSelect(float width)
    {
        EnsureCurrenciesLoaded();
        
        var changed = false;
        var preview = BuildMultiSelectPreview();
        
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo($"##{Label}", preview, ImGuiComboFlags.HeightRegular))
        {
            changed = DrawMultiSelectComboContent();
            ImGui.EndCombo();
        }
        
        return changed;
    }

    private string BuildMultiSelectPreview()
    {
        if (_selectedTypes.Count == 0)
            return "Select currencies...";
        
        return $"{_selectedTypes.Count} currencies selected";
    }

    private bool DrawMultiSelectComboContent()
    {
        var changed = false;
        
        // Filter input
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Search currencies...", ref _filterText, 100);
        
        // Selection controls
        if (ImGui.Button("Select All"))
        {
            foreach (var currency in _currencies!)
            {
                _selectedTypes.Add(currency.Type);
            }
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear All"))
        {
            _selectedTypes.Clear();
            changed = true;
        }
        
        ImGui.Separator();
        
        // Filter currencies
        var filterLower = _filterText.ToLowerInvariant();
        var filteredCurrencies = _currencies!.Where(currency =>
        {
            if (string.IsNullOrEmpty(filterLower)) return true;
            return currency.Name.ToLowerInvariant().Contains(filterLower) ||
                   currency.ShortName.ToLowerInvariant().Contains(filterLower) ||
                   currency.Category.ToString().ToLowerInvariant().Contains(filterLower);
        });
        
        foreach (var currency in filteredCurrencies)
        {
            var isSelected = _selectedTypes.Contains(currency.Type);
            
            ImGui.PushID((int)currency.Type);
            
            // Draw favorite star
            DrawFavoriteStar(currency.Type);
            ImGui.SameLine();
            
            // Draw icon (always reserve space even if no icon)
            if (currency.ItemId.HasValue)
            {
                DrawCurrencyIcon(currency.ItemId.Value);
            }
            else
            {
                ImGui.Dummy(IconSize);
            }
            ImGui.SameLine();
            
            // Checkbox for multi-select
            if (ImGui.Checkbox(currency.Name, ref isSelected))
            {
                if (isSelected)
                    _selectedTypes.Add(currency.Type);
                else
                    _selectedTypes.Remove(currency.Type);
                changed = true;
            }
            
            ImGui.PopID();
        }
        
        if (changed)
        {
            MultiSelectionChanged?.Invoke(_selectedTypes);
        }
        
        return changed;
    }

    /// <summary>
    /// Sets the multi-selection to the specified currency types.
    /// </summary>
    public void SetMultiSelection(IEnumerable<TrackedDataType> types)
    {
        _selectedTypes.Clear();
        foreach (var type in types)
            _selectedTypes.Add(type);
    }

    /// <summary>
    /// Gets the current multi-selection.
    /// </summary>
    public IReadOnlySet<TrackedDataType> GetMultiSelection() => _selectedTypes;

    /// <summary>
    /// Clears the multi-selection.
    /// </summary>
    public void ClearMultiSelection()
    {
        _selectedTypes.Clear();
    }

    /// <summary>
    /// Consumes the current multi-selection, returning and clearing the selected types.
    /// </summary>
    /// <returns>The set of selected types.</returns>
    public HashSet<TrackedDataType> ConsumeMultiSelection()
    {
        var result = new HashSet<TrackedDataType>(_selectedTypes);
        _selectedTypes.Clear();
        return result;
    }

    /// <summary>
    /// Gets whether there are any currencies selected in multi-select mode.
    /// </summary>
    public bool HasMultiSelection => _selectedTypes.Count > 0;

    #endregion

    public void Dispose()
    {
        _favoritesService.OnFavoritesChanged -= OnFavoritesChanged;
    }
}
