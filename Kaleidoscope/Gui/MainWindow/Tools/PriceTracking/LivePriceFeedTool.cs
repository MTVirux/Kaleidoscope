using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that displays a live feed of Universalis WebSocket price updates.
/// Shows listings added/removed and sales as they happen in real-time.
/// </summary>
public class LivePriceFeedTool : ToolComponent
{
    private readonly UniversalisWebSocketService _webSocketService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly ConfigurationService _configService;
    private readonly ItemDataService _itemDataService;

    // World selection widget for filtering
    private WorldSelectionWidget? _worldSelectionWidget;
    private bool _worldSelectionWidgetInitialized = false;

    private static readonly string[] EventTypeFilters = { "All Events", "Listings Added", "Listings Removed", "Sales" };

    private LivePriceFeedSettings Settings => _configService.Config.LivePriceFeed;
    private PriceTrackingSettings PriceSettings => _configService.Config.PriceTracking;

    public LivePriceFeedTool(
        UniversalisWebSocketService webSocketService,
        PriceTrackingService priceTrackingService,
        ConfigurationService configService,
        ItemDataService itemDataService)
    {
        _webSocketService = webSocketService;
        _priceTrackingService = priceTrackingService;
        _configService = configService;
        _itemDataService = itemDataService;

        Title = "Live Price Feed";
        Size = new Vector2(450, 300);
    }

    public override void DrawContent()
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
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[LivePriceFeedTool] Draw error: {ex.Message}");
        }
    }

    private void DrawConnectionStatus()
    {
        var isConnected = _webSocketService.IsConnected;
        var feedCount = _webSocketService.LiveFeedCount;

        // Status indicator
        var statusColor = isConnected 
            ? new Vector4(0.3f, 0.9f, 0.3f, 1f) 
            : new Vector4(0.9f, 0.3f, 0.3f, 1f);
        var statusText = isConnected ? "Connected" : "Disconnected";

        ImGui.TextColored(statusColor, statusText);
        ImGui.SameLine();
        ImGui.TextDisabled($"({feedCount} events)");

        if (!PriceSettings.Enabled)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "[Price Tracking Disabled]");
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
            var settings = Settings;

            ImGui.TextUnformatted("Live Feed Settings");
            ImGui.Separator();

            // Max entries
            var maxEntries = settings.MaxEntries;
            if (ImGui.SliderInt("Max entries", ref maxEntries, 10, 500))
            {
                settings.MaxEntries = maxEntries;
                _configService.Save();
            }
            ShowSettingTooltip("Maximum number of entries to display in the feed.", "100");

            // Auto-scroll
            var autoScroll = settings.AutoScroll;
            if (ImGui.Checkbox("Auto-scroll to latest", ref autoScroll))
            {
                settings.AutoScroll = autoScroll;
                _configService.Save();
            }
            ShowSettingTooltip("Automatically scroll to the newest entry.", "On");

            // Latest on top
            var latestOnTop = settings.LatestOnTop;
            if (ImGui.Checkbox("Latest on top", ref latestOnTop))
            {
                settings.LatestOnTop = latestOnTop;
                _configService.Save();
            }
            ShowSettingTooltip("Show newest entries at the top of the list instead of the bottom.", "Off");

            ImGui.Spacing();
            ImGui.TextUnformatted("Event Filters");
            ImGui.Separator();

            var showListingsAdd = settings.ShowListingsAdd;
            if (ImGui.Checkbox("Show listings added", ref showListingsAdd))
            {
                settings.ShowListingsAdd = showListingsAdd;
                _configService.Save();
            }

            var showListingsRemove = settings.ShowListingsRemove;
            if (ImGui.Checkbox("Show listings removed", ref showListingsRemove))
            {
                settings.ShowListingsRemove = showListingsRemove;
                _configService.Save();
            }

            var showSales = settings.ShowSales;
            if (ImGui.Checkbox("Show sales", ref showSales))
            {
                settings.ShowSales = showSales;
                _configService.Save();
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
                _configService.Save();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[LivePriceFeedTool] Settings error: {ex.Message}");
        }
    }

    private void DrawWorldFilterWidget(LivePriceFeedSettings settings)
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
            _configService.Save();
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

                _configService.Save();
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

    public override void Dispose()
    {
        // No resources to dispose
    }
}
