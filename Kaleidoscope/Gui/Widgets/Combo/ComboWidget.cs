using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// A generic, reusable combo/dropdown widget with support for:
/// - Single and multi-select modes
/// - Favorites (with persistent star toggles)
/// - Icons (via delegate)
/// - Sorting (alphabetical, by ID, or custom)
/// - Optional hierarchical grouping
/// - Search/filter functionality
/// </summary>
/// <typeparam name="TItem">The item type (must implement IComboItem).</typeparam>
/// <typeparam name="TId">The item ID type.</typeparam>
public class ComboWidget<TItem, TId> 
    where TItem : IComboItem<TId>
    where TId : notnull
{
    private readonly ComboConfig _config;
    private readonly ComboState<TId> _state;
    
    // Cached items
    private IReadOnlyList<TItem>? _items;
    private List<TItem>? _sortedItems;
    private bool _needsSort = true;
    private int _sortVersion;

    // Cached filtered view (keyed on filter text + sort version)
    private List<TItem>? _filteredItems;
    private string? _filteredFilterText;
    private int _filteredSortVersion = -1;
    
    // Renderers and providers (set by consumer)
    private MTIconRenderer<TItem>? _iconRenderer;
    private MTSecondaryTextProvider<TItem>? _secondaryTextProvider;
    private MTItemFilter<TItem>? _customFilter;
    private MTItemComparer<TItem, TId>? _customComparer;
    private Func<TItem, string?>? _groupKeyProvider;
    private Func<TItem, string?>? _subGroupKeyProvider;
    private Func<TItem, string?>? _tertiaryGroupKeyProvider;
    
    // Events
    /// <summary>Event fired when single selection changes. Value is the selected item's ID.</summary>
    public event Action<TId>? SelectionChanged;
    
    /// <summary>Event fired when multi-selection changes.</summary>
    public event Action<IReadOnlySet<TId>>? MultiSelectionChanged;
    
    /// <summary>Event fired when a favorite is toggled.</summary>
    public event Action<TId, bool>? FavoriteToggled;
    
    /// <summary>Event fired when state changes (for persistence).</summary>
    public event Action? StateChanged;
    
    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    public ComboConfig Config => _config;
    
    /// <summary>
    /// Gets the current state (for persistence).
    /// </summary>
    public ComboState<TId> State => _state;
    
    /// <summary>
    /// Gets the currently selected item (single-select mode).
    /// </summary>
    public TItem? SelectedItem
    {
        get
        {
            if (_state.SelectedId == null || _items == null) return default;
            return _items.FirstOrDefault(i => EqualityComparer<TId>.Default.Equals(i.Id, _state.SelectedId));
        }
    }
    
    /// <summary>
    /// Gets whether "All" is selected (multi-select mode).
    /// </summary>
    public bool IsAllSelected => _state.AllSelected;
    
    /// <summary>
    /// Gets the selected IDs for data loading.
    /// Returns null if "All" is selected.
    /// </summary>
    public IReadOnlyList<TId>? GetSelectedIdsForLoading()
    {
        if (!_config.MultiSelect)
            return _state.SelectedId != null ? new[] { _state.SelectedId } : null;
        
        if (_state.AllSelected || _state.SelectedIds.Count == 0)
            return null;
        
        return _state.SelectedIds.ToList();
    }
    
    /// <summary>
    /// Creates a new ComboWidget.
    /// </summary>
    /// <param name="config">Widget configuration.</param>
    /// <param name="state">Optional external state for persistence. If null, creates internal state.</param>
    public ComboWidget(ComboConfig config, ComboState<TId>? state = null)
    {
        _config = config;
        _state = state ?? new ComboState<TId>
        {
            SortOrder = config.DefaultSortOrder,
            GroupMode = config.DefaultGroupMode
        };
    }
    
    #region Configuration Methods
    
    /// <summary>
    /// Sets the icon renderer delegate.
    /// </summary>
    public ComboWidget<TItem, TId> WithIconRenderer(MTIconRenderer<TItem> renderer)
    {
        _iconRenderer = renderer;
        return this;
    }
    
    /// <summary>
    /// Sets the secondary text provider (e.g., for world names, categories).
    /// </summary>
    public ComboWidget<TItem, TId> WithSecondaryText(MTSecondaryTextProvider<TItem> provider)
    {
        _secondaryTextProvider = provider;
        return this;
    }
    
    /// <summary>
    /// Sets a custom filter function.
    /// </summary>
    public ComboWidget<TItem, TId> WithFilter(MTItemFilter<TItem> filter)
    {
        _customFilter = filter;
        return this;
    }
    
    /// <summary>
    /// Sets a custom comparer for sorting.
    /// </summary>
    public ComboWidget<TItem, TId> WithComparer(MTItemComparer<TItem, TId> comparer)
    {
        _customComparer = comparer;
        return this;
    }
    
    /// <summary>
    /// Sets group key providers for hierarchical grouping.
    /// </summary>
    public ComboWidget<TItem, TId> WithGrouping(
        Func<TItem, string?> groupKey,
        Func<TItem, string?>? subGroupKey = null,
        Func<TItem, string?>? tertiaryGroupKey = null)
    {
        _groupKeyProvider = groupKey;
        _subGroupKeyProvider = subGroupKey;
        _tertiaryGroupKeyProvider = tertiaryGroupKey;
        return this;
    }
    
    #endregion
    
    #region Data Management
    
    /// <summary>
    /// Sets the items to display.
    /// </summary>
    public void SetItems(IReadOnlyList<TItem> items)
    {
        _items = items;
        _needsSort = true;
    }
    
    /// <summary>
    /// Forces a re-sort of items.
    /// </summary>
    public void InvalidateSort()
    {
        _needsSort = true;
    }
    
    /// <summary>
    /// Sets the selection (single-select mode).
    /// </summary>
    public void SetSelection(TId? id)
    {
        _state.SelectedId = id;
        _state.AllSelected = false;
    }
    
    /// <summary>
    /// Sets the selection (multi-select mode).
    /// </summary>
    public void SetMultiSelection(IEnumerable<TId> ids)
    {
        _state.SelectedIds.Clear();
        foreach (var id in ids)
            _state.SelectedIds.Add(id);
        _state.AllSelected = false;
    }
    
    /// <summary>
    /// Clears all selections.
    /// </summary>
    public void ClearSelection()
    {
        _state.SelectedId = default;
        _state.SelectedIds.Clear();
        _state.AllSelected = _config.MultiSelect && _config.ShowAllOption;
    }
    
    /// <summary>
    /// Syncs favorites from an external source. Call this when external favorites change.
    /// </summary>
    /// <param name="favoriteIds">The set of favorite IDs.</param>
    public void SyncFavorites(IEnumerable<TId> favoriteIds)
    {
        _state.Favorites.Clear();
        foreach (var id in favoriteIds)
            _state.Favorites.Add(id);
        _needsSort = true;
    }
    
    /// <summary>
    /// Checks if an item is marked as favorite.
    /// </summary>
    public bool IsFavorite(TId id) => _state.Favorites.Contains(id);
    
    /// <summary>
    /// Gets all favorite IDs.
    /// </summary>
    public IReadOnlySet<TId> Favorites => _state.Favorites;
    
    #endregion
    
    #region Rendering
    
    /// <summary>
    /// Draws the combo widget.
    /// </summary>
    /// <param name="width">Widget width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(float width)
    {
        EnsureSorted();
        
        var preview = _config.MultiSelect
            ? BuildMultiSelectPreview()
            : (SelectedItem != null ? FormatItemName(SelectedItem) : _config.Placeholder);
        
        ImGui.SetNextItemWidth(width);
        
        var popupWidth = _config.PopupMaxWidth > 0 ? _config.PopupMaxWidth : (width > 0 ? Math.Max(width, 200) : 300);
        var popupMaxHeight = _config.ListHeight > 0 ? _config.ListHeight + 80 : 400;
        ImGui.SetNextWindowSizeConstraints(new Vector2(popupWidth, 0), new Vector2(popupWidth, popupMaxHeight));
        
        if (!ImGui.BeginCombo($"##{_config.ComboId}", preview, ImGuiComboFlags.HeightLarge))
            return false;
        
        var changed = DrawContent();
        ImGui.EndCombo();
        
        return changed;
    }
    
    /// <summary>
    /// Draws an inline version (no popup, renders directly).
    /// </summary>
    /// <param name="width">Widget width.</param>
    /// <param name="height">Widget height.</param>
    /// <returns>True if selection changed.</returns>
    public bool DrawInline(float width, float height)
    {
        EnsureSorted();
        
        var changed = false;
        
        if (ImGui.BeginChild($"##{_config.ComboId}_inline", new Vector2(width, height), true))
        {
            changed = DrawContent();
        }
        ImGui.EndChild();
        
        return changed;
    }
    
    private string BuildMultiSelectPreview()
    {
        if (_state.AllSelected)
            return _config.AllOptionLabel;
        
        if (_state.SelectedIds.Count == 0)
        {
            if (_config.ShowAllOption)
                return _config.AllOptionLabel;
            return _config.EmptySelectionText ?? _config.Placeholder;
        }
        
        if (_state.SelectedIds.Count == 1 && _items != null)
        {
            var id = _state.SelectedIds.First();
            var item = _items.FirstOrDefault(i => EqualityComparer<TId>.Default.Equals(i.Id, id));
            if (item != null)
                return FormatItemName(item);
        }
        
        // Build multi-select text with item type if configured
        var count = _state.SelectedIds.Count;
        if (_config.MultiSelectItemTypeSingular != null)
        {
            var itemType = count == 1 
                ? _config.MultiSelectItemTypeSingular 
                : (_config.MultiSelectItemTypePlural ?? _config.MultiSelectItemTypeSingular + "s");
            return $"{count} {itemType} selected";
        }
        
        return $"{count} selected";
    }
    
    private bool DrawContent()
    {
        var changed = false;

        if (_config.ShowSearch || _config.ShowSortToggle || _config.ShowGroupingToggle)
            changed |= DrawControlsRow();

        ImGui.Separator();

        if (_config.MultiSelect && _config.ShowAllOption)
        {
            changed |= DrawAllOption();
            ImGui.Separator();
        }

        var itemList = GetFilteredItemsCached();

        if (_state.GroupMode == MTComboGroupDisplayMode.Grouped && _groupKeyProvider != null)
            changed |= DrawGroupedItems(itemList);
        else
            changed |= DrawFlatItems(itemList);

        return changed;
    }

    private bool DrawControlsRow()
    {
        var controlsWidth = 0f;
        if (_config.ShowSortToggle) controlsWidth += 35f;
        if (_config.ShowGroupingToggle) controlsWidth += 25f;
        if (_config.MultiSelect && _config.ShowBulkActions) controlsWidth += 80f;

        if (_config.ShowSearch)
            DrawSearchInput(controlsWidth);

        if (_config.ShowSortToggle)
            DrawSortToggle();

        if (_config.ShowGroupingToggle && _groupKeyProvider != null)
            DrawGroupingToggle();

        if (_config.MultiSelect && _config.ShowBulkActions)
            return DrawBulkActions();

        return false;
    }

    private void DrawSearchInput(float controlsWidth)
    {
        var filterText = _state.FilterText;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - controlsWidth - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.InputTextWithHint("##filter", _config.SearchPlaceholder, ref filterText, 100))
            _state.FilterText = filterText;
    }

    private void DrawSortToggle()
    {
        ImGui.SameLine();
        var sortLabel = _state.SortOrder == ComboSortOrder.Alphabetical ? "A-Z" : "ID";
        if (ImGui.SmallButton(sortLabel))
        {
            _state.SortOrder = _state.SortOrder == ComboSortOrder.Alphabetical
                ? ComboSortOrder.ById
                : ComboSortOrder.Alphabetical;
            _needsSort = true;
            StateChanged?.Invoke();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_state.SortOrder == ComboSortOrder.Alphabetical
                ? "Sort alphabetically. Click to sort by ID."
                : "Sort by ID. Click to sort alphabetically.");
        }
    }

    private void DrawGroupingToggle()
    {
        ImGui.SameLine();
        var groupColor = _state.GroupMode == MTComboGroupDisplayMode.Grouped ? 0xFF00FF00u : 0xFF888888u;
        ImGui.PushStyleColor(ImGuiCol.Text, groupColor);
        if (ImGui.SmallButton("G"))
        {
            _state.GroupMode = _state.GroupMode == MTComboGroupDisplayMode.Flat
                ? MTComboGroupDisplayMode.Grouped
                : MTComboGroupDisplayMode.Flat;
            StateChanged?.Invoke();
        }
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_state.GroupMode == MTComboGroupDisplayMode.Grouped
                ? "Grouped view. Click for flat list."
                : "Flat list. Click to group.");
        }
    }

    /// <summary>
    /// Draws the multi-select bulk-action buttons from a data-driven descriptor list.
    /// Each descriptor supplies a visibility flag, button label, optional tooltip, and the
    /// mutation to apply; a triggered action marks the selection changed and fires the event.
    /// </summary>
    private bool DrawBulkActions()
    {
        var changed = false;

        var actions = new (bool Show, string Label, string? Tooltip, Action Apply)[]
        {
            (_config.ShowAllBulkAction, "All", null, ApplySelectAll),
            (_config.ShowNoneBulkAction, "None", null, ApplySelectNone),
            (_config.ShowFavoritesBulkAction && _state.Favorites.Count > 0, "\u2605", "Select favorites only", ApplySelectFavorites),
            (_config.ShowInvertBulkAction && _items != null, "\u21C4", "Invert selection", ApplyInvertSelection),
        };

        foreach (var action in actions)
        {
            if (!action.Show)
                continue;

            ImGui.SameLine();
            if (ImGui.SmallButton(action.Label))
            {
                action.Apply();
                changed = true;
                MultiSelectionChanged?.Invoke(_state.SelectedIds);
            }
            if (action.Tooltip != null && ImGui.IsItemHovered())
                ImGui.SetTooltip(action.Tooltip);
        }

        return changed;
    }

    private void ApplySelectAll()
    {
        _state.AllSelected = true;
        _state.SelectedIds.Clear();
    }

    private void ApplySelectNone()
    {
        _state.AllSelected = false;
        _state.SelectedIds.Clear();
    }

    private void ApplySelectFavorites()
    {
        _state.AllSelected = false;
        _state.SelectedIds.Clear();
        foreach (var favId in _state.Favorites)
            _state.SelectedIds.Add(favId);
    }

    private void ApplyInvertSelection()
    {
        if (_state.AllSelected)
        {
            // Invert from "all" means none
            _state.AllSelected = false;
            _state.SelectedIds.Clear();
            return;
        }

        var allIds = _items!.Select(i => i.Id).ToHashSet();
        var inverted = allIds.Except(_state.SelectedIds).ToHashSet();
        _state.SelectedIds.Clear();
        foreach (var id in inverted)
            _state.SelectedIds.Add(id);

        // If all are now selected, switch to "All" mode
        if (_state.SelectedIds.Count == allIds.Count && _config.ShowAllOption)
        {
            _state.AllSelected = true;
            _state.SelectedIds.Clear();
        }
    }

    private bool DrawAllOption()
    {
        var changed = false;
        var allChecked = _state.AllSelected;
        if (ImGui.Checkbox(_config.AllOptionLabel, ref allChecked))
        {
            _state.AllSelected = allChecked;
            if (allChecked)
                _state.SelectedIds.Clear();
            changed = true;
            MultiSelectionChanged?.Invoke(_state.SelectedIds);
        }
        return changed;
    }
    
    private bool DrawFlatItems(List<TItem> items)
    {
        var changed = false;
        
        // Use ImGuiListClipper for virtual scrolling when there are many items
        if (items.Count > 50)
        {
            unsafe
            {
                var clipper = ImGui.ImGuiListClipper();
                clipper.Begin(items.Count, -1f);
                
                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        if (i >= 0 && i < items.Count)
                        {
                            changed |= DrawItemRow(items[i]);
                        }
                    }
                }
                
                clipper.End();
                clipper.Destroy();
            }
        }
        else
        {
            // For small lists, render directly without clipper overhead
            foreach (var item in items)
            {
                changed |= DrawItemRow(item);
            }
        }
        
        return changed;
    }
    
    /// <summary>
    /// Ordered array of group key providers for recursive grouping.
    /// </summary>
    private Func<TItem, string?>?[] GroupKeyProviders =>
        [_groupKeyProvider, _subGroupKeyProvider, _tertiaryGroupKeyProvider];
    
    private bool DrawGroupedItems(List<TItem> items)
        => DrawGroupLevel(0, items);
    
    /// <summary>
    /// Recursively draws grouped items at the specified nesting level.
    /// Level 0 uses CollapsingHeader + Indent; deeper levels use TreeNodeEx + TreePop.
    /// </summary>
    private bool DrawGroupLevel(int level, List<TItem> items)
    {
        var providers = GroupKeyProviders;
        if (level >= providers.Length || providers[level] == null)
        {
            var changed = false;
            foreach (var item in items)
                changed |= DrawItemRow(item);
            return changed;
        }
        
        var keyProvider = providers[level]!;
        var grouped = items
            .GroupBy(i => keyProvider(i) ?? "Other")
            .OrderBy(g => g.Key);
        
        var result = false;
        
        foreach (var group in grouped)
        {
            var groupItems = group.ToList();
            var selectedCount = groupItems.Count(i => _state.SelectedIds.Contains(i.Id));
            var allSelected = selectedCount == groupItems.Count && groupItems.Count > 0;
            var partialSelected = selectedCount > 0 && !allSelected;
            
            ImGui.PushID(group.Key);
            
            if (_config.MultiSelect)
            {
                ImGui.PushStyleColor(ImGuiCol.CheckMark, partialSelected ? ComboStyles.PartialCheckmark : ComboStyles.FullCheckmark);
                var check = allSelected || partialSelected;
                if (ImGui.Checkbox($"##lvl{level}", ref check))
                {
                    result = true;
                    if (check)
                    {
                        foreach (var i in groupItems)
                            _state.SelectedIds.Add(i.Id);
                        _state.AllSelected = false;
                    }
                    else
                    {
                        foreach (var i in groupItems)
                            _state.SelectedIds.Remove(i.Id);
                        if (_state.SelectedIds.Count == 0 && _config.ShowAllOption)
                            _state.AllSelected = true;
                    }
                    MultiSelectionChanged?.Invoke(_state.SelectedIds);
                }
                ImGui.PopStyleColor();
                ImGui.SameLine();
            }
            
            // Level 0 uses CollapsingHeader + manual indent; deeper levels use TreeNodeEx + TreePop
            bool opened;
            if (level == 0)
            {
                opened = ImGui.CollapsingHeader(group.Key);
                if (opened) ImGui.Indent();
            }
            else
            {
                opened = ImGui.TreeNodeEx(group.Key);
            }
            
            if (opened)
            {
                result |= DrawGroupLevel(level + 1, groupItems);
                
                if (level == 0)
                    ImGui.Unindent();
                else
                    ImGui.TreePop();
            }
            
            ImGui.PopID();
        }
        
        return result;
    }
    
    private bool DrawItemRow(TItem item)
    {
        var changed = false;
        var isSelected = _config.MultiSelect 
            ? _state.SelectedIds.Contains(item.Id) 
            : EqualityComparer<TId>.Default.Equals(_state.SelectedId, item.Id);
        
        ImGui.PushID(item.Id.GetHashCode());
        
        // Selection highlight for selected items
        if (isSelected && !_state.AllSelected)
        {
            var cursorPos = ImGui.GetCursorScreenPos();
            var rowHeight = ImGui.GetTextLineHeightWithSpacing();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.GetWindowDrawList().AddRectFilled(
                cursorPos,
                cursorPos + new Vector2(rowWidth, rowHeight),
                ComboStyles.SelectedBackground);
        }
        
        // Favorite star
        if (_config.ShowFavorites)
        {
            if (DrawFavoriteStar(item.Id))
            {
                changed = true;
            }
            ImGui.SameLine();
        }
        
        // Multi-select checkbox
        if (_config.MultiSelect)
        {
            var selected = isSelected;
            if (ImGui.Checkbox($"##sel", ref selected))
            {
                ToggleMultiSelectItem(item.Id);
                changed = true;
            }
            ImGui.SameLine();
        }
        
        // Icon
        if (_config.ShowIcons && _iconRenderer != null)
        {
            _iconRenderer(item, _config.IconSize);
            ImGui.SameLine();
        }
        
        // Item content
        var displayText = FormatItemName(item);
        
        if (_config.MultiSelect)
        {
            ImGui.TextUnformatted(displayText);
            
            // Allow clicking row to toggle
            if (ImGui.IsItemClicked())
            {
                ToggleMultiSelectItem(item.Id);
                changed = true;
            }
        }
        else
        {
            if (ImGui.Selectable(displayText, isSelected))
            {
                _state.SelectedId = item.Id;
                _state.AllSelected = false;
                changed = true;
                SelectionChanged?.Invoke(item.Id);
            }
        }
        
        // Secondary text
        if (_secondaryTextProvider != null)
        {
            var secondary = _secondaryTextProvider(item);
            if (!string.IsNullOrEmpty(secondary))
            {
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, ComboStyles.SecondaryText);
                ImGui.TextUnformatted(secondary);
                ImGui.PopStyleColor();
            }
        }
        
        ImGui.PopID();
        
        return changed;
    }
    
    /// <summary>
    /// Toggles an item's multi-select state and fires the change event.
    /// </summary>
    private void ToggleMultiSelectItem(TId id)
    {
        if (_state.SelectedIds.Contains(id))
        {
            _state.SelectedIds.Remove(id);
            if (_state.SelectedIds.Count == 0 && _config.ShowAllOption)
                _state.AllSelected = true;
        }
        else
        {
            _state.SelectedIds.Add(id);
            _state.AllSelected = false;
        }
        MultiSelectionChanged?.Invoke(_state.SelectedIds);
    }
    
    private bool DrawFavoriteStar(TId id)
    {
        var isFavorite = _state.Favorites.Contains(id);
        var cursorPos = ImGui.GetCursorScreenPos();
        var hovering = ImGui.IsMouseHoveringRect(cursorPos, cursorPos + _config.StarSize);
        
        var color = ComboStyles.GetFavoriteStarColor(isFavorite, hovering);
        
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted("\u2605"); // Unicode star character
        ImGui.PopStyleColor();
        
        if (ImGui.IsItemClicked())
        {
            if (isFavorite)
                _state.Favorites.Remove(id);
            else
                _state.Favorites.Add(id);
            
            _needsSort = true; // Re-sort to move favorites to top
            FavoriteToggled?.Invoke(id, !isFavorite);
            StateChanged?.Invoke();
            return true;
        }
        
        return false;
    }
    
    private string FormatItemName(TItem item)
    {
        if (_config.ShowItemIds)
            return string.Format(_config.ItemDisplayFormat, item.Name, item.Id);
        return item.Name;
    }
    
    #endregion
    
    #region Filtering and Sorting
    
    /// <summary>
    /// Returns the filtered item list, reusing the cached result while the filter text and
    /// sorted-item version are unchanged. Avoids re-running the filter LINQ and allocating a
    /// new list every frame.
    /// </summary>
    private List<TItem> GetFilteredItemsCached()
    {
        var filterText = _state.FilterText;
        if (_filteredItems != null
            && _filteredSortVersion == _sortVersion
            && string.Equals(_filteredFilterText, filterText, StringComparison.Ordinal))
        {
            return _filteredItems;
        }

        _filteredItems = GetFilteredItems(filterText).ToList();
        _filteredFilterText = filterText;
        _filteredSortVersion = _sortVersion;
        return _filteredItems;
    }

    private IEnumerable<TItem> GetFilteredItems(string filterText)
    {
        if (_sortedItems == null) return Enumerable.Empty<TItem>();
        
        if (string.IsNullOrEmpty(filterText))
            return _sortedItems;
        
        if (_customFilter != null)
            return _sortedItems.Where(i => _customFilter(i, filterText));
        
        return _sortedItems.Where(i => 
            i.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
            i.Id.ToString()!.Contains(filterText, StringComparison.OrdinalIgnoreCase));
    }
    
    private void EnsureSorted()
    {
        if (!_needsSort && _sortedItems != null) return;
        if (_items == null)
        {
            _sortedItems = new List<TItem>();
            return;
        }
        
        var sorted = _items.ToList();
        
        if (_customComparer != null && _state.SortOrder == ComboSortOrder.Custom)
        {
            sorted.Sort((a, b) => _customComparer(a, b, _state.Favorites));
        }
        else
        {
            sorted.Sort((a, b) =>
            {
                // Favorites always first
                var aFav = _state.Favorites.Contains(a.Id);
                var bFav = _state.Favorites.Contains(b.Id);
                if (aFav != bFav)
                    return bFav.CompareTo(aFav);
                
                // Then by sort order
                return _state.SortOrder switch
                {
                    ComboSortOrder.ById => Comparer<TId>.Default.Compare(a.Id, b.Id),
                    _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                };
            });
        }
        
        _sortedItems = sorted;
        _sortVersion++;
        _needsSort = false;
    }

    #endregion
}
