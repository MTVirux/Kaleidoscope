using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable character selection dropdown widget.
/// Can be used with any data source that implements ICharacterDataSource.
/// </summary>
public class CharacterPickerWidget
{
    private readonly ICharacterDataSource _dataSource;
    private readonly ConfigurationService? _configService;
    private readonly AutoRetainerIpcService? _autoRetainerService;
#if DEBUG
    private bool _namesPopupOpen = false;
#endif

    /// <summary>
    /// Creates a new CharacterPickerWidget.
    /// </summary>
    /// <param name="dataSource">The data source providing character information.</param>
    /// <param name="configService">Optional configuration service for sort order settings.</param>
    /// <param name="autoRetainerService">Optional AutoRetainer service for AR sort order.</param>
    public CharacterPickerWidget(
        ICharacterDataSource dataSource,
        ConfigurationService? configService = null,
        AutoRetainerIpcService? autoRetainerService = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _configService = configService;
        _autoRetainerService = autoRetainerService;
    }

    /// <summary>
    /// Draws the character picker combo box.
    /// </summary>
    public void Draw()
    {
        Draw("Character");
    }

    /// <summary>
    /// Draws the character picker combo box with a custom label.
    /// </summary>
    /// <param name="label">The label for the combo box.</param>
    public void Draw(string label)
    {
        // Refresh and ensure we include any characters that have stored names
        try
        {
            _dataSource.RefreshAvailableCharacters();
            var stored = _dataSource.GetAllStoredCharacterNames();
            if (stored != null && stored.Count > 0)
            {
                foreach (var e in stored)
                {
                    if (!_dataSource.AvailableCharacters.Contains(e.cid))
                        _dataSource.AvailableCharacters.Add(e.cid);
                }
            }
            // Apply configured sort order
            ApplySortOrder(_dataSource.AvailableCharacters);
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CharacterPickerWidget] Character refresh error: {ex.Message}");
        }

        var count = _dataSource.AvailableCharacters.Count;

        // Build a filtered list of visible character ids (hide entries where
        // the display resolver falls back to the raw numeric CID). This keeps
        // the dropdown free of numeric CIDs when no name is available.
        var visibleIds = new List<ulong>();
        if (count > 0)
        {
            foreach (var id in _dataSource.AvailableCharacters)
            {
                try
                {
                    var name = _dataSource.GetCharacterDisplayName(id);
                    if (!string.IsNullOrEmpty(name) && name != id.ToString())
                    {
                        visibleIds.Add(id);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Debug($"[CharacterPickerWidget] Display name error for {id}: {ex.Message}");
                }
            }
        }

        var visibleCount = visibleIds.Count;

        // Build display names, inserting an "All" option at index 0
        var displayList = new List<string> { "All" };
        if (visibleCount > 0)
        {
            displayList.AddRange(visibleIds.Select(id => _dataSource.GetCharacterDisplayName(id)));
        }
        else
        {
            displayList.Add("No characters");
        }

        var names = displayList.ToArray();

        var idx = 0;
        if (visibleCount > 0)
        {
            // SelectedCharacterId maps to index+1 in the displayList because 0 == All
            var selIndex = visibleIds.IndexOf(_dataSource.SelectedCharacterId);
            idx = selIndex < 0 ? 0 : selIndex + 1;
        }

        try
        {
            if (ImGui.Combo(label, ref idx, names, names.Length))
            {
                // If user selected "All" (idx == 0), load for character id 0 (clears selection)
                if (idx == 0)
                {
                    // Load aggregated data across all characters
                    _dataSource.LoadAllCharacters();
                }
                else if (visibleCount > 0)
                {
                    var id = visibleIds[idx - 1];
                    _dataSource.LoadForCharacter(id);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Character selection error", ex);
        }

#if DEBUG
        // Debug popup: only available in edit mode to avoid accidental activation
        try
        {
            if (StateService.IsEditModeStatic && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                ImGui.OpenPopup("characterpicker_names_popup");
                _namesPopupOpen = true;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CharacterPickerWidget] Debug popup trigger failed: {ex.Message}");
        }

        if (ImGui.BeginPopupModal("characterpicker_names_popup", ref _namesPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            try
            {
                var entries = _dataSource.GetAllStoredCharacterNames();
                if (entries.Count == 0)
                {
                    ImGui.TextUnformatted("No stored character names in DB.");
                }
                else
                {
                    ImGui.TextUnformatted("Stored character names and CIDs:");
                    ImGui.Separator();
                    ImGui.BeginChild("characterpicker_names_child", new Vector2(600, 300), true);
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        var display = string.IsNullOrEmpty(e.name) ? "(null)" : e.name;
                        ImGui.TextUnformatted($"{i}: {e.cid}  {display}");
                    }
                    ImGui.EndChild();
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[CharacterPickerWidget] Debug popup content render failed: {ex.Message}");
            }

            if (ImGui.Button("Close"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
#endif
    }

    /// <summary>
    /// Applies the configured sort order to the character list.
    /// </summary>
    private void ApplySortOrder(List<ulong> characters)
    {
        if (characters == null || characters.Count <= 1) return;

        var sortOrder = _configService?.Config.CharacterSortOrder ?? CharacterSortOrder.Alphabetical;

        switch (sortOrder)
        {
            case CharacterSortOrder.Alphabetical:
                characters.Sort((a, b) =>
                {
                    var nameA = _dataSource.GetCharacterDisplayName(a);
                    var nameB = _dataSource.GetCharacterDisplayName(b);
                    return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                });
                break;

            case CharacterSortOrder.ReverseAlphabetical:
                characters.Sort((a, b) =>
                {
                    var nameA = _dataSource.GetCharacterDisplayName(a);
                    var nameB = _dataSource.GetCharacterDisplayName(b);
                    return string.Compare(nameB, nameA, StringComparison.OrdinalIgnoreCase);
                });
                break;

            case CharacterSortOrder.AutoRetainer:
                var arOrder = _autoRetainerService?.GetRegisteredCharacterIds();
                if (arOrder != null && arOrder.Count > 0)
                {
                    var orderLookup = new Dictionary<ulong, int>();
                    for (var i = 0; i < arOrder.Count; i++)
                    {
                        orderLookup[arOrder[i]] = i;
                    }

                    characters.Sort((a, b) =>
                    {
                        var hasA = orderLookup.TryGetValue(a, out var orderA);
                        var hasB = orderLookup.TryGetValue(b, out var orderB);

                        if (hasA && hasB)
                            return orderA.CompareTo(orderB);
                        if (hasA)
                            return -1;
                        if (hasB)
                            return 1;

                        // Both not in AR, sort alphabetically
                        var nameA = _dataSource.GetCharacterDisplayName(a);
                        var nameB = _dataSource.GetCharacterDisplayName(b);
                        return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                    });
                }
                else
                {
                    // Fall back to alphabetical
                    characters.Sort((a, b) =>
                    {
                        var nameA = _dataSource.GetCharacterDisplayName(a);
                        var nameB = _dataSource.GetCharacterDisplayName(b);
                        return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                    });
                }
                break;
        }
    }
}
