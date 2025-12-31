using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using MTGui.Tree;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that displays a live feed of Universalis WebSocket price updates.
/// Shows listings added/removed and sales as they happen in real-time.
/// Click on any entry to open the ItemDetailsPopup with full market data from the API.
/// </summary>
public class WebsocketFeedTool : ToolComponent
{
    public override string ToolName => "Websocket Feed";
    
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly ConfigurationService _configService;
    private readonly ItemDataService _itemDataService;
    private readonly UniversalisService _universalisService;
    private readonly CurrencyTrackerService? _currencyTrackerService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly CharacterDataService? _characterDataService;
    private readonly WebsocketFeedSettings _instanceSettings;

    // World selection widget for filtering
    private WorldSelectionWidget? _worldSelectionWidget;
    private bool _worldSelectionWidgetInitialized = false;

    // Item details popup for clicking on entries
    private readonly ItemDetailsPopup _itemDetailsPopup;

    private static readonly string[] EventTypeFilters = { "All Events", "Listings Added", "Listings Removed", "Sales" };

    private WebsocketFeedSettings Settings => _instanceSettings;
    private PriceTrackingSettings PriceSettings => _configService.Config.PriceTracking;

    public WebsocketFeedTool(
        UniversalisWebSocketService webSocketService,
        PriceTrackingService priceTrackingService,
        ConfigurationService configService,
        ItemDataService itemDataService,
        UniversalisService universalisService,
        CurrencyTrackerService? CurrencyTrackerService = null,
        InventoryCacheService? inventoryCacheService = null,
        CharacterDataService? characterDataService = null)
    {
        _webSocketService = webSocketService;
        _priceTrackingService = priceTrackingService;
        _configService = configService;
        _itemDataService = itemDataService;
        _universalisService = universalisService;
        _currencyTrackerService = CurrencyTrackerService;
        _inventoryCacheService = inventoryCacheService;
        _characterDataService = characterDataService;
        _instanceSettings = new WebsocketFeedSettings();

        // Create item details popup for viewing market data when clicking entries
        _itemDetailsPopup = new ItemDetailsPopup(
            _universalisService,
            _itemDataService,
            _priceTrackingService,
            _currencyTrackerService,
            _inventoryCacheService,
            _characterDataService);

        Title = "Websocket Feed";
        Size = new Vector2(450, 300);
    }

    public override void RenderToolContent()
    {
        try
        {
            // Connection status
            DrawConnectionStatus();

            ImGui.Separator();

            // Feed list
            using (ProfilerService.BeginStaticChildScope("DrawFeed"))
            {
                DrawFeed();
            }

            // Draw item details popup (renders on top when open)
            _itemDetailsPopup.Draw();
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[WebsocketFeedTool] Draw error: {ex.Message}");
        }
    }

    private void DrawConnectionStatus()
    {
        var isConnected = _webSocketService.IsConnected;

        // Status indicator with ball
        var indicatorColor = isConnected ? UiColors.Connected : UiColors.Disconnected;
        var icon = isConnected ? "●" : "○";
        var statusText = isConnected ? "Connected" : "Disconnected";

        ImGui.TextColored(indicatorColor, icon);
        ImGui.SameLine();
        ImGui.TextUnformatted(statusText);

        if (!PriceSettings.Enabled)
        {
            ImGui.SameLine();
            ImGui.TextColored(UiColors.Warning, "[Price Tracking Disabled]");
        }
    }

    private void DrawFeed()
    {
        var settings = Settings;
        var entries = _webSocketService.LiveFeed.ToList();

        // Apply world filter based on scope mode
        if (settings.FilterScopeMode != PriceTrackingScopeMode.All)
        {
            var effectiveWorldIds = GetEffectiveFilterWorldIds();
            if (effectiveWorldIds.Count > 0)
            {
                entries = entries.Where(e => effectiveWorldIds.Contains(e.WorldId)).ToList();
            }
        }

        // Apply item filter
        if (settings.FilterItemId > 0)
        {
            entries = entries.Where(e => e.ItemId == settings.FilterItemId).ToList();
        }
        if (!settings.ShowListingsAdd)
        {
            entries = entries.Where(e => e.EventType != "Listing Added").ToList();
        }
        if (!settings.ShowListingsRemove)
        {
            entries = entries.Where(e => e.EventType != "Listing Removed").ToList();
        }
        if (!settings.ShowSales)
        {
            entries = entries.Where(e => e.EventType != "Sale").ToList();
        }

        // Limit entries
        entries = entries.TakeLast(settings.MaxEntries).ToList();

        // Reverse order if latest on top
        if (settings.LatestOnTop)
        {
            entries.Reverse();
        }

        // Child window for scrolling
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        if (ImGui.BeginChild("##PriceFeed", new Vector2(0, availableHeight), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            if (entries.Count == 0)
            {
                ImGui.TextDisabled("No price updates yet...");
            }
            else
            {
                // Auto-scroll to top if latest on top
                if (settings.AutoScroll && settings.LatestOnTop)
                {
                    ImGui.SetScrollY(0);
                }

                foreach (var entry in entries)
                {
                    DrawFeedEntry(entry);
                }

                // Auto-scroll to bottom if latest on bottom
                if (settings.AutoScroll && !settings.LatestOnTop)
                {
                    ImGui.SetScrollHereY(1.0f);
                }
            }
            ImGui.EndChild();
        }
    }

    private void DrawFeedEntry(PriceFeedEntry entry)
    {
        // Get world name
        var worldName = _priceTrackingService.WorldData?.GetWorldName(entry.WorldId) ?? $"World {entry.WorldId}";
        
        // Get item name
        var itemName = _itemDataService.GetItemName(entry.ItemId);
        
        // Event type color
        var eventColor = entry.EventType switch
        {
            "Listing Added" => new Vector4(0.3f, 0.9f, 0.3f, 1f),    // Green for new listings
            "Listing Removed" => new Vector4(0.9f, 0.6f, 0.3f, 1f), // Orange for removed
            "Sale" => new Vector4(0.3f, 0.7f, 0.9f, 1f),            // Blue for sales
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1f)
        };

        var eventIcon = entry.EventType switch
        {
            "Listing Added" => "+",
            "Listing Removed" => "-",
            "Sale" => "$",
            _ => "?"
        };

        // Format: [Time] [Event] ItemName x Qty @ Price (World)
        var timeStr = entry.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
        var hqStr = entry.IsHq ? " HQ" : "";
        var priceStr = FormatUtils.FormatGil(entry.PricePerUnit);
        var totalStr = FormatUtils.FormatGil(entry.Total);

        // Make the entire entry clickable to open item details
        var entryText = $"{timeStr} {eventIcon} {itemName}{hqStr} x{entry.Quantity} @ {priceStr} ({totalStr} total) - {worldName}";
        var cursorPos = ImGui.GetCursorPos();
        
        // Draw the selectable (invisible, just for click detection)
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.3f, 0.3f, 0.3f, 0.4f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.4f, 0.4f, 0.4f, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.5f, 0.5f, 0.5f, 0.8f));
        
        if (ImGui.Selectable($"##{entry.ReceivedAt.Ticks}_{entry.ItemId}", false, ImGuiSelectableFlags.SpanAllColumns))
        {
            // Open the item details popup via Universalis API
            _itemDetailsPopup.Open(entry.ItemId);
        }
        
        ImGui.PopStyleColor(3);

        // Show tooltip hint on hover
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to view market listings and sales history (via Universalis API)");
        }

        // Draw the actual text on top of the selectable
        ImGui.SetCursorPos(cursorPos);
        ImGui.TextDisabled(timeStr);
        ImGui.SameLine();
        ImGui.TextColored(eventColor, eventIcon);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{itemName}{hqStr} x{entry.Quantity} @ {priceStr} ({totalStr} total) - {worldName}");
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        try
        {
            if (!MTTreeHelpers.DrawCollapsingSection("Websocket Feed Settings", true))
                return;
                
            var settings = Settings;

            // Max entries
            var maxEntries = settings.MaxEntries;
            if (ImGui.SliderInt("Max entries", ref maxEntries, 10, 500))
            {
                settings.MaxEntries = maxEntries;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Maximum number of entries to display in the feed.", "100");

            // Auto-scroll
            var autoScroll = settings.AutoScroll;
            if (ImGui.Checkbox("Auto-scroll to latest", ref autoScroll))
            {
                settings.AutoScroll = autoScroll;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Automatically scroll to the newest entry.", "On");

            // Latest on top
            var latestOnTop = settings.LatestOnTop;
            if (ImGui.Checkbox("Latest on top", ref latestOnTop))
            {
                settings.LatestOnTop = latestOnTop;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Show newest entries at the top of the list instead of the bottom.", "Off");

            ImGui.Spacing();
            ImGui.TextUnformatted("Event Filters");
            ImGui.Separator();

            var showListingsAdd = settings.ShowListingsAdd;
            if (ImGui.Checkbox("Show listings added", ref showListingsAdd))
            {
                settings.ShowListingsAdd = showListingsAdd;
                NotifyToolSettingsChanged();
            }

            var showListingsRemove = settings.ShowListingsRemove;
            if (ImGui.Checkbox("Show listings removed", ref showListingsRemove))
            {
                settings.ShowListingsRemove = showListingsRemove;
                NotifyToolSettingsChanged();
            }

            var showSales = settings.ShowSales;
            if (ImGui.Checkbox("Show sales", ref showSales))
            {
                settings.ShowSales = showSales;
                NotifyToolSettingsChanged();
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Filters");
            ImGui.Separator();

            // World filter using full WorldSelectionWidget
            DrawWorldFilterWidget(settings);

            // Item filter
            var filterItem = settings.FilterItemId;
            if (ImGui.InputInt("Filter by Item ID (0 = all)", ref filterItem))
            {
                settings.FilterItemId = filterItem;
                NotifyToolSettingsChanged();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[WebsocketFeedTool] Settings error: {ex.Message}");
        }
    }

    private void DrawWorldFilterWidget(WebsocketFeedSettings settings)
    {
        var worldData = _priceTrackingService.WorldData;

        if (worldData == null || worldData.DataCenters.Count == 0)
        {
            ImGui.TextDisabled("World data not loaded...");
            return;
        }

        // Create widget if needed
        if (_worldSelectionWidget == null)
        {
            _worldSelectionWidget = new WorldSelectionWidget(worldData, "LiveFeedWorldFilter");
            _worldSelectionWidget.Width = 250f;
            _worldSelectionWidget.MaxPopupHeight = 250f;
        }

        // Initialize widget from settings on first draw
        if (!_worldSelectionWidgetInitialized)
        {
            _worldSelectionWidget.InitializeFrom(
                settings.FilterRegions,
                settings.FilterDataCenters,
                settings.FilterWorldIds);

            // Set the mode based on current scope mode
            _worldSelectionWidget.Mode = settings.FilterScopeMode switch
            {
                PriceTrackingScopeMode.ByRegion => WorldSelectionMode.Regions,
                PriceTrackingScopeMode.ByDataCenter => WorldSelectionMode.DataCenters,
                PriceTrackingScopeMode.ByWorld => WorldSelectionMode.Worlds,
                _ => WorldSelectionMode.Worlds
            };

            _worldSelectionWidgetInitialized = true;
        }

        // "All" checkbox to disable filtering entirely
        var filterAll = settings.FilterScopeMode == PriceTrackingScopeMode.All;
        if (ImGui.Checkbox("Show all worlds", ref filterAll))
        {
            settings.FilterScopeMode = filterAll ? PriceTrackingScopeMode.All : PriceTrackingScopeMode.ByWorld;
            NotifyToolSettingsChanged();
        }
        ShowSettingTooltip("When enabled, shows events from all worlds without filtering.", "On");

        // Only show world selection widget if not "All"
        if (!filterAll)
        {
            if (_worldSelectionWidget.Draw("Filter Worlds##LiveFeedFilter"))
            {
                // Sync widget selections back to settings
                settings.FilterRegions.Clear();
                foreach (var r in _worldSelectionWidget.SelectedRegions)
                    settings.FilterRegions.Add(r);

                settings.FilterDataCenters.Clear();
                foreach (var dc in _worldSelectionWidget.SelectedDataCenters)
                    settings.FilterDataCenters.Add(dc);

                settings.FilterWorldIds.Clear();
                foreach (var w in _worldSelectionWidget.SelectedWorldIds)
                    settings.FilterWorldIds.Add(w);

                // Update scope mode based on widget mode
                settings.FilterScopeMode = _worldSelectionWidget.Mode switch
                {
                    WorldSelectionMode.Regions => PriceTrackingScopeMode.ByRegion,
                    WorldSelectionMode.DataCenters => PriceTrackingScopeMode.ByDataCenter,
                    WorldSelectionMode.Worlds => PriceTrackingScopeMode.ByWorld,
                    _ => PriceTrackingScopeMode.ByWorld
                };

                NotifyToolSettingsChanged();
            }

            // Show warning if nothing selected
            var hasSelection = _worldSelectionWidget.Mode switch
            {
                WorldSelectionMode.Regions => _worldSelectionWidget.SelectedRegions.Count > 0,
                WorldSelectionMode.DataCenters => _worldSelectionWidget.SelectedDataCenters.Count > 0,
                WorldSelectionMode.Worlds => _worldSelectionWidget.SelectedWorldIds.Count > 0,
                _ => false
            };

            if (!hasSelection)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "No worlds selected - no events will be shown!");
            }
        }

        ImGui.Spacing();
    }

    /// <summary>
    /// Gets the effective world IDs to filter by based on current settings.
    /// </summary>
    private HashSet<int> GetEffectiveFilterWorldIds()
    {
        var settings = Settings;
        var worldData = _priceTrackingService.WorldData;

        if (worldData == null) return new HashSet<int>();

        var result = new HashSet<int>();

        switch (settings.FilterScopeMode)
        {
            case PriceTrackingScopeMode.ByRegion:
                foreach (var region in settings.FilterRegions)
                {
                    foreach (var dc in worldData.GetDataCentersForRegion(region))
                    {
                        if (dc.Worlds != null)
                        {
                            foreach (var wid in dc.Worlds)
                                result.Add(wid);
                        }
                    }
                }
                break;

            case PriceTrackingScopeMode.ByDataCenter:
                foreach (var dcName in settings.FilterDataCenters)
                {
                    var dc = worldData.DataCenters.FirstOrDefault(d => d.Name == dcName);
                    if (dc?.Worlds != null)
                    {
                        foreach (var wid in dc.Worlds)
                            result.Add(wid);
                    }
                }
                break;

            case PriceTrackingScopeMode.ByWorld:
                foreach (var wid in settings.FilterWorldIds)
                    result.Add(wid);
                break;
        }

        return result;
    }

    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var s = Settings;
        return new Dictionary<string, object?>
        {
            ["MaxEntries"] = s.MaxEntries,
            ["ShowListingsAdd"] = s.ShowListingsAdd,
            ["ShowListingsRemove"] = s.ShowListingsRemove,
            ["ShowSales"] = s.ShowSales,
            ["AutoScroll"] = s.AutoScroll,
            ["LatestOnTop"] = s.LatestOnTop,
            ["FilterScopeMode"] = (int)s.FilterScopeMode,
            ["FilterRegions"] = s.FilterRegions.ToList(),
            ["FilterDataCenters"] = s.FilterDataCenters.ToList(),
            ["FilterWorldIds"] = s.FilterWorldIds.ToList(),
            ["FilterItemId"] = s.FilterItemId
        };
    }

    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        var s = Settings;
        s.MaxEntries = GetSetting(settings, "MaxEntries", s.MaxEntries);
        s.ShowListingsAdd = GetSetting(settings, "ShowListingsAdd", s.ShowListingsAdd);
        s.ShowListingsRemove = GetSetting(settings, "ShowListingsRemove", s.ShowListingsRemove);
        s.ShowSales = GetSetting(settings, "ShowSales", s.ShowSales);
        s.AutoScroll = GetSetting(settings, "AutoScroll", s.AutoScroll);
        s.LatestOnTop = GetSetting(settings, "LatestOnTop", s.LatestOnTop);
        s.FilterScopeMode = (PriceTrackingScopeMode)GetSetting(settings, "FilterScopeMode", (int)s.FilterScopeMode);
        s.FilterItemId = GetSetting(settings, "FilterItemId", s.FilterItemId);

        // Restore HashSet properties from lists (handles JsonElement from deserialization)
        if (settings.TryGetValue("FilterRegions", out var regionsObj) && regionsObj != null)
        {
            if (regionsObj is System.Text.Json.JsonElement regionsJson && regionsJson.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                s.FilterRegions = new HashSet<string>(regionsJson.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => !string.IsNullOrEmpty(v)));
            }
        }
        if (settings.TryGetValue("FilterDataCenters", out var dcObj) && dcObj != null)
        {
            if (dcObj is System.Text.Json.JsonElement dcJson && dcJson.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                s.FilterDataCenters = new HashSet<string>(dcJson.EnumerateArray().Select(v => v.GetString() ?? "").Where(v => !string.IsNullOrEmpty(v)));
            }
        }
        if (settings.TryGetValue("FilterWorldIds", out var worldsObj) && worldsObj != null)
        {
            if (worldsObj is System.Text.Json.JsonElement worldsJson && worldsJson.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                s.FilterWorldIds = new HashSet<int>(worldsJson.EnumerateArray().Select(v => v.GetInt32()));
            }
        }

        // Reset widget initialization flag so it picks up imported settings
        _worldSelectionWidgetInitialized = false;
    }

    public override void Dispose()
    {
        // No resources to dispose
    }
}
