using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using OtterGui.Raii;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// A character combo widget using ComboWidget.
/// Provides the same public interface as the legacy CharacterCombo.
/// </summary>
public sealed class CharacterCombo : ComboDropdownBase<CharacterItem, ulong>
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService? _configService;
    private readonly AutoRetainerService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;

    private CharacterNameFormat _cachedNameFormat;

    // Special "All" entry ID
    private const ulong AllCharactersId = 0;

    /// <summary>
    /// Whether multi-select mode is enabled.
    /// </summary>
    public bool MultiSelectEnabled
    {
        get => Widget.Config.MultiSelect;
        set { } // Multi-select is fixed at construction; setter retained for API compatibility.
    }

    /// <summary>
    /// Gets the currently selected character (single-select mode).
    /// </summary>
    public ComboCharacter? SelectedCharacter => Widget.SelectedItem != null
        ? ToComboCharacter(Widget.SelectedItem)
        : null;

    /// <summary>
    /// Gets the currently selected character ID, or 0 for "All" (single-select mode).
    /// </summary>
    public ulong SelectedCharacterId => Widget.SelectedItem?.Id ?? AllCharactersId;

    /// <summary>
    /// Whether "All Characters" is selected.
    /// </summary>
    public bool IsAllSelected => Widget.IsAllSelected;

    /// <summary>
    /// Gets the set of selected character IDs (multi-select mode).
    /// </summary>
    public IReadOnlySet<ulong> SelectedCharacterIds => State.SelectedIds;

    /// <summary>
    /// Gets the list of selected character IDs for data loading.
    /// Returns null if "All" is selected.
    /// </summary>
    public IReadOnlyList<ulong>? GetSelectedIdsForLoading() => Widget.GetSelectedIdsForLoading()?.ToList();

    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public event Action<ComboCharacter?, ComboCharacter?>? SelectionChanged;

    public CharacterCombo(
        CurrencyTrackerService currencyTrackerService,
        FavoritesService favoritesService,
        ConfigurationService? configService,
        string label,
        bool multiSelect = false,
        AutoRetainerService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null)
        : base(favoritesService, label)
    {
        _currencyTrackerService = currencyTrackerService;
        _configService = configService;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;

        State = new ComboState<ulong>
        {
            SortOrder = ComboSortOrder.Alphabetical,
            GroupMode = MTComboGroupDisplayMode.Flat,
            AllSelected = true
        };

        var config = new ComboConfig
        {
            ComboId = label,
            Placeholder = "Select character...",
            SearchPlaceholder = "Search characters...",
            MultiSelect = multiSelect,
            ShowSearch = true,
            ShowFavorites = true,
            ShowIcons = false, // Characters don't have icons in this implementation
            ShowSortToggle = true,
            ShowGroupingToggle = true,
            ShowBulkActions = true,
            ShowAllBulkAction = true,
            ShowNoneBulkAction = true,
            ShowFavoritesBulkAction = true,
            ShowInvertBulkAction = true,
            ShowAllOption = true,
            AllOptionLabel = "All Characters",
            DefaultGroupMode = MTComboGroupDisplayMode.Flat
        };

        Widget = new ComboWidget<CharacterItem, ulong>(config, State);

        // Configure grouping (Region → DC → World)
        Widget.WithGrouping(
            item => item.Region,
            item => item.DataCenter,
            item => item.World);

        // Configure secondary text (shows @ World)
        Widget.WithSecondaryText(item =>
            !string.IsNullOrEmpty(item.World) ? $"@ {item.World}" : null);

        // Configure filter to search name, world, DC, region
        Widget.WithFilter((item, filter) =>
        {
            var nameLower = item.Name.ToLowerInvariant();
            var worldLower = item.World?.ToLowerInvariant();
            var dcLower = item.DataCenter?.ToLowerInvariant();
            var regionLower = item.Region?.ToLowerInvariant();

            return nameLower.Contains(filter) ||
                   (worldLower?.Contains(filter) ?? false) ||
                   (dcLower?.Contains(filter) ?? false) ||
                   (regionLower?.Contains(filter) ?? false);
        });

        Initialize();

        if (_priceTrackingService != null)
        {
            _priceTrackingService.OnWorldDataLoaded += OnWorldDataLoaded;
        }
    }

    protected override IEnumerable<ulong> GetFavoriteIds() => FavoritesService.FavoriteCharacters;

    protected override List<CharacterItem> BuildItems() => BuildCharacterList();

    protected override void OnWidgetSelectionChanged(ulong id)
    {
        // Fire legacy event. The previous selection isn't tracked here, so the old value is null.
        SelectionChanged?.Invoke(null, CreateCharacterFromId(id));
    }

    protected override void OnWidgetFavoriteToggled(ulong id, bool isFavorite)
    {
        // Sync back to favorites service
        if (isFavorite)
            FavoritesService.AddCharacter(id);
        else
            FavoritesService.RemoveCharacter(id);
    }

    protected override void DisposeCore()
    {
        if (_priceTrackingService != null)
        {
            _priceTrackingService.OnWorldDataLoaded -= OnWorldDataLoaded;
        }
    }

    private void OnWorldDataLoaded()
    {
        NeedsRebuild = true;
    }

    private ComboCharacter? CreateCharacterFromId(ulong id)
    {
        if (id == AllCharactersId)
            return new ComboCharacter(0, "All Characters", null);

        // Find in items
        var item = Widget.SelectedItem;
        if (item != null && item.Id == id)
            return ToComboCharacter(item);

        return null;
    }

    private static ComboCharacter ToComboCharacter(CharacterItem item) =>
        new(item.Id, item.Name, item.World, item.DataCenter, item.Region);

    protected override void EnsureItemsLoaded()
    {
        var currentFormat = _configService?.Config.CharacterNameFormat ?? CharacterNameFormat.FullName;
        if (_cachedNameFormat != currentFormat)
        {
            NeedsRebuild = true;
            _cachedNameFormat = currentFormat;
        }

        base.EnsureItemsLoaded();
    }

    private List<CharacterItem> BuildCharacterList()
    {
        var items = new List<CharacterItem>();
        var cacheService = _currencyTrackerService.CacheService;
        var nameFormat = _configService?.Config.CharacterNameFormat ?? CharacterNameFormat.FullName;
        var worldData = _priceTrackingService?.WorldData;
        var characterWorlds = AutoRetainerIpcHelper.GetCharacterWorlds(_autoRetainerService);

        try
        {
            // Get all characters from CharacterDataCache (no DB access)
            var dbCharacters = _currencyTrackerService.CharacterDataCache.GetAllCharacterNames()
                .Select(c => (c.characterId, c.name))
                .DistinctBy(c => c.characterId)
                .ToList();

            foreach (var (charId, name) in dbCharacters)
            {
                if (charId == 0) continue;

                var fullNameWithWorld = cacheService.GetFormattedCharacterName(charId) ?? name ?? $"Character {charId}";

                string? world = null;
                string baseName = fullNameWithWorld;
                var atIndex = fullNameWithWorld.IndexOf('@');
                if (atIndex > 0)
                {
                    world = fullNameWithWorld[(atIndex + 1)..].Trim();
                    baseName = fullNameWithWorld[..atIndex].Trim();
                }

                if (string.IsNullOrEmpty(world) && characterWorlds.TryGetValue(charId, out var arWorld))
                {
                    world = arWorld;
                }

                var displayName = Kaleidoscope.Libs.CharacterNameFormatter.FormatName(baseName, nameFormat) ?? baseName;

                string? dcName = null;
                string? regionName = null;
                if (!string.IsNullOrEmpty(world) && worldData != null)
                {
                    dcName = worldData.GetDataCenterForWorld(world)?.Name;
                    regionName = worldData.GetRegionForWorld(world);
                }

                items.Add(new CharacterItem
                {
                    Id = charId,
                    Name = displayName,
                    World = world,
                    DataCenter = dcName,
                    Region = regionName
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"[CharacterCombo] Error building character list: {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Draws an inline multi-select widget.
    /// </summary>
    public bool DrawInline(float width, float height)
    {
        EnsureItemsLoaded();
        return Widget.DrawInline(width, height);
    }

    /// <summary>
    /// Sets the selection to a single character ID.
    /// </summary>
    public void SetSelection(ulong characterId)
    {
        if (characterId == AllCharactersId)
        {
            State.AllSelected = true;
            State.SelectedIds.Clear();
            State.SelectedId = default;
        }
        else
        {
            Widget.SetSelection(characterId);
        }
    }

    /// <summary>
    /// Sets the selection to multiple character IDs.
    /// </summary>
    public void SetSelection(IEnumerable<ulong> characterIds)
    {
        var ids = characterIds.ToList();
        if (ids.Contains(AllCharactersId) || ids.Count == 0)
        {
            State.AllSelected = true;
            State.SelectedIds.Clear();
        }
        else
        {
            Widget.SetMultiSelection(ids);
        }
    }

    /// <summary>
    /// Selects "All Characters".
    /// </summary>
    public void SelectAll()
    {
        State.AllSelected = true;
        State.SelectedIds.Clear();
        State.SelectedId = default;
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        Widget.ClearSelection();
    }

    /// <summary>
    /// Refreshes the character list from the database.
    /// </summary>
    public void RefreshCharacters()
    {
        NeedsRebuild = true;
    }
}
