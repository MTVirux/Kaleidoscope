using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
public readonly record struct ComboCharacter(ulong Id, string Name, string? World);

/// <summary>
/// A filtered combo box for selecting characters with favorites support.
/// Includes an "All Characters" option and supports filtering.
/// </summary>
public sealed class CharacterIconCombo : FilterComboCache<ComboCharacter>
{
    private readonly FavoritesService _favoritesService;
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService? _configService;
    
    // Current state
    private ulong _currentCharacterId;
    private float _innerWidth;
    
    // Special "All" entry
    private static readonly ComboCharacter AllCharacters = new(0, "All Characters", null);
    
    // Icon sizes
    private static readonly Vector2 StarSize = new(16, 16);
    
    // Colors
    private const uint FavoriteStarOn = 0xFF00CFFF;      // Yellow-gold
    private const uint FavoriteStarOff = 0x40FFFFFF;     // Dim white
    private const uint FavoriteStarHovered = 0xFF40DFFF; // Bright gold
    private const uint WorldColor = 0xFF808080;          // Dim gray for world

    /// <summary>
    /// The label for this combo (used for ImGui ID).
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the currently selected character.
    /// </summary>
    public ComboCharacter? SelectedCharacter => CurrentSelection;

    /// <summary>
    /// Gets the currently selected character ID, or 0 for "All".
    /// </summary>
    public ulong SelectedCharacterId => CurrentSelectionIdx >= 0 && CurrentSelection != null ? CurrentSelection.Id : 0;

    /// <summary>
    /// Whether "All Characters" is selected.
    /// </summary>
    public bool IsAllSelected => SelectedCharacterId == 0;

    /// <summary>
    /// Event fired when selection changes.
    /// </summary>
    public new event Action<ComboCharacter?, ComboCharacter?>? SelectionChanged;

    public CharacterIconCombo(
        SamplerService samplerService,
        FavoritesService favoritesService,
        ConfigurationService? configService,
        string label)
        : base(
            () => BuildCharacterList(samplerService, favoritesService, configService),
            MouseWheelType.Control,
            new Logger())
    {
        _samplerService = samplerService;
        _favoritesService = favoritesService;
        _configService = configService;
        Label = label;
        SearchByParts = true;
        
        // Subscribe to favorites changes to rebuild list
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
    }

    private void OnFavoritesChanged()
    {
        ResetFilter();
    }

    private static IReadOnlyList<ComboCharacter> BuildCharacterList(
        SamplerService samplerService,
        FavoritesService favoritesService,
        ConfigurationService? configService)
    {
        var characters = new List<ComboCharacter>();
        var favorites = favoritesService.FavoriteCharacters;
        var cacheService = samplerService.CacheService;

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

                // Get formatted name from cache service
                var displayName = cacheService.GetFormattedCharacterName(charId) ?? name ?? $"Character {charId}";
                
                // Try to extract world from name (format: "Name @ World")
                string? world = null;
                var atIndex = displayName.IndexOf('@');
                if (atIndex > 0)
                {
                    world = displayName[(atIndex + 1)..].Trim();
                    displayName = displayName[..atIndex].Trim();
                }

                characters.Add(new ComboCharacter(charId, displayName, world));
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
            LogService.Debug($"[CharacterIconCombo] Error building character list: {ex.Message}");
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
        var preview = CurrentSelectionIdx >= 0 && CurrentSelection != null ? ToString(CurrentSelection) : "Select character...";
        var charId = CurrentSelectionIdx >= 0 && CurrentSelection != null ? CurrentSelection.Id : 0ul;
        return Draw(preview, charId, width, width);
    }

    /// <summary>
    /// Sets the current selection by character ID.
    /// </summary>
    public void SetSelection(ulong characterId)
    {
        _currentCharacterId = characterId;
        CurrentSelectionIdx = Items.IndexOf(i => i.Id == characterId);
        if (CurrentSelectionIdx >= 0)
            CurrentSelection = Items[CurrentSelectionIdx];
        else
            CurrentSelection = default;
    }

    /// <summary>
    /// Selects "All Characters".
    /// </summary>
    public void SelectAll()
    {
        SetSelection(0);
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
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
}
