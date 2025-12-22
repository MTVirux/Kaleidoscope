using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that shows the top items by value from character inventories.
/// Displays items that contribute the most to total liquid value.
/// </summary>
public class TopItemsTool : ToolComponent
{
    private readonly PriceTrackingService _priceTrackingService;
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly ItemDataService _itemDataService;
    private readonly ItemPickerWidget _itemPicker;

    // Cached data
    private List<(int ItemId, long Quantity, long Value, string Name)> _topItems = new();
    private long _totalValue = 0;
    private long _gilValue = 0;
    private DateTime _lastRefresh = DateTime.MinValue;
    private const int RefreshIntervalSeconds = 30;
    private bool _isRefreshing = false;

    // Character selection
    private ulong _selectedCharacterId = 0;
    private string[] _characterNames = Array.Empty<string>();
    private ulong[] _characterIds = Array.Empty<ulong>();
    private int _selectedCharacterIndex = 0;

    private TopItemsSettings Settings => _configService.Config.TopItems;
    private KaleidoscopeDbService DbService => _samplerService.DbService;

    public TopItemsTool(
        PriceTrackingService priceTrackingService,
        SamplerService samplerService,
        ConfigurationService configService,
        ItemDataService itemDataService,
        IDataManager dataManager)
    {
        _priceTrackingService = priceTrackingService;
        _samplerService = samplerService;
        _configService = configService;
        _itemDataService = itemDataService;

        // Create item picker for exclusion list (marketable only since we're dealing with prices)
        _itemPicker = new ItemPickerWidget(dataManager, itemDataService, priceTrackingService);

        Title = "Top Items";
        Size = new Vector2(400, 350);
        ScrollbarVisible = true;

        RefreshCharacterList();
    }

    private void RefreshCharacterList()
    {
        try
        {
            var chars = DbService.GetAllCharacterNames()
                .Select(c => (c.characterId, c.name))
                .DistinctBy(c => c.characterId)
                .OrderBy(c => c.name)
                .ToList();

            // Include "All Characters" option
            _characterNames = new string[chars.Count + 1];
            _characterIds = new ulong[chars.Count + 1];

            _characterNames[0] = "All Characters";
            _characterIds[0] = 0;

            for (int i = 0; i < chars.Count; i++)
            {
                _characterNames[i + 1] = chars[i].name;
                _characterIds[i + 1] = chars[i].characterId;
            }

            // Update selected index
            var idx = Array.IndexOf(_characterIds, _selectedCharacterId);
            _selectedCharacterIndex = idx >= 0 ? idx : 0;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[TopItemsTool] Error refreshing characters: {ex.Message}");
        }
    }

    private async Task RefreshTopItemsAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        try
        {
            var settings = Settings;
            var charId = settings.ShowAllCharacters ? (ulong?)null : 
                (_selectedCharacterId == 0 ? (ulong?)null : _selectedCharacterId);

            // Get top items (request more to account for filtering)
            var requestCount = settings.MaxItems + settings.ExcludedItemIds.Count;
            var items = await _priceTrackingService.GetTopItemsByValueAsync(
                charId,
                requestCount,
                settings.IncludeRetainers);

            // Resolve item names using ItemDataService and filter out excluded items
            var namedItems = new List<(int ItemId, long Quantity, long Value, string Name)>();
            foreach (var (itemId, qty, value) in items)
            {
                // Skip excluded items
                if (settings.ExcludedItemIds.Contains((uint)itemId))
                    continue;

                var name = _itemDataService.GetItemName(itemId);
                namedItems.Add((itemId, qty, value, name));
            }

            // Limit to MaxItems after filtering
            _topItems = namedItems.Take(settings.MaxItems).ToList();

            // Get gil value
            if (charId.HasValue)
            {
                var (total, gil, item) = await _priceTrackingService.CalculateInventoryValueAsync(charId.Value, settings.IncludeRetainers);
                _totalValue = total;
                _gilValue = gil;
            }
            else
            {
                // Calculate for all characters
                long totalGil = 0;
                long totalValue = 0;
                var allChars = DbService.GetAllCharacterNames().Select(c => c.characterId).Distinct();
                
                foreach (var cid in allChars)
                {
                    var (total, gil, item) = await _priceTrackingService.CalculateInventoryValueAsync(cid, settings.IncludeRetainers);
                    totalGil += gil;
                    totalValue += total;
                }

                _gilValue = totalGil;
                _totalValue = totalValue;
            }

            _lastRefresh = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[TopItemsTool] Error refreshing: {ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public override void DrawContent()
    {
        try
        {
            // Auto-refresh
            if ((DateTime.UtcNow - _lastRefresh).TotalSeconds > RefreshIntervalSeconds)
            {
                _ = Task.Run(RefreshTopItemsAsync);
            }

            // Header with character selector and totals
            DrawHeader();

            ImGui.Separator();

            // Items list
            DrawItemsList();
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[TopItemsTool] Draw error: {ex.Message}");
        }
    }

    private void DrawHeader()
    {
        var settings = Settings;

        // Character selector (only if not "All" mode)
        if (!settings.ShowAllCharacters && _characterNames.Length > 0)
        {
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("##CharSelector", ref _selectedCharacterIndex, _characterNames, _characterNames.Length))
            {
                _selectedCharacterId = _characterIds[_selectedCharacterIndex];
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ImGui.SameLine();
        }

        // Totals
        if (settings.IncludeGil)
        {
            ImGui.TextUnformatted($"Total: {FormatUtils.FormatGil(_totalValue)} (Gil: {FormatUtils.FormatGil(_gilValue)})");
        }
        else
        {
            var itemValue = _totalValue - _gilValue;
            ImGui.TextUnformatted($"Item Value: {FormatUtils.FormatGil(itemValue)}");
        }

        // Refresh button
        ImGui.SameLine();
        if (ImGui.SmallButton(_isRefreshing ? "..." : "↻"))
        {
            _ = Task.Run(RefreshTopItemsAsync);
        }
    }

    private void DrawItemsList()
    {
        var settings = Settings;
        var availableHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginChild("##TopItemsList", new Vector2(0, availableHeight), false))
        {
            // Gil row first if included
            if (settings.IncludeGil && _gilValue > 0)
            {
                DrawGilRow();
            }

            // Item rows
            if (_topItems.Count == 0)
            {
                ImGui.TextDisabled("No items to display");
                ImGui.TextDisabled("Make sure price tracking is enabled");
            }
            else
            {
                int rank = 1;
                foreach (var item in _topItems)
                {
                    if (item.Value < settings.MinValueThreshold)
                        continue;

                    DrawItemRow(rank++, item);
                }
            }
            ImGui.EndChild();
        }
    }

    private void DrawGilRow()
    {
        var percentage = _totalValue > 0 ? (float)_gilValue / _totalValue * 100 : 0;
        
        // Gold color for gil
        ImGui.TextColored(new Vector4(1f, 0.84f, 0f, 1f), "●");
        ImGui.SameLine();
        ImGui.TextUnformatted("Gil");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 150);
        ImGui.TextUnformatted($"{FormatUtils.FormatGil(_gilValue)} ({percentage:F1}%)");
    }

    private void DrawItemRow(int rank, (int ItemId, long Quantity, long Value, string Name) item)
    {
        var percentage = _totalValue > 0 ? (float)item.Value / _totalValue * 100 : 0;
        
        // Color gradient from green to yellow to orange based on rank
        var hue = Math.Max(0, 0.33f - (rank * 0.03f));
        var color = HsvToRgb(hue, 0.8f, 0.9f);

        // Rank indicator
        ImGui.TextColored(color, $"#{rank}");
        ImGui.SameLine();

        // Item name and quantity
        var text = Settings.GroupByItem 
            ? $"{item.Name} x{item.Quantity}"
            : item.Name;
        ImGui.TextUnformatted(text);

        // Value and percentage (right-aligned)
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
        ImGui.TextUnformatted($"{FormatUtils.FormatGil(item.Value)} ({percentage:F1}%)");
    }

    private static Vector4 HsvToRgb(float h, float s, float v)
    {
        float r, g, b;
        
        int i = (int)(h * 6);
        float f = h * 6 - i;
        float p = v * (1 - s);
        float q = v * (1 - f * s);
        float t = v * (1 - (1 - f) * s);

        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }

        return new Vector4(r, g, b, 1f);
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        try
        {
            var settings = Settings;

            ImGui.TextUnformatted("Top Items Settings");
            ImGui.Separator();

            var maxItems = settings.MaxItems;
            if (ImGui.SliderInt("Max items", ref maxItems, 10, 500))
            {
                settings.MaxItems = maxItems;
                _configService.Save();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Maximum number of items to display.", "100");

            var showAllCharacters = settings.ShowAllCharacters;
            if (ImGui.Checkbox("Show all characters combined", ref showAllCharacters))
            {
                settings.ShowAllCharacters = showAllCharacters;
                _configService.Save();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Combine items from all characters, or select a specific character.", "On");

            var includeRetainers = settings.IncludeRetainers;
            if (ImGui.Checkbox("Include retainer inventories", ref includeRetainers))
            {
                settings.IncludeRetainers = includeRetainers;
                _configService.Save();
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ShowSettingTooltip("Include items from retainer inventories.", "On");

            var includeGil = settings.IncludeGil;
            if (ImGui.Checkbox("Include gil in list", ref includeGil))
            {
                settings.IncludeGil = includeGil;
                _configService.Save();
            }
            ShowSettingTooltip("Show gil as a row in the top items list.", "On");

            var groupByItem = settings.GroupByItem;
            if (ImGui.Checkbox("Group by item", ref groupByItem))
            {
                settings.GroupByItem = groupByItem;
                _configService.Save();
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
                _configService.Save();
            }
            ShowSettingTooltip("Only show items worth at least this much gil.", "0");

            // Item exclusion section
            ImGui.Spacing();
            ImGui.TextUnformatted("Excluded Items");
            ImGui.Separator();

            // Item picker for adding exclusions
            ImGui.TextDisabled("Add item to exclude:");
            if (_itemPicker.Draw("##ExcludeItemPicker", marketableOnly: true, width: 250))
            {
                if (_itemPicker.SelectedItemId.HasValue)
                {
                    settings.ExcludedItemIds.Add(_itemPicker.SelectedItemId.Value);
                    _configService.Save();
                    _itemPicker.ClearSelection();
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
                    _configService.Save();
                    _ = Task.Run(RefreshTopItemsAsync);
                }

                // Clear all button
                if (ImGui.Button("Clear All Exclusions"))
                {
                    settings.ExcludedItemIds.Clear();
                    _configService.Save();
                    _ = Task.Run(RefreshTopItemsAsync);
                }
            }
            else
            {
                ImGui.TextDisabled("No items excluded");
            }

            // Refresh button
            ImGui.Spacing();
            if (ImGui.Button("Refresh Now"))
            {
                _ = Task.Run(RefreshTopItemsAsync);
            }
            ImGui.SameLine();
            if (ImGui.Button("Refresh Character List"))
            {
                RefreshCharacterList();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[TopItemsTool] Settings error: {ex.Message}");
        }
    }

    public override void Dispose()
    {
        // No resources to dispose
    }
}
