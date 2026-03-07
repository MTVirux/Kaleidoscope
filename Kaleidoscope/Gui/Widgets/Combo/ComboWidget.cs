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
        
        return _config.MultiSelect ? DrawMultiSelect(width) : DrawSingleSelect(width);
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
    
    private bool DrawSingleSelect(float width)
    {
        var preview = SelectedItem != null ? FormatItemName(SelectedItem) : _config.Placeholder;
        
        ImGui.SetNextItemWidth(width);
        
        // Constrain popup width if configured
        var popupWidth = _config.PopupMaxWidth > 0 ? _config.PopupMaxWidth : (width > 0 ? Math.Max(width, 200) : 300);
        var popupMaxHeight = _config.ListHeight > 0 ? _config.ListHeight + 80 : 400; // +80 for search bar and controls
        ImGui.SetNextWindowSizeConstraints(new Vector2(popupWidth, 0), new Vector2(popupWidth, popupMaxHeight));
        
        if (!ImGui.BeginCombo($"##{_config.ComboId}", preview, ImGuiComboFlags.HeightLarge))
            return false;
        
        var changed = DrawContent();
        ImGui.EndCombo();
        
        return changed;
    }
    
    private bool DrawMultiSelect(float width)
    {
        var preview = BuildMultiSelectPreview();
        
        ImGui.SetNextItemWidth(width);
        
        // Constrain popup width if configured
        var popupWidth = _config.PopupMaxWidth > 0 ? _config.PopupMaxWidth : (width > 0 ? Math.Max(width, 200) : 300);
        var popupMaxHeight = _config.ListHeight > 0 ? _config.ListHeight + 80 : 400; // +80 for search bar and controls
        ImGui.SetNextWindowSizeConstraints(new Vector2(popupWidth, 0), new Vector2(popupWidth, popupMaxHeight));
        
        if (!ImGui.BeginCombo($"##{_config.ComboId}", preview, ImGuiComboFlags.HeightLarge))
            return false;
        
        var changed = DrawContent();
        ImGui.EndCombo();
        
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
        var filterText = _state.FilterText;
        
        // Search bar and controls
        if (_config.ShowSearch || _config.ShowSortToggle || _config.ShowGroupingToggle)
        {
            var controlsWidth = 0f;
            if (_config.ShowSortToggle) controlsWidth += 35f;
            if (_config.ShowGroupingToggle) controlsWidth += 25f;
            if (_config.MultiSelect && _config.ShowBulkActions) controlsWidth += 80f;
            
            if (_config.ShowSearch)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - controlsWidth - ImGui.GetStyle().ItemSpacing.X);
                if (ImGui.InputTextWithHint("##filter", _config.SearchPlaceholder, ref filterText, 100))
                {
                    _state.FilterText = filterText;
                }
            }
            
            // Sort toggle
            if (_config.ShowSortToggle)
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
            
            // Grouping toggle
            if (_config.ShowGroupingToggle && _groupKeyProvider != null)
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
            
            // Bulk actions for multi-select
            if (_config.MultiSelect && _config.ShowBulkActions)
            {
                // All button
                if (_config.ShowAllBulkAction)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("All"))
                    {
                        _state.AllSelected = true;
                        _state.SelectedIds.Clear();
                        changed = true;
                        MultiSelectionChanged?.Invoke(_state.SelectedIds);
                    }
                }
                
                // None button
                if (_config.ShowNoneBulkAction)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("None"))
                    {
                        _state.AllSelected = false;
                        _state.SelectedIds.Clear();
                        changed = true;
                        MultiSelectionChanged?.Invoke(_state.SelectedIds);
                    }
                }
                
                // Favorites bulk action
                if (_config.ShowFavoritesBulkAction && _state.Favorites.Count > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("\u2605")) // Star character
                    {
                        _state.AllSelected = false;
                        _state.SelectedIds.Clear();
                        foreach (var favId in _state.Favorites)
                        {
                            _state.SelectedIds.Add(favId);
                        }
                        changed = true;
                        MultiSelectionChanged?.Invoke(_state.SelectedIds);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Select favorites only");
                    }
                }
                
                // Invert bulk action
                if (_config.ShowInvertBulkAction && _items != null)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("\u21C4")) // Swap arrows character
                    {
                        if (_state.AllSelected)
                        {
                            // Invert from "all" means none
                            _state.AllSelected = false;
                            _state.SelectedIds.Clear();
                        }
                        else
                        {
                            var allIds = _items.Select(i => i.Id).ToHashSet();
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
                        changed = true;
                        MultiSelectionChanged?.Invoke(_state.SelectedIds);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Invert selection");
                    }
                }
            }
        }
        
        ImGui.Separator();
        
        // "All" option for multi-select
        if (_config.MultiSelect && _config.ShowAllOption)
        {
            var allChecked = _state.AllSelected;
            if (ImGui.Checkbox(_config.AllOptionLabel, ref allChecked))
            {
                _state.AllSelected = allChecked;
                if (allChecked)
                    _state.SelectedIds.Clear();
                changed = true;
                MultiSelectionChanged?.Invoke(_state.SelectedIds);
            }
            ImGui.Separator();
        }
        
        // Filter items
        var filterLower = _state.FilterText;
        var filteredItems = GetFilteredItems(filterLower);
        
        // Draw items (no limit - use virtual scrolling)
        var itemList = filteredItems.ToList();
        
        if (_state.GroupMode == MTComboGroupDisplayMode.Grouped && _groupKeyProvider != null)
        {
            changed |= DrawGroupedItems(itemList);
        }
        else
        {
            changed |= DrawFlatItems(itemList);
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
    
    private bool DrawGroupedItems(List<TItem> items)
    {
        var changed = false;
        
        // Group by primary key
        var grouped = items
            .GroupBy(i => _groupKeyProvider!(i) ?? "Other")
            .OrderBy(g => g.Key);
        
        foreach (var group in grouped)
        {
            var groupItems = group.ToList();
            var groupSelectedCount = groupItems.Count(i => _state.SelectedIds.Contains(i.Id));
            var allSelected = groupSelectedCount == groupItems.Count && groupItems.Count > 0;
            var partialSelected = groupSelectedCount > 0 && !allSelected;
            
            ImGui.PushID(group.Key);
            
            if (_config.MultiSelect)
            {
                // Checkbox for group selection
                ImGui.PushStyleColor(ImGuiCol.CheckMark, partialSelected ? ComboStyles.PartialCheckmark : ComboStyles.FullCheckmark);
                var groupCheck = allSelected || partialSelected;
                if (ImGui.Checkbox($"##grp", ref groupCheck))
                {
                    changed = true;
                    if (groupCheck)
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
            
            // Group header - starts collapsed
            if (ImGui.CollapsingHeader(group.Key))
            {
                ImGui.Indent();
                
                // Check for sub-grouping
                if (_subGroupKeyProvider != null)
                {
                    changed |= DrawSubGroupedItems(groupItems);
                }
                else
                {
                    foreach (var item in groupItems)
                    {
                        changed |= DrawItemRow(item);
                    }
                }
                
                ImGui.Unindent();
            }
            
            ImGui.PopID();
        }
        
        return changed;
    }
    
    private bool DrawSubGroupedItems(List<TItem> items)
    {
        var changed = false;
        
        var subGrouped = items
            .GroupBy(i => _subGroupKeyProvider!(i) ?? "Other")
            .OrderBy(g => g.Key);
        
        foreach (var subGroup in subGrouped)
        {
            var subItems = subGroup.ToList();
            var subSelectedCount = subItems.Count(i => _state.SelectedIds.Contains(i.Id));
            var allSelected = subSelectedCount == subItems.Count && subItems.Count > 0;
            var partialSelected = subSelectedCount > 0 && !allSelected;
            
            ImGui.PushID(subGroup.Key);
            
            if (_config.MultiSelect)
            {
                ImGui.PushStyleColor(ImGuiCol.CheckMark, partialSelected ? ComboStyles.PartialCheckmark : ComboStyles.FullCheckmark);
                var subCheck = allSelected || partialSelected;
                if (ImGui.Checkbox($"##sub", ref subCheck))
                {
                    changed = true;
                    if (subCheck)
                    {
                        foreach (var i in subItems)
                            _state.SelectedIds.Add(i.Id);
                        _state.AllSelected = false;
                    }
                    else
                    {
                        foreach (var i in subItems)
                            _state.SelectedIds.Remove(i.Id);
                        if (_state.SelectedIds.Count == 0 && _config.ShowAllOption)
                            _state.AllSelected = true;
                    }
                    MultiSelectionChanged?.Invoke(_state.SelectedIds);
                }
                ImGui.PopStyleColor();
                ImGui.SameLine();
            }
            
            // Sub-group header - starts collapsed
            if (ImGui.TreeNodeEx(subGroup.Key))
            {
                // Tertiary grouping or items
                if (_tertiaryGroupKeyProvider != null)
                {
                    changed |= DrawTertiaryGroupedItems(subItems);
                }
                else
                {
                    foreach (var item in subItems)
                    {
                        changed |= DrawItemRow(item);
                    }
                }
                
                ImGui.TreePop();
            }
            
            ImGui.PopID();
        }
        
        return changed;
    }
    
    private bool DrawTertiaryGroupedItems(List<TItem> items)
    {
        var changed = false;
        
        var tertGrouped = items
            .GroupBy(i => _tertiaryGroupKeyProvider!(i) ?? "Other")
            .OrderBy(g => g.Key);
        
        foreach (var tertGroup in tertGrouped)
        {
            var tertItems = tertGroup.ToList();
            var tertSelectedCount = tertItems.Count(i => _state.SelectedIds.Contains(i.Id));
            var allSelected = tertSelectedCount == tertItems.Count && tertItems.Count > 0;
            var partialSelected = tertSelectedCount > 0 && !allSelected;
            
            ImGui.PushID(tertGroup.Key);
            
            if (_config.MultiSelect)
            {
                ImGui.PushStyleColor(ImGuiCol.CheckMark, partialSelected ? ComboStyles.PartialCheckmark : ComboStyles.FullCheckmark);
                var tertCheck = allSelected || partialSelected;
                if (ImGui.Checkbox($"##tert", ref tertCheck))
                {
                    changed = true;
                    if (tertCheck)
                    {
                        foreach (var i in tertItems)
                            _state.SelectedIds.Add(i.Id);
                        _state.AllSelected = false;
                    }
                    else
                    {
                        foreach (var i in tertItems)
                            _state.SelectedIds.Remove(i.Id);
                        if (_state.SelectedIds.Count == 0 && _config.ShowAllOption)
                            _state.AllSelected = true;
                    }
                    MultiSelectionChanged?.Invoke(_state.SelectedIds);
                }
                ImGui.PopStyleColor();
                ImGui.SameLine();
            }
            
            // Tertiary group header - starts collapsed
            if (ImGui.TreeNodeEx(tertGroup.Key))
            {
                foreach (var item in tertItems)
                {
                    changed |= DrawItemRow(item);
                }
                ImGui.TreePop();
            }
            
            ImGui.PopID();
        }
        
        return changed;
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
                if (selected)
                {
                    _state.SelectedIds.Add(item.Id);
                    _state.AllSelected = false;
                }
                else
                {
                    _state.SelectedIds.Remove(item.Id);
                    if (_state.SelectedIds.Count == 0 && _config.ShowAllOption)
                        _state.AllSelected = true;
                }
                changed = true;
                MultiSelectionChanged?.Invoke(_state.SelectedIds);
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
                if (_state.SelectedIds.Contains(item.Id))
                    _state.SelectedIds.Remove(item.Id);
                else
                {
                    _state.SelectedIds.Add(item.Id);
                    _state.AllSelected = false;
                }
                changed = true;
                MultiSelectionChanged?.Invoke(_state.SelectedIds);
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
        _needsSort = false;
    }
    
    #endregion
}
