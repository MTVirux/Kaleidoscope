using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
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

    private static readonly string[] EventTypeFilters = { "All Events", "Listings Added", "Listings Removed", "Sales" };

    private LivePriceFeedSettings Settings => _configService.Config.LivePriceFeed;
    private PriceTrackingSettings PriceSettings => _configService.Config.PriceTracking;

    public LivePriceFeedTool(
        UniversalisWebSocketService webSocketService,
        PriceTrackingService priceTrackingService,
        ConfigurationService configService)
    {
        _webSocketService = webSocketService;
        _priceTrackingService = priceTrackingService;
        _configService = configService;

        Title = "Live Price Feed";
        Size = new Vector2(450, 300);
        ScrollbarVisible = true;
    }

    public override void DrawContent()
    {
        try
        {
            // Connection status
            DrawConnectionStatus();

            ImGui.Separator();

            // Feed list
            DrawFeed();
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

        // Apply filters
        if (settings.FilterWorldId > 0)
        {
            entries = entries.Where(e => e.WorldId == settings.FilterWorldId).ToList();
        }
        if (settings.FilterItemId > 0)
        {
            entries = entries.Where(e => e.ItemId == settings.FilterItemId).ToList();
        }
        if (!settings.ShowListingsAdd)
        {
            entries = entries.Where(e => e.EventType != "listings/add").ToList();
        }
        if (!settings.ShowListingsRemove)
        {
            entries = entries.Where(e => e.EventType != "listings/remove").ToList();
        }
        if (!settings.ShowSales)
        {
            entries = entries.Where(e => e.EventType != "sales/add").ToList();
        }

        // Limit entries
        entries = entries.TakeLast(settings.MaxEntries).ToList();

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
                foreach (var entry in entries)
                {
                    DrawFeedEntry(entry);
                }

                // Auto-scroll to bottom
                if (settings.AutoScroll && entries.Count > 0)
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
        
        // Event type color
        var eventColor = entry.EventType switch
        {
            "listings/add" => new Vector4(0.3f, 0.9f, 0.3f, 1f),    // Green for new listings
            "listings/remove" => new Vector4(0.9f, 0.6f, 0.3f, 1f), // Orange for removed
            "sales/add" => new Vector4(0.3f, 0.7f, 0.9f, 1f),       // Blue for sales
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1f)
        };

        var eventIcon = entry.EventType switch
        {
            "listings/add" => "+",
            "listings/remove" => "-",
            "sales/add" => "$",
            _ => "?"
        };

        // Format: [Time] [Event] ItemId x Qty @ Price (World)
        var timeStr = entry.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
        var hqStr = entry.IsHq ? " HQ" : "";
        var priceStr = FormatGil(entry.PricePerUnit);
        var totalStr = FormatGil(entry.Total);

        ImGui.TextDisabled(timeStr);
        ImGui.SameLine();
        ImGui.TextColored(eventColor, eventIcon);
        ImGui.SameLine();
        ImGui.TextUnformatted($"Item #{entry.ItemId}{hqStr} x{entry.Quantity} @ {priceStr} ({totalStr} total) - {worldName}");
    }

    private static string FormatGil(long amount)
    {
        if (amount >= 1_000_000)
            return $"{amount / 1_000_000.0:F1}M";
        if (amount >= 1_000)
            return $"{amount / 1_000.0:F1}K";
        return amount.ToString("N0");
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

            // World filter
            var filterWorld = settings.FilterWorldId;
            if (ImGui.InputInt("Filter by World ID (0 = all)", ref filterWorld))
            {
                settings.FilterWorldId = filterWorld;
                _configService.Save();
            }

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

    public override void Dispose()
    {
        // No resources to dispose
    }
}
