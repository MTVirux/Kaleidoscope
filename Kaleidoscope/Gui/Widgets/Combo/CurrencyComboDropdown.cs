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

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// A currency combo widget using ComboWidget.
/// Provides the same public interface as the legacy CurrencyComboDropdown.
/// </summary>
public sealed class CurrencyComboDropdown : ComboDropdownBase<CurrencyItem, TrackedDataType>
{
    private readonly ITextureProvider _textureProvider;
    private readonly TrackedDataRegistry _registry;
    private readonly ItemDataService? _itemDataService;

    /// <summary>
    /// Whether multi-select mode is enabled.
    /// </summary>
    public bool MultiSelectEnabled { get; set; }

    /// <summary>
    /// Gets the currently selected currency type (single-select mode).
    /// </summary>
    public TrackedDataType SelectedType => Widget.SelectedItem?.Id ?? default;

    /// <summary>
    /// Gets the set of selected currency types (multi-select mode).
    /// </summary>
    public IReadOnlySet<TrackedDataType> SelectedTypes => State.SelectedIds;

    /// <summary>
    /// Gets the currently selected currency, or null if none (single-select mode).
    /// </summary>
    public ComboCurrency? SelectedCurrency => Widget.SelectedItem != null
        ? new ComboCurrency(
            Widget.SelectedItem.Id,
            Widget.SelectedItem.Name,
            Widget.SelectedItem.ShortName,
            Widget.SelectedItem.ItemId,
            Widget.SelectedItem.Category)
        : null;

    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public event Action<TrackedDataType>? SelectionChanged;

    public CurrencyComboDropdown(
        ITextureProvider textureProvider,
        TrackedDataRegistry registry,
        FavoritesService favoritesService,
        string label,
        ItemDataService? itemDataService = null,
        bool multiSelect = false)
        : base(favoritesService, label)
    {
        _textureProvider = textureProvider;
        _registry = registry;
        _itemDataService = itemDataService;
        MultiSelectEnabled = multiSelect;

        State = new ComboState<TrackedDataType>
        {
            SortOrder = ComboSortOrder.Custom
        };

        var config = new ComboConfig
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
            EmptySelectionText = "0 currencies",
            MultiSelectItemTypeSingular = "currency",
            MultiSelectItemTypePlural = "currencies",
            DefaultGroupMode = MTComboGroupDisplayMode.Flat
        };

        Widget = new ComboWidget<CurrencyItem, TrackedDataType>(config, State);

        // Configure grouping by category
        Widget.WithGrouping(item => item.Category.ToString());

        // Configure icon renderer
        Widget.WithIconRenderer(DrawCurrencyIcon);

        // Configure filter
        Widget.WithFilter((item, filter) =>
            item.Name.ToLowerInvariant().Contains(filter) ||
            item.ShortName.ToLowerInvariant().Contains(filter) ||
            item.Category.ToString().ToLowerInvariant().Contains(filter));

        // Configure custom comparer for primary currency ordering
        Widget.WithComparer((a, b, favorites) =>
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

        Initialize();
    }

    private static int GetPrimaryCurrencyOrder(TrackedDataType type) => type switch
    {
        TrackedDataType.Gil => 0,
        TrackedDataType.RetainerGil => 1,
        TrackedDataType.FreeCompanyGil => 2,
        TrackedDataType.InventoryValueItems => 3,
        _ => int.MaxValue
    };

    protected override IEnumerable<TrackedDataType> GetFavoriteIds() => FavoritesService.FavoriteCurrencies;

    protected override List<CurrencyItem> BuildItems() => BuildCurrencyList();

    protected override void OnWidgetSelectionChanged(TrackedDataType type)
    {
        SelectionChanged?.Invoke(type);
    }

    protected override void OnWidgetFavoriteToggled(TrackedDataType type, bool isFavorite)
    {
        if (isFavorite)
            FavoritesService.AddCurrency(type);
        else
            FavoritesService.RemoveCurrency(type);
    }

    private void DrawCurrencyIcon(CurrencyItem item, Vector2 size)
        // Resolve the icon source: prefer ItemId, fall back to IconId, then the raw value.
        => ImGuiHelpers.DrawGameIcon(_textureProvider, _itemDataService, item.ItemId ?? item.IconId, size);

    private List<CurrencyItem> BuildCurrencyList()
    {
        var items = new List<CurrencyItem>();

        foreach (var (type, def) in _registry.Definitions)
        {
            items.Add(new CurrencyItem
            {
                Id = type,
                Name = def.DisplayName,
                ShortName = def.ShortName,
                ItemId = def.ItemId,
                IconId = def.IconId,
                Category = def.Category
            });
        }

        return items;
    }

    /// <summary>
    /// Draws the combo in multi-select mode at the specified width.
    /// </summary>
    public bool DrawMultiSelect(float width) => Draw(width);

    /// <summary>
    /// Sets the current selection by type.
    /// </summary>
    public void SetSelection(TrackedDataType type)
    {
        Widget.SetSelection(type);
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        Widget.ClearSelection();
    }

    /// <summary>
    /// Sets the multi-selection to the specified currency types.
    /// </summary>
    public void SetMultiSelection(IEnumerable<TrackedDataType> types)
    {
        Widget.SetMultiSelection(types);
    }

    /// <summary>
    /// Gets the current multi-selection.
    /// </summary>
    public IReadOnlySet<TrackedDataType> GetMultiSelection() => State.SelectedIds;

    /// <summary>
    /// Clears the multi-selection.
    /// </summary>
    public void ClearMultiSelection()
    {
        State.SelectedIds.Clear();
    }

    /// <summary>
    /// Consumes the current multi-selection, returning and clearing the selected types.
    /// </summary>
    public HashSet<TrackedDataType> ConsumeMultiSelection()
    {
        var result = new HashSet<TrackedDataType>(State.SelectedIds);
        State.SelectedIds.Clear();
        return result;
    }

    /// <summary>
    /// Gets whether there are any currencies selected in multi-select mode.
    /// </summary>
    public bool HasMultiSelection => State.SelectedIds.Count > 0;
}
