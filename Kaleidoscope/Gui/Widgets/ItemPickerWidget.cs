using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable item picker widget with search and optional marketable-only filter.
/// </summary>
public class ItemPickerWidget
{
    private readonly IDataManager _dataManager;
    private readonly ItemDataService _itemDataService;
    private readonly PriceTrackingService? _priceTrackingService;

    // Search state
    private string _searchText = string.Empty;
    private List<(uint Id, string Name)> _filteredItems = new();
    private DateTime _lastFilterTime = DateTime.MinValue;
    private string _lastSearchText = string.Empty;
    private bool _lastMarketableOnly = false;
    private bool _itemsCached = false;

    // All items cache (built on first use)
    private List<(uint Id, string Name)>? _allItemsCache;
    private List<(uint Id, string Name)>? _marketableItemsCache;

    // Configuration
    private const int MaxDisplayedItems = 100;
    private const float FilterDebounceMs = 150f;

    /// <summary>
    /// The currently selected item ID, or null if none selected.
    /// </summary>
    public uint? SelectedItemId { get; private set; }

    /// <summary>
    /// The name of the currently selected item, or null if none selected.
    /// </summary>
    public string? SelectedItemName { get; private set; }

    /// <summary>
    /// Creates a new ItemPickerWidget.
    /// </summary>
    /// <param name="dataManager">The Dalamud data manager for Excel sheet access.</param>
    /// <param name="itemDataService">The item data service for name lookups.</param>
    /// <param name="priceTrackingService">Optional price tracking service for marketable item filtering.</param>
    public ItemPickerWidget(
        IDataManager dataManager,
        ItemDataService itemDataService,
        PriceTrackingService? priceTrackingService = null)
    {
        _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        _itemDataService = itemDataService ?? throw new ArgumentNullException(nameof(itemDataService));
        _priceTrackingService = priceTrackingService;
    }

    /// <summary>
    /// Sets the currently selected item.
    /// </summary>
    /// <param name="itemId">The item ID to select.</param>
    public void SetSelectedItem(uint? itemId)
    {
        SelectedItemId = itemId;
        SelectedItemName = itemId.HasValue ? _itemDataService.GetItemName(itemId.Value) : null;
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        SelectedItemId = null;
        SelectedItemName = null;
    }

    /// <summary>
    /// Draws the item picker widget with a combo dropdown.
    /// </summary>
    /// <param name="label">The label for the combo box.</param>
    /// <param name="marketableOnly">If true, only show marketable items.</param>
    /// <param name="width">Optional width for the combo. If 0, uses available width.</param>
    /// <returns>True if the selection changed.</returns>
    public bool Draw(string label, bool marketableOnly = false, float width = 0)
    {
        var changed = false;

        // Set width if specified
        if (width > 0)
        {
            ImGui.SetNextItemWidth(width);
        }

        // Preview text for the combo
        var preview = SelectedItemName ?? "Select item...";

        if (ImGui.BeginCombo(label, preview))
        {
            // Search input at the top of the popup
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##ItemSearch", "Search items...", ref _searchText, 256))
            {
                // Search text changed, will filter on next frame
            }

            // Keep focus on search box when opened
            if (ImGui.IsWindowAppearing())
            {
                ImGui.SetKeyboardFocusHere(-1);
            }

            ImGui.Separator();

            // Update filtered items if needed
            UpdateFilteredItems(marketableOnly);

            // Show filtered items in a scrollable child
            var childSize = new Vector2(0, 300);
            if (ImGui.BeginChild("##ItemList", childSize, false))
            {
                if (_filteredItems.Count == 0)
                {
                    if (string.IsNullOrEmpty(_searchText))
                    {
                        ImGui.TextDisabled("Type to search for items...");
                    }
                    else
                    {
                        ImGui.TextDisabled("No items found");
                    }
                }
                else
                {
                    // Use clipper for efficient rendering of large lists
                    foreach (var (id, name) in _filteredItems)
                    {
                        var isSelected = SelectedItemId == id;
                        if (ImGui.Selectable($"{name}##{id}", isSelected))
                        {
                            SelectedItemId = id;
                            SelectedItemName = name;
                            changed = true;
                            ImGui.CloseCurrentPopup();
                        }

                        // Show item ID in tooltip
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip($"Item ID: {id}");
                        }
                    }

                    // Show count info
                    if (_filteredItems.Count >= MaxDisplayedItems)
                    {
                        ImGui.Separator();
                        ImGui.TextDisabled($"Showing first {MaxDisplayedItems} results. Refine your search.");
                    }
                }

                ImGui.EndChild();
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    /// <summary>
    /// Draws a compact item picker button that opens a popup.
    /// </summary>
    /// <param name="id">Unique ID for the popup.</param>
    /// <param name="marketableOnly">If true, only show marketable items.</param>
    /// <returns>True if the selection changed.</returns>
    public bool DrawButton(string id, bool marketableOnly = false)
    {
        var changed = false;
        var buttonLabel = SelectedItemName ?? "Select Item...";

        if (ImGui.Button($"{buttonLabel}##{id}"))
        {
            ImGui.OpenPopup($"ItemPicker_{id}");
        }

        if (ImGui.BeginPopup($"ItemPicker_{id}"))
        {
            changed = DrawPopupContent(marketableOnly);
            ImGui.EndPopup();
        }

        return changed;
    }

    /// <summary>
    /// Draws just the popup content (for custom popup implementations).
    /// </summary>
    /// <param name="marketableOnly">If true, only show marketable items.</param>
    /// <returns>True if the selection changed.</returns>
    public bool DrawPopupContent(bool marketableOnly = false)
    {
        var changed = false;

        // Search input
        ImGui.SetNextItemWidth(250);
        ImGui.InputTextWithHint("##ItemSearch", "Search items...", ref _searchText, 256);

        ImGui.Separator();

        // Update filtered items
        UpdateFilteredItems(marketableOnly);

        // Item list
        var childSize = new Vector2(300, 350);
        if (ImGui.BeginChild("##ItemListPopup", childSize, false))
        {
            if (_filteredItems.Count == 0)
            {
                if (string.IsNullOrEmpty(_searchText))
                {
                    ImGui.TextDisabled("Type to search...");
                }
                else
                {
                    ImGui.TextDisabled("No items found");
                }
            }
            else
            {
                foreach (var (id, name) in _filteredItems)
                {
                    var isSelected = SelectedItemId == id;
                    if (ImGui.Selectable($"{name}##{id}", isSelected))
                    {
                        SelectedItemId = id;
                        SelectedItemName = name;
                        changed = true;
                        ImGui.CloseCurrentPopup();
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Item ID: {id}");
                    }
                }

                if (_filteredItems.Count >= MaxDisplayedItems)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled($"Showing first {MaxDisplayedItems} results");
                }
            }

            ImGui.EndChild();
        }

        return changed;
    }

    /// <summary>
    /// Updates the filtered items list based on current search text.
    /// </summary>
    private void UpdateFilteredItems(bool marketableOnly)
    {
        // Check if we need to update
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastFilterTime).TotalMilliseconds;

        if (_searchText == _lastSearchText && 
            marketableOnly == _lastMarketableOnly && 
            elapsed < FilterDebounceMs &&
            _itemsCached)
        {
            return;
        }

        // Require at least 2 characters to search (to avoid huge result sets)
        if (string.IsNullOrEmpty(_searchText) || _searchText.Length < 2)
        {
            _filteredItems.Clear();
            _lastSearchText = _searchText;
            _lastMarketableOnly = marketableOnly;
            _lastFilterTime = now;
            _itemsCached = true;
            return;
        }

        // Build caches if needed
        EnsureItemsCached(marketableOnly);

        // Get the appropriate source list
        var sourceItems = marketableOnly && _marketableItemsCache != null
            ? _marketableItemsCache
            : _allItemsCache;

        if (sourceItems == null)
        {
            _filteredItems.Clear();
            return;
        }

        // Filter by search text (case-insensitive)
        var searchLower = _searchText.ToLowerInvariant();
        _filteredItems = sourceItems
            .Where(item => item.Name.Contains(searchLower, StringComparison.OrdinalIgnoreCase))
            .Take(MaxDisplayedItems)
            .ToList();

        _lastSearchText = _searchText;
        _lastMarketableOnly = marketableOnly;
        _lastFilterTime = now;
        _itemsCached = true;
    }

    /// <summary>
    /// Ensures the item caches are built.
    /// </summary>
    private void EnsureItemsCached(bool needsMarketable)
    {
        // Build all items cache
        if (_allItemsCache == null)
        {
            try
            {
                var itemSheet = _dataManager.GetExcelSheet<Item>();
                if (itemSheet != null)
                {
                    _allItemsCache = new List<(uint, string)>();
                    foreach (var item in itemSheet)
                    {
                        var name = item.Name.ExtractText();
                        // Skip items with empty names or invalid IDs
                        if (!string.IsNullOrWhiteSpace(name) && item.RowId > 0)
                        {
                            _allItemsCache.Add((item.RowId, name));
                        }
                    }
                    _allItemsCache.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[ItemPickerWidget] Error building item cache: {ex.Message}");
                _allItemsCache = new List<(uint, string)>();
            }
        }

        // Build marketable items cache if needed
        if (needsMarketable && _marketableItemsCache == null && _priceTrackingService != null)
        {
            var marketableSet = _priceTrackingService.MarketableItems;
            if (marketableSet != null && _allItemsCache != null)
            {
                _marketableItemsCache = _allItemsCache
                    .Where(item => marketableSet.Contains((int)item.Id))
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Clears the internal caches. Call this if item data may have changed.
    /// </summary>
    public void ClearCache()
    {
        _allItemsCache = null;
        _marketableItemsCache = null;
        _itemsCached = false;
    }

    /// <summary>
    /// Gets whether marketable items are available for filtering.
    /// </summary>
    public bool HasMarketableItems => _priceTrackingService?.MarketableItems != null;

    /// <summary>
    /// Gets the count of marketable items (if available).
    /// </summary>
    public int? MarketableItemCount => _priceTrackingService?.MarketableItems?.Count;
}
