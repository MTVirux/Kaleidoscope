namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// Interface for items that can be displayed in a ComboWidget.
/// Implement this interface to define your item's identity and display properties.
/// </summary>
/// <typeparam name="TId">The type of the item's unique identifier (e.g., uint, ulong, enum).</typeparam>
public interface IComboItem<TId> where TId : notnull
{
    /// <summary>
    /// Gets the unique identifier for this item.
    /// </summary>
    TId Id { get; }
    
    /// <summary>
    /// Gets the display name for this item.
    /// </summary>
    string Name { get; }
}

/// <summary>
/// Extended interface for items that support grouping.
/// </summary>
/// <typeparam name="TId">The type of the item's unique identifier.</typeparam>
public interface IGroupableComboItem<TId> : IComboItem<TId> where TId : notnull
{
    /// <summary>
    /// Gets the primary group key (e.g., Region, Category).
    /// Null means ungrouped.
    /// </summary>
    string? Group { get; }
    
    /// <summary>
    /// Gets the secondary group key (e.g., DataCenter, SubCategory).
    /// Null means no sub-group.
    /// </summary>
    string? SubGroup { get; }
    
    /// <summary>
    /// Gets the tertiary group key (e.g., World).
    /// Null means no tertiary grouping.
    /// </summary>
    string? TertiaryGroup { get; }
}

/// <summary>
/// Configuration for ComboWidget behavior and appearance.
/// </summary>
public class ComboConfig
{
    /// <summary>
    /// Unique ID for ImGui identification.
    /// </summary>
    public required string ComboId { get; init; }
    
    /// <summary>
    /// Placeholder text shown when nothing is selected.
    /// </summary>
    public string Placeholder { get; init; } = "Select...";
    
    /// <summary>
    /// Search placeholder text.
    /// </summary>
    public string SearchPlaceholder { get; init; } = "Search...";
    
    /// <summary>
    /// Whether to enable multi-select mode.
    /// </summary>
    public bool MultiSelect { get; init; } = false;
    
    /// <summary>
    /// Whether to show a search filter input.
    /// </summary>
    public bool ShowSearch { get; init; } = true;
    
    /// <summary>
    /// Whether to show favorite stars.
    /// </summary>
    public bool ShowFavorites { get; init; } = true;
    
    /// <summary>
    /// Whether to show icons for items.
    /// </summary>
    public bool ShowIcons { get; init; } = true;
    
    /// <summary>
    /// Whether to show the sort toggle button.
    /// </summary>
    public bool ShowSortToggle { get; init; } = true;
    
    /// <summary>
    /// Whether to show grouping controls.
    /// </summary>
    public bool ShowGroupingToggle { get; init; } = true;
    
    /// <summary>
    /// Whether to show "Select All" / "Clear All" buttons in multi-select.
    /// </summary>
    public bool ShowBulkActions { get; init; } = true;
    
    /// <summary>
    /// Whether to show the "All" bulk select button in multi-select. Requires ShowBulkActions to be true.
    /// </summary>
    public bool ShowAllBulkAction { get; init; } = false;
    
    /// <summary>
    /// Whether to show the "None" bulk select button in multi-select. Requires ShowBulkActions to be true.
    /// </summary>
    public bool ShowNoneBulkAction { get; init; } = false;
    
    /// <summary>
    /// Whether to show "Favorites" bulk select button in multi-select.
    /// </summary>
    public bool ShowFavoritesBulkAction { get; init; } = true;
    
    /// <summary>
    /// Whether to show "Invert" bulk select button in multi-select.
    /// </summary>
    public bool ShowInvertBulkAction { get; init; } = true;
    
    /// <summary>
    /// Maximum number of items to display (for performance). 0 = unlimited.
    /// </summary>
    public int MaxDisplayedItems { get; init; } = 100;
    
    /// <summary>
    /// Height of the item list in pixels. 0 = auto.
    /// </summary>
    public float ListHeight { get; init; } = 300f;
    
    /// <summary>
    /// Size of item icons in pixels.
    /// </summary>
    public Vector2 IconSize { get; init; } = new(20, 20);
    
    /// <summary>
    /// Size of favorite star icons in pixels.
    /// </summary>
    public Vector2 StarSize { get; init; } = new(16, 16);
    
    /// <summary>
    /// Default sort order.
    /// </summary>
    public ComboSortOrder DefaultSortOrder { get; init; } = ComboSortOrder.Alphabetical;
    
    /// <summary>
    /// Default group display mode.
    /// </summary>
    public MTComboGroupDisplayMode DefaultGroupMode { get; init; } = MTComboGroupDisplayMode.Flat;
    
    /// <summary>
    /// Whether to include an "All" option at the top (for multi-select).
    /// </summary>
    public bool ShowAllOption { get; init; } = true;
    
    /// <summary>
    /// Label for the "All" option.
    /// </summary>
    public string AllOptionLabel { get; init; } = "All";
    
    /// <summary>
    /// Text to show in multi-select mode when no items are selected.
    /// Use null to fall back to Placeholder text.
    /// </summary>
    public string? EmptySelectionText { get; init; } = "0 selected";
    
    /// <summary>
    /// Singular item type name for multi-select text (e.g., "item", "currency").
    /// Used to generate "X items selected" or "1 item selected" text.
    /// If null, falls back to generic "X selected" text.
    /// </summary>
    public string? MultiSelectItemTypeSingular { get; init; }
    
    /// <summary>
    /// Plural item type name for multi-select text (e.g., "items", "currencies").
    /// If null, defaults to adding 's' to singular form.
    /// </summary>
    public string? MultiSelectItemTypePlural { get; init; }
    
    /// <summary>
    /// Whether to show item IDs next to names.
    /// </summary>
    public bool ShowItemIds { get; init; } = false;
    
    /// <summary>
    /// Format string for items when ShowItemIds is true.
    /// Use {0} for name and {1} for ID.
    /// </summary>
    public string ItemDisplayFormat { get; init; } = "{0}  ({1})";
    
    /// <summary>
    /// Maximum popup width in pixels. 0 = use widget width, -1 = no limit (auto-size to content).
    /// </summary>
    public float PopupMaxWidth { get; init; } = 0;
}

/// <summary>
/// State holder for combo widget to enable external persistence.
/// </summary>
/// <typeparam name="TId">The type of item identifiers.</typeparam>
public class ComboState<TId> where TId : notnull
{
    /// <summary>
    /// Current sort order.
    /// </summary>
    public ComboSortOrder SortOrder { get; set; } = ComboSortOrder.Alphabetical;
    
    /// <summary>
    /// Current group display mode.
    /// </summary>
    public MTComboGroupDisplayMode GroupMode { get; set; } = MTComboGroupDisplayMode.Flat;
    
    /// <summary>
    /// Set of favorite item IDs.
    /// </summary>
    public HashSet<TId> Favorites { get; } = [];
    
    /// <summary>
    /// Set of selected item IDs (for multi-select).
    /// </summary>
    public HashSet<TId> SelectedIds { get; } = [];
    
    /// <summary>
    /// Whether "All" is selected (multi-select mode).
    /// </summary>
    public bool AllSelected { get; set; } = false;
    
    /// <summary>
    /// Currently selected single item ID.
    /// </summary>
    public TId? SelectedId { get; set; }
    
    /// <summary>
    /// Current filter/search text.
    /// </summary>
    public string FilterText { get; set; } = string.Empty;
}

/// <summary>
/// Delegate for rendering custom item icons.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <param name="item">The item to render an icon for.</param>
/// <param name="size">The icon size.</param>
public delegate void MTIconRenderer<in TItem>(TItem item, Vector2 size);

/// <summary>
/// Delegate for getting additional display text (e.g., world name, category).
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <param name="item">The item.</param>
/// <returns>Additional text to display, or null.</returns>
public delegate string? MTSecondaryTextProvider<in TItem>(TItem item);

/// <summary>
/// Delegate for custom item filtering.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <param name="item">The item to check.</param>
/// <param name="filterText">The current filter text (lowercase).</param>
/// <returns>True if the item matches the filter.</returns>
public delegate bool MTItemFilter<in TItem>(TItem item, string filterText);

/// <summary>
/// Delegate for custom item sorting.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <typeparam name="TId">The item ID type.</typeparam>
/// <param name="a">First item.</param>
/// <param name="b">Second item.</param>
/// <param name="favorites">Set of favorite IDs.</param>
/// <returns>Comparison result.</returns>
public delegate int MTItemComparer<in TItem, TId>(TItem a, TItem b, IReadOnlySet<TId> favorites) where TId : notnull;
