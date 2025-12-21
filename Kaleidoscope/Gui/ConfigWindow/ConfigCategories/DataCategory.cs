using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Data management category in the config window.
/// Provides data export, cleanup, and maintenance options.
/// </summary>
public class DataCategory
{
    private readonly SamplerService _samplerService;
    private readonly AutoRetainerIpcService _autoRetainerIpc;

    private bool _clearDbOpen = false;
    private bool _sanitizeDbOpen = false;
    private bool _importAutoRetainerOpen = false;
    private string _importStatus = "";
    private int _importCount = 0;

    public DataCategory(SamplerService samplerService, AutoRetainerIpcService autoRetainerIpc)
    {
        _samplerService = samplerService;
        _autoRetainerIpc = autoRetainerIpc;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Data Management");
        ImGui.Separator();
        var hasDb = _samplerService.HasDb;
        if (ImGui.Button("Export Gil CSV") && hasDb)
        {
            try
            {
                var fileName = _samplerService.ExportCsv(TrackedDataType.Gil);
                if (!string.IsNullOrEmpty(fileName)) ImGui.TextUnformatted($"Exported to {fileName}");
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to export CSV", ex);
            }
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

        // AutoRetainer import section
        ImGui.Separator();
        ImGui.TextUnformatted("Import from AutoRetainer");
        
        if (!_autoRetainerIpc.IsAvailable)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f), "AutoRetainer not available");
            if (ImGui.Button("Refresh Connection"))
            {
                _autoRetainerIpc.Refresh();
            }
        }
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f), "AutoRetainer connected");
            if (ImGui.Button("Import Characters from AutoRetainer") && hasDb)
            {
                ImGui.OpenPopup("config_import_autoretainer_confirm");
                _importAutoRetainerOpen = true;
                _importStatus = "";
                _importCount = 0;
            }
        }
        
        if (!string.IsNullOrEmpty(_importStatus))
        {
            ImGui.TextUnformatted(_importStatus);
        }

        if (ImGui.BeginPopupModal("config_clear_db_confirm", ref _clearDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("This will permanently delete all saved GilTracker data from the DB for all characters. Proceed?");
            if (ImGui.Button("Yes"))
            {
                try
                {
                    _samplerService.ClearAllData();
                    LogService.Info("Cleared all GilTracker data");
                }
                catch (Exception ex)
                {
                    LogService.Error("Failed to clear data", ex);
                }
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
                try
                {
                    var count = _samplerService.CleanUnassociatedCharacters();
                    LogService.Info($"Cleaned {count} unassociated character records");
                }
                catch (Exception ex)
                {
                    LogService.Error("Failed to sanitize data", ex);
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("No"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal("config_import_autoretainer_confirm", ref _importAutoRetainerOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("This will import all characters from AutoRetainer into the GilTracker database.");
            ImGui.TextUnformatted("AutoRetainer data takes priority: character names and gil amounts will be updated");
            ImGui.TextUnformatted("if they differ from our current data.");
            ImGui.Separator();
            
            if (ImGui.Button("Import"))
            {
                try
                {
                    var characters = _autoRetainerIpc.GetAllCharacterData();
                    _importCount = 0;
                    var updatedCount = 0;
                    
                    foreach (var (name, world, gil, cid) in characters)
                    {
                        if (cid == 0 || string.IsNullOrEmpty(name)) continue;
                        
                        // Always save/overwrite the character name from AutoRetainer (AR data takes priority)
                        _samplerService.DbService?.SaveCharacterName(cid, name);
                        
                        // Create a series if it doesn't exist
                        var seriesId = _samplerService.DbService?.GetOrCreateSeries("Gil", cid);
                        if (seriesId.HasValue)
                        {
                            // Check if we already have data for this character
                            var existingValue = _samplerService.DbService?.GetLastValueForCharacter("Gil", cid);
                            
                            // AutoRetainer data takes priority - add sample if gil differs from latest
                            if (existingValue == null || (long)existingValue.Value != gil)
                            {
                                _samplerService.DbService?.SaveSampleIfChanged("Gil", cid, gil);
                                if (existingValue != null) updatedCount++;
                            }
                            _importCount++;
                        }
                    }
                    
                    _importStatus = $"Imported {_importCount} characters from AutoRetainer";
                    if (updatedCount > 0)
                        _importStatus += $" ({updatedCount} updated with new gil values)";
                    LogService.Info(_importStatus);
                }
                catch (Exception ex)
                {
                    _importStatus = $"Import failed: {ex.Message}";
                    LogService.Error("Failed to import from AutoRetainer", ex);
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
}
