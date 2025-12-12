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
            if (_helper.AvailableCharacters.Count > 0)
            {
                var idx = _helper.AvailableCharacters.IndexOf(_helper.SelectedCharacterId);
                if (idx < 0) idx = 0;
                try
                {
                    var localCid = Svc.ClientState.LocalContentId;
                    if (localCid != 0 && !_helper.AvailableCharacters.Contains(localCid))
                    {
                        _helper.RefreshAvailableCharacters();
                    }
                }
                catch { }

                var names = _helper.AvailableCharacters.Select(id => _helper.GetCharacterDisplayName(id)).ToArray();
                if (ImGui.Combo("Character", ref idx, names, names.Length))
                {
                    var id = _helper.AvailableCharacters[idx];
                    _helper.LoadForCharacter(id);
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
                catch { }
#endif
            }

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
