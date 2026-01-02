using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// TopInventoryValueTool partial class containing settings UI and import/export logic.
/// </summary>
public partial class TopInventoryValueTool
{
    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        try
        {
            var settings = Settings;

            var maxItems = settings.MaxItems;
            if (ImGui.SliderInt("Max items", ref maxItems, 10, 500))
            {
                settings.MaxItems = maxItems;
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Maximum number of items to display.", "100");

            var showAllCharacters = settings.ShowAllCharacters;
            if (ImGui.Checkbox("Show all characters combined", ref showAllCharacters))
            {
                settings.ShowAllCharacters = showAllCharacters;
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Combine items from all characters, or select a specific character.", "On");

            var includeRetainers = settings.IncludeRetainers;
            if (ImGui.Checkbox("Include retainer inventories", ref includeRetainers))
            {
                settings.IncludeRetainers = includeRetainers;
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Include items from retainer inventories.", "On");

            var includeGil = settings.IncludeGil;
            if (ImGui.Checkbox("Include gil in list", ref includeGil))
            {
                settings.IncludeGil = includeGil;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Show gil as a row in the top items list.", "On");

            var groupByItem = settings.GroupByItem;
            if (ImGui.Checkbox("Group by item", ref groupByItem))
            {
                settings.GroupByItem = groupByItem;
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Combine quantities of the same item across inventories.", "On");

            ImGui.Spacing();
            ImGui.TextUnformatted("Filters");
            ImGui.Separator();

            var minThreshold = (int)settings.MinValueThreshold;
            if (ImGui.InputInt("Min value threshold", ref minThreshold, 1000, 10000))
            {
                settings.MinValueThreshold = Math.Max(0, minThreshold);
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Only show items worth at least this much gil.", "0");

            // Item exclusion section
            ImGui.Spacing();
            ImGui.TextUnformatted("Excluded Items");
            ImGui.Separator();

            DrawExcludedItemsSection(settings);

            // Refresh button
            ImGui.Spacing();
            if (ImGui.Button("Refresh Now"))
            {
                _ = Task.Run(RefreshTopItemsAsync);
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Settings error: {ex.Message}");
        }
    }

    private void DrawExcludedItemsSection(Models.Universalis.TopInventoryValueItemsSettings settings)
    {
        // Item picker for adding exclusions
        ImGui.TextDisabled("Add item to exclude:");
        if (_itemCombo.Draw(250))
        {
            if (_itemCombo.SelectedItemId > 0)
            {
                settings.ExcludedItemIds.Add(_itemCombo.SelectedItemId);
                NotifyToolSettingsChanged();
                _itemCombo.ClearSelection();
                _ = Task.Run(RefreshTopItemsAsync);
            }
        }
        ShowSettingTooltip("Select an item to exclude from the top items list.", "");

        // Show current exclusions
        if (settings.ExcludedItemIds.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Currently excluded ({settings.ExcludedItemIds.Count}):");
            
            uint? itemToRemove = null;
            foreach (var itemId in settings.ExcludedItemIds)
            {
                var itemName = _itemDataService.GetItemName(itemId);
                ImGui.BulletText(itemName);
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##{itemId}"))
                {
                    itemToRemove = itemId;
                }
            }

            // Remove outside iteration to avoid collection modification
            if (itemToRemove.HasValue)
            {
                settings.ExcludedItemIds.Remove(itemToRemove.Value);
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }

            // Clear all button
            if (ImGui.Button("Clear All Exclusions"))
            {
                settings.ExcludedItemIds.Clear();
                NotifyToolSettingsChanged();
                _ = Task.Run(RefreshTopItemsAsync);
            }
        }
        else
        {
            ImGui.TextDisabled("No items excluded");
        }
    }

    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["MaxItems"] = Settings.MaxItems,
            ["ShowAllCharacters"] = Settings.ShowAllCharacters,
            ["SelectedCharacterId"] = Settings.SelectedCharacterId,
            ["IncludeRetainers"] = Settings.IncludeRetainers,
            ["IncludeGil"] = Settings.IncludeGil,
            ["MinValueThreshold"] = Settings.MinValueThreshold,
            ["GroupByItem"] = Settings.GroupByItem,
            ["ExcludedItemIds"] = Settings.ExcludedItemIds.ToList()
        };
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        _instanceSettings.MaxItems = GetSetting(settings, "MaxItems", _instanceSettings.MaxItems);
        _instanceSettings.ShowAllCharacters = GetSetting(settings, "ShowAllCharacters", _instanceSettings.ShowAllCharacters);
        _instanceSettings.SelectedCharacterId = GetSetting(settings, "SelectedCharacterId", _instanceSettings.SelectedCharacterId);
        _instanceSettings.IncludeRetainers = GetSetting(settings, "IncludeRetainers", _instanceSettings.IncludeRetainers);
        _instanceSettings.IncludeGil = GetSetting(settings, "IncludeGil", _instanceSettings.IncludeGil);
        _instanceSettings.MinValueThreshold = GetSetting(settings, "MinValueThreshold", _instanceSettings.MinValueThreshold);
        _instanceSettings.GroupByItem = GetSetting(settings, "GroupByItem", _instanceSettings.GroupByItem);
        
        // Deserialize ExcludedItemIds from JsonElement array to HashSet<uint>
        var excludedIds = GetSetting<List<uint>>(settings, "ExcludedItemIds", null);
        if (excludedIds != null)
        {
            _instanceSettings.ExcludedItemIds = new HashSet<uint>(excludedIds);
        }
        
        // Sync selected character from settings
        _selectedCharacterId = _instanceSettings.SelectedCharacterId;
    }
}
