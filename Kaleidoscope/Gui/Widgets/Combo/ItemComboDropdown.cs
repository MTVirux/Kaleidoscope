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
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// An item combo widget using ComboWidget.
/// Provides the same public interface as the legacy ItemComboDropdown.
/// </summary>
public sealed class ItemComboDropdown : ComboDropdownBase<GameItem, uint>
{
    private readonly ITextureProvider _textureProvider;
    private readonly IDataManager _dataManager;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly ConfigurationService? _configService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly bool _marketableOnly;
    private readonly bool _excludeCurrencies;

    /// <summary>
    /// Whether multi-select mode is enabled.
    /// </summary>
    public bool MultiSelectEnabled { get; set; }

    /// <summary>
    /// Gets the currently selected item ID, or 0 if none (single-select mode).
    /// </summary>
    public uint SelectedItemId => Widget.SelectedItem?.Id ?? 0;

    /// <summary>
    /// Gets the set of selected item IDs (multi-select mode).
    /// </summary>
    public IReadOnlySet<uint> SelectedItemIds => State.SelectedIds;

    /// <summary>
    /// Gets the currently selected item, or null if none (single-select mode).
    /// </summary>
    public ComboItem? SelectedItem => Widget.SelectedItem != null
        ? new ComboItem(Widget.SelectedItem.Id, Widget.SelectedItem.Name, Widget.SelectedItem.IconId)
        : null;

    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public event Action<uint>? SelectionChanged;

    public ItemComboDropdown(
        ITextureProvider textureProvider,
        IDataManager dataManager,
        FavoritesService favoritesService,
        PriceTrackingService? priceTrackingService,
        string label,
        bool marketableOnly = false,
        ConfigurationService? configService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        bool excludeCurrencies = false,
        bool multiSelect = false,
        string? emptySelectionText = null,
        bool showAllBulkAction = false,
        bool showNoneBulkAction = false)
        : base(favoritesService, label)
    {
        _textureProvider = textureProvider;
        _dataManager = dataManager;
        _priceTrackingService = priceTrackingService;
        _configService = configService;
        _trackedDataRegistry = trackedDataRegistry;
        _marketableOnly = marketableOnly;
        _excludeCurrencies = excludeCurrencies;
        MultiSelectEnabled = multiSelect;

        State = new ComboState<uint>
        {
            SortOrder = ComboSortOrder.Alphabetical
        };

        var config = new ComboConfig
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
            ShowAllBulkAction = showAllBulkAction,
            ShowNoneBulkAction = showNoneBulkAction,
            ShowFavoritesBulkAction = true,
            ShowInvertBulkAction = false,
            ShowAllOption = false,
            EmptySelectionText = emptySelectionText ?? "0 items",
            MultiSelectItemTypeSingular = "item",
            MultiSelectItemTypePlural = "items",
            ShowItemIds = true,
            ItemDisplayFormat = "{0}  ({1})"
        };

        Widget = new ComboWidget<GameItem, uint>(config, State);

        // Configure icon renderer
        Widget.WithIconRenderer(DrawItemIcon);

        // Configure filter
        Widget.WithFilter((item, filter) =>
            item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));

        Initialize();
    }

    protected override IEnumerable<uint> GetFavoriteIds() => FavoritesService.FavoriteItems;

    protected override List<GameItem> BuildItems() => BuildItemList();

    protected override void OnWidgetSelectionChanged(uint id)
    {
        SelectionChanged?.Invoke(id);
    }

    protected override void OnWidgetFavoriteToggled(uint id, bool isFavorite)
    {
        if (isFavorite)
            FavoritesService.AddItem(id);
        else
            FavoritesService.RemoveItem(id);
    }

    private void DrawItemIcon(GameItem item, Vector2 size)
        => ImGuiHelpers.DrawGameIcon(_textureProvider, null, item.IconId, size);

    private List<GameItem> BuildItemList()
    {
        var items = new List<GameItem>();
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

                items.Add(new GameItem
                {
                    Id = row.RowId,
                    Name = name,
                    IconId = row.Icon
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"[ItemComboDropdown] Error building item list: {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Draws the combo in multi-select mode at the specified width.
    /// </summary>
    public bool DrawMultiSelect(float width) => Draw(width);

    /// <summary>
    /// Sets the current selection by item ID.
    /// </summary>
    public void SetSelection(uint itemId)
    {
        Widget.SetSelection(itemId);
    }

    /// <summary>
    /// Sets the multi-selection to the specified item IDs.
    /// </summary>
    public void SetMultiSelection(IEnumerable<uint> itemIds)
    {
        Widget.SetMultiSelection(itemIds);
    }

    /// <summary>
    /// Gets the current multi-selection.
    /// </summary>
    public IReadOnlySet<uint> GetMultiSelection() => State.SelectedIds;

    /// <summary>
    /// Clears the current selection (both single and multi-select).
    /// </summary>
    public void ClearSelection()
    {
        Widget.ClearSelection();
    }

    /// <summary>
    /// Clears the multi-selection only.
    /// </summary>
    public void ClearMultiSelection()
    {
        State.SelectedIds.Clear();
    }

    /// <summary>
    /// Gets the selected item IDs and clears the selection (for add-and-clear workflow).
    /// </summary>
    public List<uint> ConsumeMultiSelection()
    {
        var result = State.SelectedIds.ToList();
        State.SelectedIds.Clear();
        return result;
    }

    /// <summary>
    /// Checks if any items are selected in multi-select mode.
    /// </summary>
    public bool HasMultiSelection => State.SelectedIds.Count > 0;
}
