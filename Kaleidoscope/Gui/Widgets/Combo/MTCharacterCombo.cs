using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using MTGui.Combo;
using OtterGui.Raii;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// A character combo widget using MTComboWidget from MTGui.
/// Provides the same public interface as the legacy CharacterCombo.
/// </summary>
public sealed class MTCharacterCombo : IDisposable
{
    private readonly FavoritesService _favoritesService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService? _configService;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    
    private readonly MTComboWidget<MTCharacterItem, ulong> _widget;
    private readonly MTComboState<ulong> _state;
    
    private bool _disposed;
    private bool _needsRebuild = true;
    private CharacterNameFormat _cachedNameFormat;
    
    // Special "All" entry ID
    private const ulong AllCharactersId = 0;
    
    /// <summary>
    /// The label for this combo (used for ImGui ID).
    /// </summary>
    public string Label { get; }
    
    /// <summary>
    /// Whether multi-select mode is enabled.
    /// </summary>
    public bool MultiSelectEnabled
    {
        get => _widget.Config.MultiSelect;
        set
        {
            // Can't change after construction in MTComboWidget, but we can ignore single/multi draw
        }
    }
    
    /// <summary>
    /// Gets the currently selected character (single-select mode).
    /// </summary>
    public ComboCharacter? SelectedCharacter => _widget.SelectedItem != null 
        ? ToComboCharacter(_widget.SelectedItem) 
        : null;
    
    /// <summary>
    /// Gets the currently selected character ID, or 0 for "All" (single-select mode).
    /// </summary>
    public ulong SelectedCharacterId => _widget.SelectedItem?.Id ?? AllCharactersId;
    
    /// <summary>
    /// Whether "All Characters" is selected.
    /// </summary>
    public bool IsAllSelected => _widget.IsAllSelected;
    
    /// <summary>
    /// Gets the set of selected character IDs (multi-select mode).
    /// </summary>
    public IReadOnlySet<ulong> SelectedCharacterIds => _state.SelectedIds;
    
    /// <summary>
    /// Gets the list of selected character IDs for data loading.
    /// Returns null if "All" is selected.
    /// </summary>
    public IReadOnlyList<ulong>? GetSelectedIdsForLoading() => _widget.GetSelectedIdsForLoading()?.ToList();
    
    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public event Action<ComboCharacter?, ComboCharacter?>? SelectionChanged;
    
    /// <summary>
    /// Event fired when multi-selection changes.
    /// </summary>
    public event Action<IReadOnlySet<ulong>>? MultiSelectionChanged;
    
    public MTCharacterCombo(
        CurrencyTrackerService currencyTrackerService,
        FavoritesService favoritesService,
        ConfigurationService? configService,
        string label,
        bool multiSelect = false,
        AutoRetainerIpcService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null)
    {
        _currencyTrackerService = currencyTrackerService;
        _favoritesService = favoritesService;
        _configService = configService;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        Label = label;
        
        // Create state
        _state = new MTComboState<ulong>
        {
            SortOrder = MTComboSortOrder.Alphabetical,
            GroupMode = MTComboGroupDisplayMode.Flat,
            AllSelected = true
        };
        
        // Create config
        var config = new MTComboConfig
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
        
        // Create widget
        _widget = new MTComboWidget<MTCharacterItem, ulong>(config, _state);
        
        // Configure grouping (Region → DC → World)
        _widget.WithGrouping(
            item => item.Region,
            item => item.DataCenter,
            item => item.World);
        
        // Configure secondary text (shows @ World)
        _widget.WithSecondaryText(item => 
            !string.IsNullOrEmpty(item.World) ? $"@ {item.World}" : null);
        
        // Configure filter to search name, world, DC, region
        _widget.WithFilter((item, filter) =>
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
        
        // Subscribe to events
        _widget.SelectionChanged += OnWidgetSelectionChanged;
        _widget.MultiSelectionChanged += OnWidgetMultiSelectionChanged;
        _widget.FavoriteToggled += OnWidgetFavoriteToggled;
        
        // Sync favorites from service (must be after widget is created)
        SyncFavoritesFromService();
        
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
        
        if (_priceTrackingService != null)
        {
            _priceTrackingService.OnWorldDataLoaded += OnWorldDataLoaded;
        }
    }
    
    private void SyncFavoritesFromService()
    {
        _widget.SyncFavorites(_favoritesService.FavoriteCharacters);
    }
    
    private void OnFavoritesChanged()
    {
        SyncFavoritesFromService();
        _needsRebuild = true;
    }
    
    private void OnWorldDataLoaded()
    {
        _needsRebuild = true;
    }
    
    private void OnWidgetSelectionChanged(ulong id)
    {
        // Fire legacy event
        // Note: we don't have the old selection easily available
        SelectionChanged?.Invoke(null, CreateCharacterFromId(id));
    }
    
    private void OnWidgetMultiSelectionChanged(IReadOnlySet<ulong> ids)
    {
        MultiSelectionChanged?.Invoke(ids);
    }
    
    private void OnWidgetFavoriteToggled(ulong id, bool isFavorite)
    {
        // Sync back to favorites service
        if (isFavorite)
            _favoritesService.AddCharacter(id);
        else
            _favoritesService.RemoveCharacter(id);
    }
    
    private ComboCharacter? CreateCharacterFromId(ulong id)
    {
        if (id == AllCharactersId)
            return new ComboCharacter(0, "All Characters", null);
        
        // Find in items
        var item = _widget.SelectedItem;
        if (item != null && item.Id == id)
            return ToComboCharacter(item);
        
        return null;
    }
    
    private static ComboCharacter ToComboCharacter(MTCharacterItem item) =>
        new(item.Id, item.Name, item.World, item.DataCenter, item.Region);
    
    private void EnsureCharactersLoaded()
    {
        var currentFormat = _configService?.Config.CharacterNameFormat ?? CharacterNameFormat.FullName;
        if (_cachedNameFormat != currentFormat)
        {
            _needsRebuild = true;
            _cachedNameFormat = currentFormat;
        }
        
        if (!_needsRebuild)
            return;
        
        var items = BuildCharacterList();
        _widget.SetItems(items);
        _needsRebuild = false;
    }
    
    private List<MTCharacterItem> BuildCharacterList()
    {
        var items = new List<MTCharacterItem>();
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
                
                var displayName = TimeSeriesCacheService.FormatName(baseName, nameFormat) ?? baseName;
                
                string? dcName = null;
                string? regionName = null;
                if (!string.IsNullOrEmpty(world) && worldData != null)
                {
                    dcName = worldData.GetDataCenterForWorld(world)?.Name;
                    regionName = worldData.GetRegionForWorld(world);
                }
                
                items.Add(new MTCharacterItem
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
            LogService.Debug(LogCategory.UI, $"[MTCharacterCombo] Error building character list: {ex.Message}");
        }
        
        return items;
    }
    
    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    public bool Draw(float width)
    {
        EnsureCharactersLoaded();
        return _widget.Draw(width);
    }
    
    /// <summary>
    /// Draws an inline multi-select widget.
    /// </summary>
    public bool DrawInline(float width, float height)
    {
        EnsureCharactersLoaded();
        return _widget.DrawInline(width, height);
    }
    
    /// <summary>
    /// Sets the selection to a single character ID.
    /// </summary>
    public void SetSelection(ulong characterId)
    {
        if (characterId == AllCharactersId)
        {
            _state.AllSelected = true;
            _state.SelectedIds.Clear();
            _state.SelectedId = default;
        }
        else
        {
            _widget.SetSelection(characterId);
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
            _state.AllSelected = true;
            _state.SelectedIds.Clear();
        }
        else
        {
            _widget.SetMultiSelection(ids);
        }
    }
    
    /// <summary>
    /// Selects "All Characters".
    /// </summary>
    public void SelectAll()
    {
        _state.AllSelected = true;
        _state.SelectedIds.Clear();
        _state.SelectedId = default;
    }
    
    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _widget.ClearSelection();
    }
    
    /// <summary>
    /// Refreshes the character list from the database.
    /// </summary>
    public void RefreshCharacters()
    {
        _needsRebuild = true;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _widget.SelectionChanged -= OnWidgetSelectionChanged;
        _widget.MultiSelectionChanged -= OnWidgetMultiSelectionChanged;
        _widget.FavoriteToggled -= OnWidgetFavoriteToggled;
        
        _favoritesService.OnFavoritesChanged -= OnFavoritesChanged;
        
        if (_priceTrackingService != null)
        {
            _priceTrackingService.OnWorldDataLoaded -= OnWorldDataLoaded;
        }
    }
}
