using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using OtterGui.Classes;
using OtterGui.Extensions;
using OtterGui.Log;
using OtterGui.Raii;
using OtterGui.Widgets;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Represents a currency (TrackedDataType) for display in the combo.
/// </summary>
public readonly record struct ComboCurrency(TrackedDataType Type, string Name, string ShortName, uint? ItemId, TrackedDataCategory Category);

/// <summary>
/// A filtered combo box for selecting currencies (TrackedDataTypes) with icons and favorites support.
/// </summary>
public sealed class CurrencyIconCombo : FilterComboCache<ComboCurrency>
{
    private readonly ITextureProvider _textureProvider;
    private readonly FavoritesService _favoritesService;
    private readonly TrackedDataRegistry _registry;
    
    // Current state
    private TrackedDataType _currentType;
    private float _innerWidth;
    
    // Icon sizes
    private static readonly Vector2 IconSize = new(20, 20);
    private static readonly Vector2 StarSize = new(16, 16);

    /// <summary>
    /// The label for this combo (used for ImGui ID).
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the currently selected currency.
    /// </summary>
    public ComboCurrency? SelectedCurrency => CurrentSelection;

    /// <summary>
    /// Gets the currently selected currency type.
    /// </summary>
    public TrackedDataType? SelectedType => CurrentSelectionIdx >= 0 ? CurrentSelection.Type : null;

    /// <summary>
    /// Event fired when selection changes.
    /// </summary>
    public new event Action<ComboCurrency?, ComboCurrency?>? SelectionChanged;

    public CurrencyIconCombo(
        ITextureProvider textureProvider,
        TrackedDataRegistry registry,
        FavoritesService favoritesService,
        string label)
        : base(
            () => BuildCurrencyList(registry, favoritesService),
            MouseWheelType.Control,
            new Logger())
    {
        _textureProvider = textureProvider;
        _favoritesService = favoritesService;
        _registry = registry;
        Label = label;
        SearchByParts = true;
        
        // Subscribe to favorites changes to rebuild list
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
    }

    private void OnFavoritesChanged()
    {
        ResetFilter();
    }

    private static IReadOnlyList<ComboCurrency> BuildCurrencyList(
        TrackedDataRegistry registry,
        FavoritesService favoritesService)
    {
        var currencies = new List<ComboCurrency>();
        var favorites = favoritesService.FavoriteCurrencies;

        foreach (var (type, def) in registry.Definitions)
        {
            currencies.Add(new ComboCurrency(
                type,
                def.DisplayName,
                def.ShortName,
                def.ItemId,
                def.Category));
        }

        // Sort: favorites first, then primary currencies, then by category, then alphabetically
        currencies.Sort((a, b) =>
        {
            var aFav = favorites.Contains(a.Type);
            var bFav = favorites.Contains(b.Type);
            if (aFav != bFav)
                return bFav.CompareTo(aFav); // Favorites first
            
            // Primary currencies get priority in specific order
            var aPrimary = GetPrimaryCurrencyOrder(a.Type);
            var bPrimary = GetPrimaryCurrencyOrder(b.Type);
            if (aPrimary != bPrimary)
                return aPrimary.CompareTo(bPrimary);
            
            // Then by category
            var catCompare = a.Category.CompareTo(b.Category);
            if (catCompare != 0)
                return catCompare;
            
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return currencies;
    }

    /// <summary>
    /// Returns the sort order for primary currencies. Lower values sort first.
    /// Non-primary currencies return int.MaxValue to sort after primary ones.
    /// </summary>
    private static int GetPrimaryCurrencyOrder(TrackedDataType type) => type switch
    {
        TrackedDataType.Gil => 0,                    // Character Gil first
        TrackedDataType.RetainerGil => 1,            // Retainer Gil second
        TrackedDataType.FreeCompanyGil => 2,         // Free Company Gil third
        TrackedDataType.InventoryValueItems => 3,    // Inventory Value (in Gil) fourth
        _ => int.MaxValue                            // All others after
    };

    protected override string ToString(ComboCurrency obj)
        => obj.Name;

    protected override float GetFilterWidth()
        => _innerWidth - 2 * ImGui.GetStyle().FramePadding.X;

    protected override int UpdateCurrentSelected(int currentSelected)
    {
        if (CurrentSelectionIdx >= 0 && CurrentSelection.Type == _currentType)
            return currentSelected;

        CurrentSelectionIdx = Items.IndexOf(i => i.Type == _currentType);
        if (CurrentSelectionIdx >= 0)
            CurrentSelection = Items[CurrentSelectionIdx];
        else
            CurrentSelection = default;
            
        return base.UpdateCurrentSelected(CurrentSelectionIdx);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var currency = Items[globalIdx];
        var isFavorite = _favoritesService.ContainsCurrency(currency.Type);
        
        // Draw favorite star
        if (DrawFavoriteStar(currency.Type, isFavorite))
        {
            // Star was clicked - toggle favorite
            if (CurrentSelectionIdx == globalIdx)
            {
                CurrentSelectionIdx = -1;
                _currentType = currency.Type;
                CurrentSelection = default;
            }
        }
        
        ImGui.SameLine();
        
        // Draw icon if item ID is available
        if (currency.ItemId.HasValue)
        {
            DrawCurrencyIcon(currency.ItemId.Value);
            ImGui.SameLine();
        }
        
        // Draw selectable text
        var ret = ImGui.Selectable(currency.Name, selected);
        
        // Draw category on right side (dimmed)
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, UiColors.CategoryColor))
        {
            var categoryText = $"[{currency.Category}]";
            var textWidth = ImGui.CalcTextSize(categoryText).X;
            var availWidth = ImGui.GetContentRegionAvail().X;
            if (availWidth > textWidth)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availWidth - textWidth);
                ImGui.TextUnformatted(categoryText);
            }
        }
        
        // If Shift is held, keep dropdown open (return false to prevent close)
        if (ret && ImGui.GetIO().KeyShift)
        {
            // Update selection but don't close
            _currentType = currency.Type;
            CurrentSelectionIdx = globalIdx;
            CurrentSelection = currency;
            return false;
        }
        
        return ret;
    }

    protected override void DrawList(float width, float itemHeight)
    {
        base.DrawList(width, itemHeight);
        if (NewSelection != null && Items.Count > NewSelection.Value)
        {
            var newCurrency = Items[NewSelection.Value];
            if (!Equals(CurrentSelection, newCurrency))
            {
                SelectionChanged?.Invoke(CurrentSelection, newCurrency);
            }
            CurrentSelection = newCurrency;
        }
    }

    private bool DrawFavoriteStar(TrackedDataType type, bool isFavorite)
    {
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
            return true;
        }

        return false;
    }

    private void DrawCurrencyIcon(uint itemId)
    {
        // Currency icons are typically based on item IDs
        // We need to look up the icon from the item sheet
        try
        {
            var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(GetItemIconId(itemId)));
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

    private ushort GetItemIconId(uint itemId)
    {
        // Common currency icon mappings - these are hardcoded for performance
        // In a full implementation, you'd look these up from the Item sheet
        return itemId switch
        {
            1 => 65002,      // Gil
            28 => 65086,     // Poetics
            44123 => 65098,  // Heliometry (capped)
            43693 => 65097,  // Aesthetics (uncapped)
            25199 => 65073,  // White Crafters' Scrip
            33913 => 65093,  // Purple Crafters' Scrip
            41784 => 65101,  // Orange Crafters' Scrip
            25200 => 65074,  // White Gatherers' Scrip
            33914 => 65094,  // Purple Gatherers' Scrip
            41785 => 65102,  // Orange Gatherers' Scrip
            28063 => 65088,  // Skybuilders' Scrip
            10307 => 65065,  // Centurio Seals
            36656 => 65094,  // Trophy Crystals
            _ => 65002       // Default to Gil icon
        };
    }

    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    /// <param name="previewName">The preview text to show when closed.</param>
    /// <param name="previewType">The current type for selection tracking.</param>
    /// <param name="width">The combo width.</param>
    /// <param name="innerWidth">The popup inner width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(string previewName, TrackedDataType previewType, float width, float innerWidth)
    {
        _innerWidth = innerWidth;
        _currentType = previewType;
        return Draw($"##{Label}", previewName, string.Empty, width, ImGui.GetTextLineHeightWithSpacing() + 4);
    }

    /// <summary>
    /// Draws the combo with automatic preview text from current selection.
    /// </summary>
    /// <param name="width">The combo width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(float width)
    {
        var preview = CurrentSelectionIdx >= 0 ? CurrentSelection.Name : "Select currency...";
        var type = CurrentSelectionIdx >= 0 ? CurrentSelection.Type : default;
        return Draw(preview, type, width, width);
    }

    /// <summary>
    /// Sets the current selection by type.
    /// </summary>
    public void SetSelection(TrackedDataType type)
    {
        _currentType = type;
        CurrentSelectionIdx = Items.IndexOf(i => i.Type == type);
        if (CurrentSelectionIdx >= 0)
            CurrentSelection = Items[CurrentSelectionIdx];
        else
            CurrentSelection = default;
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _currentType = default;
        CurrentSelectionIdx = -1;
        CurrentSelection = default;
    }
}
