using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using Kaleidoscope.Services.Resources;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Data management category in the config window.
/// Provides data export, cleanup, and maintenance options.
/// </summary>
public sealed class DataCategory : IConfigCategory
{
    public string Label => "Data";
    public bool IsDeveloper => true;

    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService _configService;
    private readonly ResourceStore _resourceStore;

    public DataCategory(
        CurrencyTrackerService currencyTrackerService,
        ConfigurationService configService,
        ResourceStore resourceStore)
    {
        _currencyTrackerService = currencyTrackerService;
        _configService = configService;
        _resourceStore = resourceStore;
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
            }
            ImGui.SameLine();
            if (ImGui.Button("Sanitize DB Data"))
            {
                ImGui.OpenPopup("config_sanitize_db_confirm");
            }
        }

        ConfigUiHelpers.ConfirmPopup("config_clear_db_confirm", "This will permanently delete ALL data from the database (simulating a fresh install). Proceed?", () => { try { _currencyTrackerService.ClearAllData(); _resourceStore.Clear(); LogService.Info(LogCategory.UI, "Cleared all Kaleidoscope data"); } catch (Exception ex) { LogService.Error(LogCategory.UI, "Failed to clear data", ex); } });

        ConfigUiHelpers.ConfirmPopup("config_sanitize_db_confirm", "This will remove data for characters that do not have a stored name association. Proceed?", () => { try { var count = _currencyTrackerService.CleanUnassociatedCharacters(); LogService.Info(LogCategory.UI, $"Cleaned {count} unassociated character records"); } catch (Exception ex) { LogService.Error(LogCategory.UI, "Failed to sanitize data", ex); } });

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
        var cacheMb = _configService.Config.DatabaseCacheSizeMb;
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f),
            "Database and cache size settings have been moved to the Storage category.");
        ImGui.TextDisabled($"Current cache: {cacheMb * 2} MB total (2 connections × {cacheMb} MB)");
    }
}
