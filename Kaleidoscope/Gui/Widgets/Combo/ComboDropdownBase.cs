using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// Shared scaffolding for the concrete combo dropdown wrappers. Owns the underlying
/// <see cref="ComboWidget{TItem,TId}"/>, favorites wiring, the rebuild flag, and disposal.
/// Subclasses assign <see cref="Widget"/>/<see cref="State"/> in their constructor, call
/// <see cref="Initialize"/> once configured, and supply item building plus the
/// service-specific selection/favorite hooks.
/// </summary>
/// <typeparam name="TItem">The item type (must implement <see cref="IComboItem{TId}"/>).</typeparam>
/// <typeparam name="TId">The item ID type.</typeparam>
public abstract class ComboDropdownBase<TItem, TId> : IDisposable
    where TItem : IComboItem<TId>
    where TId : notnull
{
    protected readonly FavoritesService FavoritesService;
    protected ComboWidget<TItem, TId> Widget = null!;
    protected ComboState<TId> State = null!;

    private bool _disposed;
    protected bool NeedsRebuild = true;

    /// <summary>
    /// The label for this combo (used for ImGui ID).
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Event fired when multi-selection changes.
    /// </summary>
    public event Action<IReadOnlySet<TId>>? MultiSelectionChanged;

    protected ComboDropdownBase(FavoritesService favoritesService, string label)
    {
        FavoritesService = favoritesService;
        Label = label;
    }

    /// <summary>
    /// Wires widget events and favorites syncing. Call at the end of the subclass constructor,
    /// after <see cref="Widget"/> and <see cref="State"/> have been assigned and configured.
    /// </summary>
    protected void Initialize()
    {
        Widget.SelectionChanged += OnWidgetSelectionChanged;
        Widget.MultiSelectionChanged += OnWidgetMultiSelectionChanged;
        Widget.FavoriteToggled += OnWidgetFavoriteToggled;

        SyncFavoritesFromService();
        FavoritesService.OnFavoritesChanged += OnFavoritesChanged;
    }

    /// <summary>Returns the current favorite IDs from the favorites service.</summary>
    protected abstract IEnumerable<TId> GetFavoriteIds();

    /// <summary>Builds the current item list for the widget.</summary>
    protected abstract List<TItem> BuildItems();

    /// <summary>Handles a single-select change from the widget.</summary>
    protected abstract void OnWidgetSelectionChanged(TId id);

    /// <summary>Persists a favorite toggle back to the favorites service.</summary>
    protected abstract void OnWidgetFavoriteToggled(TId id, bool isFavorite);

    private void OnWidgetMultiSelectionChanged(IReadOnlySet<TId> ids)
        => MultiSelectionChanged?.Invoke(ids);

    private void SyncFavoritesFromService() => Widget.SyncFavorites(GetFavoriteIds());

    private void OnFavoritesChanged()
    {
        SyncFavoritesFromService();
        NeedsRebuild = true;
    }

    /// <summary>
    /// Rebuilds the widget's item list when a rebuild is pending.
    /// Subclasses may override to add pre-checks before calling the base implementation.
    /// </summary>
    protected virtual void EnsureItemsLoaded()
    {
        if (!NeedsRebuild)
            return;

        Widget.SetItems(BuildItems());
        NeedsRebuild = false;
    }

    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    public bool Draw(float width)
    {
        EnsureItemsLoaded();
        return Widget.Draw(width);
    }

    /// <summary>
    /// Hook for subclasses to release extra subscriptions during disposal.
    /// </summary>
    protected virtual void DisposeCore() { }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Widget.SelectionChanged -= OnWidgetSelectionChanged;
        Widget.MultiSelectionChanged -= OnWidgetMultiSelectionChanged;
        Widget.FavoriteToggled -= OnWidgetFavoriteToggled;

        FavoritesService.OnFavoritesChanged -= OnFavoritesChanged;

        DisposeCore();
    }
}
