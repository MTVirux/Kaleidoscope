namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories
{
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;
    using Kaleidoscope.Services;

    public class DataCategory
    {
        private readonly SamplerService _samplerService;

        private bool _clearDbOpen = false;
        private bool _sanitizeDbOpen = false;

        public DataCategory(SamplerService samplerService)
        {
            _samplerService = samplerService;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Data Management");
            ImGui.Separator();
            var hasDb = _samplerService.HasDb;
            if (ImGui.Button("Export CSV") && hasDb)
            {
                try
                {
                    var fileName = _samplerService.ExportCsv();
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
                    try { _samplerService.ClearAllData(); } catch { }
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
                    try { _samplerService.CleanUnassociatedCharacters(); } catch { }
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
