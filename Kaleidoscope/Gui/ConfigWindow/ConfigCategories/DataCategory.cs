namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories
{
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

    public class DataCategory
    {
        private readonly Func<bool>? _hasDb;
        private readonly Action? _clearAllData;
        private readonly Func<int>? _cleanUnassociatedCharacters;
        private readonly Func<string?>? _exportCsv;

        private bool _clearDbOpen = false;
        private bool _sanitizeDbOpen = false;

        public DataCategory(Func<bool>? hasDb, Action? clearAllData, Func<int>? cleanUnassociatedCharacters, Func<string?>? exportCsv)
        {
            this._hasDb = hasDb;
            this._clearAllData = clearAllData;
            this._cleanUnassociatedCharacters = cleanUnassociatedCharacters;
            this._exportCsv = exportCsv;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Data Management");
            ImGui.Separator();
            var hasDb = this._hasDb != null ? this._hasDb() : false;
            if (ImGui.Button("Export CSV") && hasDb)
            {
                try
                {
                    var fileName = this._exportCsv != null ? this._exportCsv() : null;
                    if (!string.IsNullOrEmpty(fileName)) ImGui.TextUnformatted($"Exported to {fileName}");
                }
                catch { }
            }

            if (hasDb)
            {
                if (ImGui.Button("Clear DB"))
                {
                    ImGui.OpenPopup("config_clear_db_confirm");
                    _clearDbOpen = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("Sanitize DB Data"))
                {
                    ImGui.OpenPopup("config_sanitize_db_confirm");
                    _sanitizeDbOpen = true;
                }
            }

            if (ImGui.BeginPopupModal("config_clear_db_confirm", ref _clearDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("This will permanently delete all saved GilTracker data from the DB for all characters. Proceed?");
                if (ImGui.Button("Yes"))
                {
                    try { this._clearAllData?.Invoke(); } catch { }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            if (ImGui.BeginPopupModal("config_sanitize_db_confirm", ref _sanitizeDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("This will remove GilTracker data for characters that do not have a stored name association. Proceed?");
                if (ImGui.Button("Yes"))
                {
                    try { this._cleanUnassociatedCharacters?.Invoke(); } catch { }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
    }
}
