using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Data management category in the config window.
/// Provides data export, cleanup, and maintenance options.
/// </summary>
public sealed class DataCategory
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly AutoRetainerIpcService _autoRetainerIpc;
    private readonly ConfigurationService _configService;

    private bool _clearDbOpen = false;
    private bool _sanitizeDbOpen = false;
    private bool _importAutoRetainerOpen = false;
    private string _importStatus = "";
    private int _importCount = 0;

    public DataCategory(CurrencyTrackerService currencyTrackerService, AutoRetainerIpcService autoRetainerIpc, ConfigurationService configService)
    {
        _currencyTrackerService = currencyTrackerService;
        _autoRetainerIpc = autoRetainerIpc;
        _configService = configService;
    }

    public void Draw()
    {
        DrawDatabaseSettings();
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Data Management");
        ImGui.Separator();
        var hasDb = _currencyTrackerService.HasDb;
        if (ImGui.Button("Export Gil CSV") && hasDb)
        {
            try
            {
                var fileName = _currencyTrackerService.ExportCsv(TrackedDataType.Gil);
                if (!string.IsNullOrEmpty(fileName)) ImGui.TextUnformatted($"Exported to {fileName}");
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.UI, "Failed to export CSV", ex);
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
            ImGui.TextUnformatted("This will permanently delete ALL data from the database (simulating a fresh install). Proceed?");
            if (ImGui.Button("Yes"))
            {
                try
                {
                    _currencyTrackerService.ClearAllData();
                    LogService.Info(LogCategory.UI, "Cleared all GilTracker data");
                }
                catch (Exception ex)
                {
                    LogService.Error(LogCategory.UI, "Failed to clear data", ex);
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
                    var count = _currencyTrackerService.CleanUnassociatedCharacters();
                    LogService.Info(LogCategory.UI, $"Cleaned {count} unassociated character records");
                }
                catch (Exception ex)
                {
                    LogService.Error(LogCategory.UI, "Failed to sanitize data", ex);
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
                        _currencyTrackerService.DbService?.SaveCharacterName(cid, name);
                        
                        // Create a series if it doesn't exist
                        var seriesId = _currencyTrackerService.DbService?.GetOrCreateSeries("Gil", cid);
                        if (seriesId.HasValue)
                        {
                            // Check if we already have data for this character
                            var existingValue = _currencyTrackerService.DbService?.GetLastValueForCharacter("Gil", cid);
                            
                            // AutoRetainer data takes priority - add sample if gil differs from latest
                            if (existingValue == null || (long)existingValue.Value != gil)
                            {
                                _currencyTrackerService.DbService?.SaveSampleIfChanged("Gil", cid, gil);
                                if (existingValue != null) updatedCount++;
                            }
                            _importCount++;
                        }
                    }
                    
                    _importStatus = $"Imported {_importCount} characters from AutoRetainer";
                    if (updatedCount > 0)
                        _importStatus += $" ({updatedCount} updated with new gil values)";
                    LogService.Info(LogCategory.UI, _importStatus);
                }
                catch (Exception ex)
                {
                    _importStatus = $"Import failed: {ex.Message}";
                    LogService.Error(LogCategory.UI, "Failed to import from AutoRetainer", ex);
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

    private void DrawDatabaseSettings()
    {
        ImGui.TextUnformatted("Database Settings");
        ImGui.Separator();
        
        // Show count of items with historical tracking enabled
        var config = _configService.Config;
        var itemsWithTracking = config.ItemsWithHistoricalTracking.Count;
        if (itemsWithTracking > 0)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f), 
                $"{itemsWithTracking} item(s) have historical tracking enabled.");
        }
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), 
                "No items have historical tracking enabled.");
        }
        ImGui.TextDisabled("Enable historical tracking per-item in the Data Tool settings or Items category.");
        
        ImGui.Spacing();
        
        // Reference to Storage category for cache/size settings
        var currencyTrackerConfig = _configService.CurrencyTrackerConfig;
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), 
            "Database and cache size settings have been moved to the Storage category.");
        ImGui.TextDisabled($"Current cache: {currencyTrackerConfig.DatabaseCacheSizeMb * 2} MB total (2 connections × {currencyTrackerConfig.DatabaseCacheSizeMb} MB)");
    }
}
