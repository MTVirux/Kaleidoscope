using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// TopInventoryValueTool partial class containing item rendering, tooltips, and sales table logic.
/// </summary>
public partial class TopInventoryValueTool
{
    private void DrawGilRow()
    {
        var percentage = _totalValue > 0 ? (float)_gilValue / _totalValue * 100 : 0;
        
        // Gold color for gil
        ImGui.TextColored(new Vector4(1f, 0.84f, 0f, 1f), "●");
        ImGui.SameLine();
        ImGui.TextUnformatted("Gil");
        
        // Value and percentage (right-aligned, matching item rows)
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 120);
        ImGui.TextUnformatted($"{FormatUtils.FormatGil(_gilValue)} ({percentage:F1}%)");
    }

    private void DrawItemRow(int rank, (int ItemId, long Quantity, long Value, string Name, TopItemPriceInfo? PriceInfo) item)
    {
        var percentage = _totalValue > 0 ? (float)item.Value / _totalValue * 100 : 0;
        
        // Color gradient from green to yellow to orange based on rank (scales with MaxItems setting)
        var maxItems = Math.Max(1, Settings.MaxItems);
        var gradientStep = 0.33f / maxItems;
        var hue = Math.Max(0, 0.33f - (rank * gradientStep));
        var color = FormatUtils.HsvToRgb(hue, 0.8f, 0.9f);

        // Start invisible button for hover detection (covers the whole row)
        var cursorPos = ImGui.GetCursorPos();

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

        // Create an invisible button over the row for hover detection and click handling
        var endCursorPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(cursorPos);
        var rowHeight = endCursorPos.Y - cursorPos.Y;
        if (rowHeight < ImGui.GetTextLineHeight()) rowHeight = ImGui.GetTextLineHeight();
        ImGui.InvisibleButton($"##row_{item.ItemId}_{rank}", new Vector2(ImGui.GetContentRegionAvail().X, rowHeight));
        
        // Handle click to open item details popup
        if (ImGui.IsItemClicked())
        {
            _itemDetailsPopup.Open(item.ItemId);
        }
        
        ImGui.SetCursorPos(endCursorPos);

        // Show tooltip on hover
        if (ImGui.IsItemHovered())
        {
            // Trigger async fetch if needed
            EnsureTooltipDataLoaded(item.ItemId);
            DrawItemTooltip(item);
        }
    }

    private void EnsureTooltipDataLoaded(int itemId)
    {
        // Check if we have cached data that's still valid
        if (_tooltipCache.TryGetValue(itemId, out var cached))
        {
            // If loading or data is fresh enough, don't refetch
            if (cached.IsLoading || (DateTime.UtcNow - cached.FetchedAt).TotalMinutes < TooltipCacheExpiryMinutes)
                return;
        }

        // Mark as loading and start fetch
        _tooltipCache[itemId] = new TooltipMarketData(null, DateTime.UtcNow, IsLoading: true);
        _ = FetchTooltipDataAsync(itemId);
    }

    private async Task FetchTooltipDataAsync(int itemId)
    {
        try
        {
            // Check if sales channel is subscribed for the warning (quick, no await needed)
            string? warning = null;
            if (!_priceTrackingService.IsSocketConnected)
            {
                warning = "WebSocket not connected - prices may be stale";
            }
            else if (!_priceTrackingService.WebSocketService.IsSalesChannelSubscribed())
            {
                warning = "Sales channel not subscribed - enable in Universalis settings";
            }

            // Offload database query to background thread to avoid blocking UI
            var recentSales = await Task.Run(() =>
            {
                var dbSales = _currencyTrackerService.DbService.GetSaleRecords(itemId, limit: 5);
                return dbSales
                    .Select(s => new LocalSaleInfo(s.WorldId, s.PricePerUnit, s.Quantity, s.IsHq, s.Timestamp))
                    .ToList();
            }).ConfigureAwait(false);

            _tooltipCache[itemId] = new TooltipMarketData(recentSales, DateTime.UtcNow, Warning: warning);
        }
        catch (Exception ex)
        {
            LogDebug($"Error fetching tooltip data for item {itemId}: {ex.Message}");
            _tooltipCache[itemId] = new TooltipMarketData(null, DateTime.UtcNow, Warning: $"Error: {ex.Message}");
        }
    }

    private void DrawItemTooltip((int ItemId, long Quantity, long Value, string Name, TopItemPriceInfo? PriceInfo) item)
    {
        ImGui.BeginTooltip();
        
        // Item name header
        ImGui.TextUnformatted(item.Name);
        ImGui.Separator();

        // Basic price info
        if (item.PriceInfo != null)
        {
            ImGui.TextUnformatted($"Unit Price: {FormatUtils.FormatGil(item.PriceInfo.UnitPrice)}");
            ImGui.TextUnformatted($"Quantity: {item.Quantity:N0}");
            ImGui.TextUnformatted($"Total Value: {FormatUtils.FormatGil(item.Value)}");

            // World info
            var worldName = _priceTrackingService.WorldData?.GetWorldName(item.PriceInfo.WorldId);
            if (!string.IsNullOrEmpty(worldName))
            {
                ImGui.TextUnformatted($"Best Price From: {worldName}");
            }

            // Last updated
            var timeSince = DateTime.UtcNow - item.PriceInfo.LastUpdated;
            ImGui.TextDisabled($"Updated: {FormatUtils.FormatTimeAgo(timeSince)}");
        }

        ImGui.Spacing();

        // Check for cached tooltip data
        if (_tooltipCache.TryGetValue(item.ItemId, out var tooltipData))
        {
            if (tooltipData.IsLoading)
            {
                ImGui.TextDisabled("Loading sales data...");
            }
            else
            {
                // Show warning if present (e.g., websocket not connected or sales channel not subscribed)
                if (!string.IsNullOrEmpty(tooltipData.Warning))
                {
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), $"⚠ {tooltipData.Warning}");
                    ImGui.Spacing();
                }

                // Draw Recent Sales table
                DrawRecentSalesTable(tooltipData.RecentSales);
            }
        }
        else
        {
            ImGui.TextDisabled("Hover to load sales data...");
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Click to view full details");

        ImGui.EndTooltip();
    }

    private void DrawRecentSalesTable(List<LocalSaleInfo>? sales)
    {
        ImGui.TextUnformatted("Recent Sales:");
        
        if (sales == null || sales.Count == 0)
        {
            ImGui.TextDisabled("  No sales recorded");
            ImGui.TextDisabled("  (Enable WebSocket sales tracking to record sales)");
            return;
        }

        if (ImGui.BeginTable("##RecentSalesTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 25);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var sale in sales)
            {
                ImGui.TableNextRow();
                
                // HQ
                ImGui.TableNextColumn();
                if (sale.IsHq)
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.3f, 1f), "★");
                else
                    ImGui.TextDisabled("-");
                
                // Price
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatUtils.FormatGil(sale.PricePerUnit));
                
                // Qty
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sale.Quantity.ToString());
                
                // World + Time
                ImGui.TableNextColumn();
                var worldName = _priceTrackingService.WorldData?.GetWorldName(sale.WorldId) ?? $"World {sale.WorldId}";
                var timeAgo = FormatUtils.FormatTimeAgo(DateTime.UtcNow - sale.Timestamp);
                ImGui.TextUnformatted($"{worldName}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(timeAgo);
                }
            }

            ImGui.EndTable();
        }
    }
}
