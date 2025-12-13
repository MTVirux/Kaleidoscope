using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;

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
                try { _helper.AvailableCharacters.Sort(); } catch { }
            }
            catch { }

            var count = _helper.AvailableCharacters.Count;
            // Build display names, inserting an "All" option at index 0
            var displayList = new List<string>();
            displayList.Add("All");
            if (count > 0)
            {
                displayList.AddRange(_helper.AvailableCharacters.Select(id => _helper.GetCharacterDisplayName(id)));
            }
            else
            {
                displayList.Add("No characters");
            }

            var names = displayList.ToArray();

            var idx = 0;
            if (count > 0)
            {
                // SelectedCharacterId maps to index+1 in the displayList because 0 == All
                var selIndex = _helper.AvailableCharacters.IndexOf(_helper.SelectedCharacterId);
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
                    else if (count > 0)
                    {
                        var id = _helper.AvailableCharacters[idx - 1];
                        _helper.LoadForCharacter(id);
                    }
                }
            }
            catch { }

#if DEBUG
            try
            {
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    ImGui.OpenPopup("giltracker_names_popup");
                    _namesPopupOpen = true;
                }
            }
            catch { }
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
                catch { }

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
