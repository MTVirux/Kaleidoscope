using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using OtterGui.Classes;
using OtterGui.Extensions;
using OtterGui.Log;
using OtterGui.Raii;
using OtterGui.Widgets;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Represents a character for display in the combo.
/// </summary>
public readonly record struct ComboCharacter(ulong Id, string Name, string? World, string? DataCenter = null, string? Region = null);

/// <summary>
/// A filtered combo box for selecting characters with favorites support.
/// Includes an "All Characters" option and supports both single-select and multi-select modes.
/// </summary>
public sealed class CharacterCombo : FilterComboCache<ComboCharacter>, IDisposable
{
    private readonly FavoritesService _favoritesService;
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService? _configService;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    private bool _disposed;
    
    // Current state (single-select mode)
    private ulong _currentCharacterId;
    private float _innerWidth;
    
    // Multi-select state
    private readonly HashSet<ulong> _selectedCharacterIds = new();
    private bool _allSelected = true;
    private string _filterText = string.Empty;
    
    // Grouping state for multi-select
    private bool _groupByRegion = true;
    
    // Cached character list for multi-select (loaded lazily)
    private List<ComboCharacter>? _cachedCharacters;
    private bool _needsRebuild = true;
    private CharacterNameFormat _cachedNameFormat;
    
    // Special "All" entry
    private static readonly ComboCharacter AllCharacters = new(0, "All Characters", null);
    
    // Icon sizes
    private static readonly Vector2 StarSize = new(16, 16);
    
    // Colors
    private const uint FavoriteStarOn = 0xFF00CFFF;      // Yellow-gold
    private const uint FavoriteStarOff = 0x40FFFFFF;     // Dim white
    private const uint FavoriteStarHovered = 0xFF40DFFF; // Bright gold
    private const uint WorldColor = 0xFF808080;          // Dim gray for world
    private const uint SelectedBgColor = 0x40008000;     // Dim green background

    /// <summary>
    /// The label for this combo (used for ImGui ID).
    /// </summary>
    public string Label { get; }
    
    /// <summary>
    /// Whether multi-select mode is enabled. When true, allows selecting multiple characters.
    /// Default is false for backward compatibility.
    /// </summary>
    public bool MultiSelectEnabled { get; set; }

    /// <summary>
    /// Gets the currently selected character (single-select mode).
    /// </summary>
    public ComboCharacter? SelectedCharacter => CurrentSelection;

    /// <summary>
    /// Gets the currently selected character ID, or 0 for "All" (single-select mode).
    /// </summary>
    public ulong SelectedCharacterId => CurrentSelectionIdx >= 0 && CurrentSelection != null ? CurrentSelection.Id : 0;

    /// <summary>
    /// Whether "All Characters" is selected.
    /// </summary>
    public bool IsAllSelected => MultiSelectEnabled ? _allSelected : SelectedCharacterId == 0;
    
    /// <summary>
    /// Gets the set of selected character IDs (multi-select mode).
    /// Empty or null means "All Characters" is selected.
    /// </summary>
    public IReadOnlySet<ulong> SelectedCharacterIds => _selectedCharacterIds;
    
    /// <summary>
    /// Gets the list of selected character IDs for data loading.
    /// Returns null if "All" is selected (meaning load all characters).
    /// </summary>
    public IReadOnlyList<ulong>? GetSelectedIdsForLoading()
    {
        if (!MultiSelectEnabled)
            return SelectedCharacterId == 0 ? null : new[] { SelectedCharacterId };
        
        if (_allSelected || _selectedCharacterIds.Count == 0)
            return null;
        return _selectedCharacterIds.ToList();
    }

    /// <summary>
    /// Event fired when selection changes (single-select mode).
    /// </summary>
    public new event Action<ComboCharacter?, ComboCharacter?>? SelectionChanged;
    
    /// <summary>
    /// Event fired when multi-selection changes.
    /// The parameter is the set of selected character IDs (empty means "All").
    /// </summary>
    public event Action<IReadOnlySet<ulong>>? MultiSelectionChanged;

    public CharacterCombo(
        SamplerService samplerService,
        FavoritesService favoritesService,
        ConfigurationService? configService,
        string label,
        AutoRetainerIpcService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null)
        : base(
            () => BuildCharacterList(samplerService, favoritesService, configService, autoRetainerService, priceTrackingService),
            MouseWheelType.Control,
            new Logger())
    {
        _samplerService = samplerService;
        _favoritesService = favoritesService;
        _configService = configService;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        Label = label;
        SearchByParts = true;
        
        // Subscribe to favorites changes to rebuild list
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
        
        // Subscribe to world data loading to refresh DC/Region info
        if (_priceTrackingService != null)
        {
            _priceTrackingService.OnWorldDataLoaded += OnWorldDataLoaded;
        }
    }

    private void OnFavoritesChanged()
    {
        // Clear cached list to force regeneration on next access
        _needsRebuild = true;
        Cleanup();
        ResetFilter();
    }
    
    private void OnWorldDataLoaded()
    {
        // Rebuild list now that we have DC/Region data
        LogService.Debug("[CharacterCombo] World data loaded, rebuilding character list");
        _needsRebuild = true;
        Cleanup();
        ResetFilter();
    }

    private static IReadOnlyList<ComboCharacter> BuildCharacterList(
        SamplerService samplerService,
        FavoritesService favoritesService,
        ConfigurationService? configService,
        AutoRetainerIpcService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null)
    {
        var characters = new List<ComboCharacter>();
        var favorites = favoritesService.FavoriteCharacters;
        var cacheService = samplerService.CacheService;
        var nameFormat = configService?.Config.CharacterNameFormat ?? CharacterNameFormat.FullName;
        
        // Get world data for DC/Region lookups
        var worldData = priceTrackingService?.WorldData;
        
        // Get character world info from AutoRetainer (maps CID to world name)
        var characterWorlds = AutoRetainerIpcHelper.GetCharacterWorlds(autoRetainerService);

        try
        {
            // Add "All Characters" option first
            characters.Add(AllCharacters);

            // Get all characters from database
            var dbCharacters = samplerService.DbService.GetAllCharacterNames()
                .Select(c => (c.characterId, c.name))
                .DistinctBy(c => c.characterId)
                .ToList();

            foreach (var (charId, name) in dbCharacters)
            {
                if (charId == 0) continue; // Skip invalid

                // Get the full name from cache service (includes world in "Name @ World" format)
                var fullNameWithWorld = cacheService.GetFormattedCharacterName(charId) ?? name ?? $"Character {charId}";
                
                // Extract world from name (format: "Name @ World")
                string? world = null;
                string baseName = fullNameWithWorld;
                var atIndex = fullNameWithWorld.IndexOf('@');
                if (atIndex > 0)
                {
                    world = fullNameWithWorld[(atIndex + 1)..].Trim();
                    baseName = fullNameWithWorld[..atIndex].Trim();
                }
                
                // Try to get world from AutoRetainer if not in name
                if (string.IsNullOrEmpty(world) && characterWorlds.TryGetValue(charId, out var arWorld))
                {
                    world = arWorld;
                }
                
                // Apply name format from config
                var displayName = TimeSeriesCacheService.FormatName(baseName, nameFormat) ?? baseName;
                
                // Get DC and Region from world data
                string? dcName = null;
                string? regionName = null;
                if (!string.IsNullOrEmpty(world) && worldData != null)
                {
                    dcName = worldData.GetDataCenterForWorld(world)?.Name;
                    regionName = worldData.GetRegionForWorld(world);
                }

                characters.Add(new ComboCharacter(charId, displayName, world, dcName, regionName));
            }

            // Sort: "All" first, then favorites, then alphabetically
            var sorted = new List<ComboCharacter> { AllCharacters };
            var rest = characters.Where(c => c.Id != 0).ToList();
            
            rest.Sort((a, b) =>
            {
                var aFav = favorites.Contains(a.Id);
                var bFav = favorites.Contains(b.Id);
                if (aFav != bFav)
                    return bFav.CompareTo(aFav); // Favorites first
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            
            sorted.AddRange(rest);
            return sorted;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CharacterCombo] Error building character list: {ex.Message}");
        }

        return characters;
    }

    protected override string ToString(ComboCharacter obj)
        => obj.World != null ? $"{obj.Name} @ {obj.World}" : obj.Name;

    protected override float GetFilterWidth()
        => _innerWidth - 2 * ImGui.GetStyle().FramePadding.X;

    protected override int UpdateCurrentSelected(int currentSelected)
    {
        if (CurrentSelection != null && CurrentSelection.Id == _currentCharacterId)
            return currentSelected;

        CurrentSelectionIdx = Items.IndexOf(i => i.Id == _currentCharacterId);
        if (CurrentSelectionIdx >= 0)
            CurrentSelection = Items[CurrentSelectionIdx];
        else
            CurrentSelection = default;
            
        return base.UpdateCurrentSelected(CurrentSelectionIdx);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var character = Items[globalIdx];
        
        // Don't show favorite star for "All Characters"
        if (character.Id != 0)
        {
            var isFavorite = _favoritesService.ContainsCharacter(character.Id);
            
            // Draw favorite star
            if (DrawFavoriteStar(character.Id, isFavorite))
            {
                // Star was clicked - toggle favorite
                if (CurrentSelectionIdx == globalIdx)
                {
                    CurrentSelectionIdx = -1;
                    _currentCharacterId = character.Id;
                    CurrentSelection = default;
                }
            }
            
            ImGui.SameLine();
        }
        else
        {
            // Placeholder spacing for "All"
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Dummy(StarSize);
            }
            ImGui.SameLine();
        }
        
        // Draw selectable text
        var ret = ImGui.Selectable(character.Name, selected);
        
        // Draw world on right side (dimmed) if available
        if (character.World != null)
        {
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, WorldColor))
            {
                var worldText = $"@ {character.World}";
                var textWidth = ImGui.CalcTextSize(worldText).X;
                var availWidth = ImGui.GetContentRegionAvail().X;
                if (availWidth > textWidth)
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availWidth - textWidth);
                    ImGui.TextUnformatted(worldText);
                }
            }
        }
        
        // If Shift is held, keep dropdown open (return false to prevent close)
        if (ret && ImGui.GetIO().KeyShift)
        {
            // Update selection but don't close
            _currentCharacterId = character.Id;
            CurrentSelectionIdx = globalIdx;
            CurrentSelection = character;
            return false;
        }
        
        return ret;
    }

    protected override void DrawList(float width, float itemHeight)
    {
        base.DrawList(width, itemHeight);
        if (NewSelection != null && Items.Count > NewSelection.Value)
        {
            var newChar = Items[NewSelection.Value];
            if (!Equals(CurrentSelection, newChar))
            {
                SelectionChanged?.Invoke(CurrentSelection, newChar);
            }
            CurrentSelection = newChar;
        }
    }

    private bool DrawFavoriteStar(ulong characterId, bool isFavorite)
    {
        var hovering = ImGui.IsMouseHoveringRect(
            ImGui.GetCursorScreenPos(),
            ImGui.GetCursorScreenPos() + StarSize);

        var color = hovering ? FavoriteStarHovered : isFavorite ? FavoriteStarOn : FavoriteStarOff;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.TextUnformatted(FontAwesomeIcon.Star.ToIconString());
        }

        if (ImGui.IsItemClicked())
        {
            _favoritesService.ToggleCharacter(characterId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    /// <param name="previewName">The preview text to show when closed.</param>
    /// <param name="previewCharacterId">The current character ID for selection tracking.</param>
    /// <param name="width">The combo width.</param>
    /// <param name="innerWidth">The popup inner width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(string previewName, ulong previewCharacterId, float width, float innerWidth)
    {
        if (MultiSelectEnabled)
            return DrawMultiSelect(width);
            
        _innerWidth = innerWidth;
        _currentCharacterId = previewCharacterId;
        return Draw($"##{Label}", previewName, string.Empty, width, ImGui.GetTextLineHeightWithSpacing() + 4);
    }

    /// <summary>
    /// Draws the combo with automatic preview text from current selection.
    /// </summary>
    /// <param name="width">The combo width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(float width)
    {
        if (MultiSelectEnabled)
            return DrawMultiSelect(width);
            
        var preview = CurrentSelectionIdx >= 0 && CurrentSelection != null ? ToString(CurrentSelection) : "Select character...";
        var charId = CurrentSelectionIdx >= 0 && CurrentSelection != null ? CurrentSelection.Id : 0ul;
        return Draw(preview, charId, width, width);
    }
    
    /// <summary>
    /// Draws the multi-select inline list version of this widget (AutoRetainer style).
    /// Shows a collapsible combo with checkboxes for each character.
    /// </summary>
    private bool DrawMultiSelect(float width)
    {
        var changed = false;
        
        // Check if we need to rebuild the character list
        EnsureCharacterListLoaded();
        
        // Build preview text
        var preview = BuildMultiSelectPreview();
        
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo($"##{Label}", preview, ImGuiComboFlags.HeightLarge))
        {
            changed = DrawMultiSelectComboContent();
            ImGui.EndCombo();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Draws an inline multi-select widget that takes up a specified height.
    /// This draws directly in the UI without a combo/popup wrapper.
    /// </summary>
    /// <param name="width">Width of the widget.</param>
    /// <param name="height">Height of the widget.</param>
    /// <returns>True if selection changed.</returns>
    public bool DrawInline(float width, float height)
    {
        if (!MultiSelectEnabled)
        {
            // Fallback to single-select combo
            return Draw(width);
        }
        
        var changed = false;
        
        // Check if we need to rebuild the character list
        EnsureCharacterListLoaded();
        
        if (ImGui.BeginChild($"##CharComboInline_{Label}", new Vector2(width, height), true))
        {
            changed = DrawMultiSelectComboContent();
        }
        ImGui.EndChild();
        
        return changed;
    }
    
    private bool DrawMultiSelectComboContent()
    {
        var changed = false;
        
        // Filter input
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Search characters...", ref _filterText, 100);
        
        ImGui.Separator();
        
        // Quick action buttons
        if (ImGui.Button("All"))
        {
            _allSelected = true;
            _selectedCharacterIds.Clear();
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("None"))
        {
            _allSelected = false;
            _selectedCharacterIds.Clear();
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Favorites"))
        {
            _allSelected = false;
            _selectedCharacterIds.Clear();
            foreach (var id in _favoritesService.FavoriteCharacters)
            {
                _selectedCharacterIds.Add(id);
            }
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Invert"))
        {
            if (_allSelected)
            {
                _allSelected = false;
                // Invert from all = none selected
            }
            else
            {
                var allIds = CachedCharacters.Where(c => c.Id != 0).Select(c => c.Id).ToHashSet();
                var inverted = allIds.Except(_selectedCharacterIds).ToHashSet();
                _selectedCharacterIds.Clear();
                foreach (var id in inverted)
                    _selectedCharacterIds.Add(id);
                
                // If all are now selected, switch to "All" mode
                if (_selectedCharacterIds.Count == allIds.Count)
                {
                    _allSelected = true;
                    _selectedCharacterIds.Clear();
                }
            }
            changed = true;
        }
        
        // Grouping toggle
        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, _groupByRegion ? 0xFF00FF00 : 0xFF888888))
        {
            if (ImGui.SmallButton(FontAwesomeIcon.LayerGroup.ToIconString()))
            {
                _groupByRegion = !_groupByRegion;
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(_groupByRegion ? "Click to show flat list" : "Click to group by Region/DC/World");
        
        ImGui.Separator();
        
        // "All Characters" option (special)
        var allChecked = _allSelected;
        if (ImGui.Checkbox("All Characters", ref allChecked))
        {
            _allSelected = allChecked;
            if (allChecked)
                _selectedCharacterIds.Clear();
            changed = true;
        }
        
        ImGui.Separator();
        
        // Filter
        var filterLower = _filterText.ToLowerInvariant();
        var favorites = _favoritesService.FavoriteCharacters;
        
        // Filter characters
        var filteredCharacters = CachedCharacters.Where(c =>
        {
            if (c.Id == 0) return false; // Skip "All"
            if (string.IsNullOrEmpty(filterLower)) return true;
            
            var matchName = c.Name.ToLowerInvariant().Contains(filterLower);
            var matchWorld = c.World?.ToLowerInvariant().Contains(filterLower) ?? false;
            var matchDc = c.DataCenter?.ToLowerInvariant().Contains(filterLower) ?? false;
            var matchRegion = c.Region?.ToLowerInvariant().Contains(filterLower) ?? false;
            return matchName || matchWorld || matchDc || matchRegion;
        }).ToList();
        
        if (_groupByRegion)
        {
            changed |= DrawGroupedCharacterList(filteredCharacters, favorites);
        }
        else
        {
            // Flat list
            foreach (var character in filteredCharacters)
            {
                changed |= DrawMultiSelectCharacterRow(character, favorites.Contains(character.Id));
            }
        }
        
        if (changed)
        {
            MultiSelectionChanged?.Invoke(_selectedCharacterIds);
        }
        
        return changed;
    }
    
    /// <summary>
    /// Ensures the character list is loaded and up to date.
    /// </summary>
    private void EnsureCharacterListLoaded()
    {
        // Check if config changed
        var currentFormat = _configService?.Config.CharacterNameFormat ?? CharacterNameFormat.FullName;
        if (_cachedNameFormat != currentFormat)
        {
            _needsRebuild = true;
            _cachedNameFormat = currentFormat;
        }
        
        if (!_needsRebuild && _cachedCharacters != null)
            return;
        
        // Rebuild the character list
        _cachedCharacters = BuildCharacterListInternal();
        _needsRebuild = false;
    }
    
    /// <summary>
    /// Gets the cached characters for multi-select mode.
    /// </summary>
    private IReadOnlyList<ComboCharacter> CachedCharacters => _cachedCharacters ?? (IReadOnlyList<ComboCharacter>)Array.Empty<ComboCharacter>();
    
    /// <summary>
    /// Builds the character list using current services and config.
    /// </summary>
    private List<ComboCharacter> BuildCharacterListInternal()
    {
        var characters = new List<ComboCharacter>();
        var favorites = _favoritesService.FavoriteCharacters;
        var cacheService = _samplerService.CacheService;
        var nameFormat = _configService?.Config.CharacterNameFormat ?? CharacterNameFormat.FullName;
        
        // Get world data for DC/Region lookups
        var worldData = _priceTrackingService?.WorldData;
        
        // Get character world info from AutoRetainer (maps CID to world name)
        var characterWorlds = AutoRetainerIpcHelper.GetCharacterWorlds(_autoRetainerService);

        try
        {
            // Add "All Characters" option first
            characters.Add(AllCharacters);

            // Get all characters from database
            var dbCharacters = _samplerService.DbService.GetAllCharacterNames()
                .Select(c => (c.characterId, c.name))
                .DistinctBy(c => c.characterId)
                .ToList();

            foreach (var (charId, name) in dbCharacters)
            {
                if (charId == 0) continue; // Skip invalid

                // Get the full name from cache service (includes world in "Name @ World" format)
                var fullNameWithWorld = cacheService.GetFormattedCharacterName(charId) ?? name ?? $"Character {charId}";
                
                // Extract world from name (format: "Name @ World")
                string? world = null;
                string baseName = fullNameWithWorld;
                var atIndex = fullNameWithWorld.IndexOf('@');
                if (atIndex > 0)
                {
                    world = fullNameWithWorld[(atIndex + 1)..].Trim();
                    baseName = fullNameWithWorld[..atIndex].Trim();
                }
                
                // Try to get world from AutoRetainer if not in name
                if (string.IsNullOrEmpty(world) && characterWorlds.TryGetValue(charId, out var arWorld))
                {
                    world = arWorld;
                }
                
                // Apply name format from config
                var displayName = TimeSeriesCacheService.FormatName(baseName, nameFormat) ?? baseName;
                
                // Get DC and Region from world data
                string? dcName = null;
                string? regionName = null;
                if (!string.IsNullOrEmpty(world) && worldData != null)
                {
                    dcName = worldData.GetDataCenterForWorld(world)?.Name;
                    regionName = worldData.GetRegionForWorld(world);
                }

                characters.Add(new ComboCharacter(charId, displayName, world, dcName, regionName));
            }

            // Sort: "All" first, then favorites, then alphabetically
            var sorted = new List<ComboCharacter> { AllCharacters };
            var rest = characters.Where(c => c.Id != 0).ToList();
            
            rest.Sort((a, b) =>
            {
                var aFav = favorites.Contains(a.Id);
                var bFav = favorites.Contains(b.Id);
                if (aFav != bFav)
                    return bFav.CompareTo(aFav); // Favorites first
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            
            sorted.AddRange(rest);
            return sorted;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CharacterCombo] Error building character list: {ex.Message}");
        }

        return characters;
    }
    
    private string BuildMultiSelectPreview()
    {
        if (_allSelected || _selectedCharacterIds.Count == 0)
            return "All Characters";
        
        if (_selectedCharacterIds.Count == 1)
        {
            var id = _selectedCharacterIds.First();
            var character = CachedCharacters.FirstOrDefault(c => c.Id == id);
            return character.Id != 0 ? ToString(character) : $"Character {id}";
        }
        
        return $"{_selectedCharacterIds.Count} Characters Selected";
    }
    
    private bool DrawGroupedCharacterList(List<ComboCharacter> characters, IReadOnlyCollection<ulong> favorites)
    {
        var changed = false;
        
        // Group by Region → DC → World
        var grouped = characters
            .GroupBy(c => c.Region ?? "Unknown Region")
            .OrderBy(g => g.Key)
            .Select(regionGroup => new
            {
                Region = regionGroup.Key,
                DataCenters = regionGroup
                    .GroupBy(c => c.DataCenter ?? "Unknown DC")
                    .OrderBy(g => g.Key)
                    .Select(dcGroup => new
                    {
                        DataCenter = dcGroup.Key,
                        Worlds = dcGroup
                            .GroupBy(c => c.World ?? "Unknown World")
                            .OrderBy(g => g.Key)
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();
        
        foreach (var region in grouped)
        {
            // Calculate region selection state
            var regionChars = characters.Where(c => (c.Region ?? "Unknown Region") == region.Region).ToList();
            var regionSelectedCount = regionChars.Count(c => _selectedCharacterIds.Contains(c.Id));
            var regionAllSelected = regionSelectedCount == regionChars.Count && regionChars.Count > 0;
            var regionPartialSelected = regionSelectedCount > 0 && !regionAllSelected;
            
            ImGui.PushID(region.Region);
            
            // Draw checkbox before collapsing header
            using (ImRaii.PushColor(ImGuiCol.CheckMark, regionPartialSelected ? 0xFF888888 : 0xFFFFFFFF))
            {
                var regionCheck = regionAllSelected || regionPartialSelected;
                if (ImGui.Checkbox($"##{region.Region}_check", ref regionCheck))
                {
                    changed = true;
                    if (regionCheck)
                    {
                        foreach (var c in regionChars)
                            _selectedCharacterIds.Add(c.Id);
                        _allSelected = false;
                    }
                    else
                    {
                        foreach (var c in regionChars)
                            _selectedCharacterIds.Remove(c.Id);
                        if (_selectedCharacterIds.Count == 0)
                            _allSelected = true;
                    }
                }
            }
            ImGui.SameLine();
            
            // Region collapsing header - collapse when all selected
            var regionFlags = regionAllSelected ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen;
            var regionOpen = ImGui.CollapsingHeader(region.Region, regionFlags);
            
            if (regionOpen)
            {
                foreach (var dc in region.DataCenters)
                {
                    // Calculate DC selection state
                    var dcChars = characters.Where(c => (c.DataCenter ?? "Unknown DC") == dc.DataCenter && (c.Region ?? "Unknown Region") == region.Region).ToList();
                    var dcSelectedCount = dcChars.Count(c => _selectedCharacterIds.Contains(c.Id));
                    var dcAllSelected = dcSelectedCount == dcChars.Count && dcChars.Count > 0;
                    var dcPartialSelected = dcSelectedCount > 0 && !dcAllSelected;
                    
                    ImGui.Indent();
                    ImGui.PushID(dc.DataCenter);
                    
                    // Draw checkbox before collapsing header
                    using (ImRaii.PushColor(ImGuiCol.CheckMark, dcPartialSelected ? 0xFF888888 : 0xFFFFFFFF))
                    {
                        var dcCheck = dcAllSelected || dcPartialSelected;
                        if (ImGui.Checkbox($"##{dc.DataCenter}_check", ref dcCheck))
                        {
                            changed = true;
                            if (dcCheck)
                            {
                                foreach (var c in dcChars)
                                    _selectedCharacterIds.Add(c.Id);
                                _allSelected = false;
                            }
                            else
                            {
                                foreach (var c in dcChars)
                                    _selectedCharacterIds.Remove(c.Id);
                                if (_selectedCharacterIds.Count == 0)
                                    _allSelected = true;
                            }
                        }
                    }
                    ImGui.SameLine();
                    
                    // DC collapsing header - collapse when all selected
                    var dcFlags = dcAllSelected ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen;
                    var dcOpen = ImGui.CollapsingHeader(dc.DataCenter, dcFlags);
                    
                    if (dcOpen)
                    {
                        foreach (var worldGroup in dc.Worlds)
                        {
                            var worldChars = worldGroup.ToList();
                            var worldSelectedCount = worldChars.Count(c => _selectedCharacterIds.Contains(c.Id));
                            var worldAllSelected = worldSelectedCount == worldChars.Count && worldChars.Count > 0;
                            var worldPartialSelected = worldSelectedCount > 0 && !worldAllSelected;
                            
                            ImGui.Indent();
                            ImGui.PushID(worldGroup.Key);
                            
                            // Draw checkbox for world
                            using (ImRaii.PushColor(ImGuiCol.CheckMark, worldPartialSelected ? 0xFF888888 : 0xFFFFFFFF))
                            {
                                var worldCheck = worldAllSelected || worldPartialSelected;
                                if (ImGui.Checkbox($"##{worldGroup.Key}_check", ref worldCheck))
                                {
                                    changed = true;
                                    if (worldCheck)
                                    {
                                        foreach (var c in worldChars)
                                            _selectedCharacterIds.Add(c.Id);
                                        _allSelected = false;
                                    }
                                    else
                                    {
                                        foreach (var c in worldChars)
                                            _selectedCharacterIds.Remove(c.Id);
                                        if (_selectedCharacterIds.Count == 0)
                                            _allSelected = true;
                                    }
                                }
                            }
                            ImGui.SameLine();
                            
                            // World tree node - collapse when all selected
                            var worldFlags = worldAllSelected ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen;
                            var worldOpen = ImGui.TreeNodeEx(worldGroup.Key, worldFlags);
                            
                            if (worldOpen)
                            {
                                // Draw characters in world
                                foreach (var character in worldChars)
                                {
                                    changed |= DrawMultiSelectCharacterRowSimple(character, favorites.Contains(character.Id));
                                }
                                
                                ImGui.TreePop();
                            }
                            
                            ImGui.PopID();
                            ImGui.Unindent();
                        }
                    }
                    
                    ImGui.PopID();
                    ImGui.Unindent();
                }
            }
            
            ImGui.PopID();
        }
        
        return changed;
    }
    
    private bool DrawMultiSelectCharacterRowSimple(ComboCharacter character, bool isFavorite)
    {
        var changed = false;
        var isSelected = _selectedCharacterIds.Contains(character.Id);
        
        // Highlight selected rows
        if (isSelected && !_allSelected)
        {
            var cursorPos = ImGui.GetCursorScreenPos();
            var rowHeight = ImGui.GetTextLineHeightWithSpacing();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.GetWindowDrawList().AddRectFilled(
                cursorPos,
                cursorPos + new Vector2(rowWidth, rowHeight),
                SelectedBgColor);
        }
        
        // Favorite star
        DrawFavoriteStar(character.Id, isFavorite);
        ImGui.SameLine();
        
        // Checkbox for multi-select
        var selected = isSelected;
        if (ImGui.Checkbox($"##{character.Id}", ref selected))
        {
            if (selected)
            {
                _selectedCharacterIds.Add(character.Id);
                _allSelected = false;
            }
            else
            {
                _selectedCharacterIds.Remove(character.Id);
                if (_selectedCharacterIds.Count == 0)
                    _allSelected = true;
            }
            changed = true;
        }
        ImGui.SameLine();
        
        // Character name only (world is shown in parent tree node)
        ImGui.TextUnformatted(character.Name);
        
        return changed;
    }
    
    private bool DrawMultiSelectCharacterRow(ComboCharacter character, bool isFavorite)
    {
        var changed = false;
        var isSelected = _selectedCharacterIds.Contains(character.Id);
        
        // Highlight selected rows
        if (isSelected && !_allSelected)
        {
            var cursorPos = ImGui.GetCursorScreenPos();
            var rowHeight = ImGui.GetTextLineHeightWithSpacing();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            ImGui.GetWindowDrawList().AddRectFilled(
                cursorPos,
                cursorPos + new Vector2(rowWidth, rowHeight),
                SelectedBgColor);
        }
        
        // Favorite star
        DrawFavoriteStar(character.Id, isFavorite);
        ImGui.SameLine();
        
        // Checkbox for multi-select
        var selected = isSelected;
        if (ImGui.Checkbox($"##{character.Id}", ref selected))
        {
            if (selected)
            {
                _selectedCharacterIds.Add(character.Id);
                _allSelected = false;
            }
            else
            {
                _selectedCharacterIds.Remove(character.Id);
                // If nothing selected, revert to "All"
                if (_selectedCharacterIds.Count == 0)
                    _allSelected = true;
            }
            changed = true;
        }
        ImGui.SameLine();
        
        // Character name
        ImGui.TextUnformatted(character.Name);
        
        // World (right-aligned, dimmed) with padding
        if (character.World != null)
        {
            ImGui.SameLine();
            var worldText = $"@ {character.World}";
            var textWidth = ImGui.CalcTextSize(worldText).X;
            var rightPadding = ImGui.GetStyle().ScrollbarSize + 8; // Padding for scrollbar + extra
            var availWidth = ImGui.GetContentRegionAvail().X - rightPadding;
            if (availWidth > textWidth + 20)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availWidth - textWidth);
                using (ImRaii.PushColor(ImGuiCol.Text, WorldColor))
                {
                    ImGui.TextUnformatted(worldText);
                }
            }
        }
        
        return changed;
    }

    /// <summary>
    /// Sets the current selection by character ID (single-select mode).
    /// </summary>
    public void SetSelection(ulong characterId)
    {
        _currentCharacterId = characterId;
        CurrentSelectionIdx = Items.IndexOf(i => i.Id == characterId);
        if (CurrentSelectionIdx >= 0)
            CurrentSelection = Items[CurrentSelectionIdx];
        else
            CurrentSelection = default;
            
        // Also update multi-select state for consistency
        _selectedCharacterIds.Clear();
        if (characterId == 0)
            _allSelected = true;
        else
        {
            _allSelected = false;
            _selectedCharacterIds.Add(characterId);
        }
    }
    
    /// <summary>
    /// Sets the selection to multiple character IDs (multi-select mode).
    /// </summary>
    public void SetSelection(IEnumerable<ulong> characterIds)
    {
        _selectedCharacterIds.Clear();
        _allSelected = false;
        
        foreach (var id in characterIds)
        {
            if (id == 0)
            {
                _allSelected = true;
                _selectedCharacterIds.Clear();
                break;
            }
            _selectedCharacterIds.Add(id);
        }
        
        if (_selectedCharacterIds.Count == 0)
            _allSelected = true;
            
        // Update single-select state for consistency
        if (_allSelected)
        {
            _currentCharacterId = 0;
            CurrentSelectionIdx = 0;
            CurrentSelection = AllCharacters;
        }
        else if (_selectedCharacterIds.Count == 1)
        {
            var id = _selectedCharacterIds.First();
            _currentCharacterId = id;
            CurrentSelectionIdx = Items.IndexOf(i => i.Id == id);
            CurrentSelection = CurrentSelectionIdx >= 0 ? Items[CurrentSelectionIdx] : default;
        }
    }
    
    /// <summary>
    /// Toggles selection of a specific character ID (multi-select mode).
    /// </summary>
    public void ToggleSelection(ulong characterId)
    {
        if (characterId == 0)
        {
            SelectAll();
            return;
        }
        
        if (_selectedCharacterIds.Contains(characterId))
            _selectedCharacterIds.Remove(characterId);
        else
        {
            _selectedCharacterIds.Add(characterId);
            _allSelected = false;
        }
        
        if (_selectedCharacterIds.Count == 0)
            _allSelected = true;
            
        MultiSelectionChanged?.Invoke(_selectedCharacterIds);
    }

    /// <summary>
    /// Selects "All Characters".
    /// </summary>
    public void SelectAll()
    {
        _allSelected = true;
        _selectedCharacterIds.Clear();
        _currentCharacterId = 0;
        CurrentSelectionIdx = 0;
        CurrentSelection = AllCharacters;
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _allSelected = true;
        _selectedCharacterIds.Clear();
        _currentCharacterId = 0;
        CurrentSelectionIdx = -1;
        CurrentSelection = default;
    }

    /// <summary>
    /// Refreshes the character list from the database.
    /// </summary>
    public void RefreshCharacters()
    {
        ResetFilter();
    }
    
    /// <summary>
    /// Disposes resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _favoritesService.OnFavoritesChanged -= OnFavoritesChanged;
        
        if (_priceTrackingService != null)
        {
            _priceTrackingService.OnWorldDataLoaded -= OnWorldDataLoaded;
        }
    }
}
