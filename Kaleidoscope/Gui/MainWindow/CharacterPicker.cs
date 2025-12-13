using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow
{
    internal class CharacterPicker
    {
        private readonly GilTrackerHelper _helper;
#if DEBUG
        private bool _namesPopupOpen = false;
#endif

        public CharacterPicker(GilTrackerHelper helper)
        {
            _helper = helper ?? throw new ArgumentNullException(nameof(helper));
        }

        public void Draw()
        {
            // Refresh and ensure we include any characters that have stored names
            try
            {
                _helper.RefreshAvailableCharacters();
                var stored = _helper.GetAllStoredCharacterNames();
                if (stored != null && stored.Count > 0)
                {
                    foreach (var e in stored)
                    {
                        if (!_helper.AvailableCharacters.Contains(e.cid)) _helper.AvailableCharacters.Add(e.cid);
                    }
                }
                // Keep the list sorted for predictable ordering
                _helper.AvailableCharacters.Sort();
            }
            catch (Exception ex)
            {
                LogService.Debug($"Character refresh error: {ex.Message}");
            }

            var count = _helper.AvailableCharacters.Count;
            // Build a filtered list of visible character ids (hide entries where
            // the display resolver falls back to the raw numeric CID). This keeps
            // the dropdown free of numeric CIDs when no name is available.
            var visibleIds = new List<ulong>();
            if (count > 0)
            {
                foreach (var id in _helper.AvailableCharacters)
                {
                    try
                    {
                        var name = _helper.GetCharacterDisplayName(id);
                        if (!string.IsNullOrEmpty(name) && name != id.ToString())
                        {
                            visibleIds.Add(id);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug($"Display name error for {id}: {ex.Message}");
                    }
                }
            }

            var visibleCount = visibleIds.Count;
            // Build display names, inserting an "All" option at index 0
            var displayList = new List<string> { "All" };
            if (visibleCount > 0)
            {
                displayList.AddRange(visibleIds.Select(id => _helper.GetCharacterDisplayName(id)));
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
                var selIndex = visibleIds.IndexOf(_helper.SelectedCharacterId);
                idx = selIndex < 0 ? 0 : selIndex + 1;
            }

            try
            {
                if (ImGui.Combo("Character", ref idx, names, names.Length))
                {
                    // If user selected "All" (idx == 0), load for character id 0 (clears selection)
                    if (idx == 0)
                        {
                            // Load aggregated data across all characters
                            _helper.LoadAllCharacters();
                        }
                    else if (visibleCount > 0)
                    {
                        var id = visibleIds[idx - 1];
                        _helper.LoadForCharacter(id);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Character selection error", ex);
            }

#if DEBUG
            try
            {
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    ImGui.OpenPopup("giltracker_names_popup");
                    _namesPopupOpen = true;
                }
            }
            catch (Exception ex) { LogService.Debug($"[CharacterPicker] Debug popup trigger failed: {ex.Message}"); }
#endif

#if DEBUG
            if (ImGui.BeginPopupModal("giltracker_names_popup", ref _namesPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                try
                {
                    var entries = _helper.GetAllStoredCharacterNames();
                    if (entries.Count == 0)
                    {
                        ImGui.TextUnformatted("No stored character names in DB.");
                    }
                    else
                    {
                        ImGui.TextUnformatted("Stored character names and CIDs:");
                        ImGui.Separator();
                        ImGui.BeginChild("giltracker_names_child", new Vector2(600, 300), true);
                        for (var i = 0; i < entries.Count; i++)
                        {
                            var e = entries[i];
                            var display = string.IsNullOrEmpty(e.name) ? "(null)" : e.name;
                            ImGui.TextUnformatted($"{i}: {e.cid}  {display}");
                        }
                        ImGui.EndChild();
                    }
                }
                catch (Exception ex) { LogService.Debug($"[CharacterPicker] Debug popup content render failed: {ex.Message}"); }

                if (ImGui.Button("Close"))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
#endif
        }
    }
}
