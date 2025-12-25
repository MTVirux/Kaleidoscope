using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that shows sale history for a selected item from Universalis.
/// </summary>
public class ItemSalesHistoryTool : ToolComponent
{
    private readonly UniversalisService _universalisService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly ConfigurationService _configService;
    private readonly ItemDataService _itemDataService;
    private readonly SamplerService _samplerService;
    private readonly ItemIconCombo _itemCombo;

    // Convenience accessor for database service
    private KaleidoscopeDbService DbService => _samplerService.DbService;

    // State
    private MarketHistory? _currentHistory;
    private bool _isLoading = false;
    private string? _errorMessage;
    private uint _loadedItemId = 0;
    private DateTime _lastFetchTime = DateTime.MinValue;

    // Settings
    private int _maxEntries = 100;
    private bool _showHqOnly = false;
    private bool _showNqOnly = false;

    public ItemSalesHistoryTool(
        UniversalisService universalisService,
        PriceTrackingService priceTrackingService,
        ConfigurationService configService,
        ItemDataService itemDataService,
        SamplerService samplerService,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        FavoritesService favoritesService)
    {
        _universalisService = universalisService;
        _priceTrackingService = priceTrackingService;
        _configService = configService;
        _itemDataService = itemDataService;
        _samplerService = samplerService;

        _itemCombo = new ItemIconCombo(
            textureProvider,
            dataManager,
            favoritesService,
            priceTrackingService,
            "ItemSalesHistory",
            marketableOnly: true);

        Title = "Item Sales History";
        Size = new Vector2(450, 400);
    }

    public override void DrawContent()
    {
        try
        {
            DrawItemSelector();
            ImGui.Separator();
            DrawFilters();
            ImGui.Separator();
            using (ProfilerService.BeginStaticChildScope("DrawSalesHistory"))
            {
                DrawSalesHistory();
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[ItemSalesHistoryTool] Draw error: {ex.Message}");
        }
    }

    private void DrawItemSelector()
    {
        ImGui.TextUnformatted("Select Item:");
        ImGui.SameLine();

        // Item picker with icons and favorites
        if (_itemCombo.Draw(_itemCombo.SelectedItem?.Name ?? "Select item...", _itemCombo.SelectedItemId, 250, 300))
        {
            if (_itemCombo.SelectedItemId > 0)
            {
                _ = FetchHistoryAsync(_itemCombo.SelectedItemId);
            }
        }

        // Refresh button
        ImGui.SameLine();
        if (_itemCombo.SelectedItemId > 0)
        {
            if (ImGui.Button(_isLoading ? "..." : "↻"))
            {
                _ = FetchHistoryAsync(_itemCombo.SelectedItemId);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh sale history");
            }
        }

        // Show item info if selected
        if (_itemCombo.SelectedItemId > 0 && _currentHistory != null)
        {
            var velocityText = $"Sales/day: {_currentHistory.RegularSaleVelocity:F1}";
            if (_currentHistory.NqSaleVelocity > 0 || _currentHistory.HqSaleVelocity > 0)
            {
                velocityText += $" (NQ: {_currentHistory.NqSaleVelocity:F1}, HQ: {_currentHistory.HqSaleVelocity:F1})";
            }
            ImGui.TextDisabled(velocityText);
        }
    }

    private void DrawFilters()
    {
        // Entry count
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Max Entries", ref _maxEntries))
        {
            _maxEntries = Math.Clamp(_maxEntries, 10, 500);
        }

        ImGui.SameLine();

        // Quality filters
        if (ImGui.Checkbox("HQ Only", ref _showHqOnly))
        {
            if (_showHqOnly) _showNqOnly = false;
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("NQ Only", ref _showNqOnly))
        {
            if (_showNqOnly) _showHqOnly = false;
        }

        ImGui.SameLine();
        var filterByListing = _configService.Config.PriceTracking.FilterSalesByListingPrice;
        if (ImGui.Checkbox("Filter Outliers", ref filterByListing))
        {
            _configService.Config.PriceTracking.FilterSalesByListingPrice = filterByListing;
            _configService.Save();
        }
        if (ImGui.IsItemHovered())
        {
            var threshold = _configService.Config.PriceTracking.SaleDiscrepancyThreshold;
            ImGui.SetTooltip($"Ignore sales with {threshold}%+ discrepancy from current listing price on that world.\nConfigure threshold in Settings > Universalis.");
        }
    }

    private void DrawSalesHistory()
    {
        if (_isLoading)
        {
            ImGui.TextDisabled("Loading sale history...");
            return;
        }

        if (!string.IsNullOrEmpty(_errorMessage))
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), _errorMessage);
            return;
        }

        if (_currentHistory == null || _currentHistory.Entries == null || _currentHistory.Entries.Count == 0)
        {
            if (_itemCombo.SelectedItemId > 0)
            {
                ImGui.TextDisabled("No sale history found for this item.");
            }
            else
            {
                ImGui.TextDisabled("Select an item to view sale history.");
            }
            return;
        }

        // Filter entries
        var entries = _currentHistory.Entries.AsEnumerable();
        if (_showHqOnly)
            entries = entries.Where(e => e.IsHq);
        else if (_showNqOnly)
            entries = entries.Where(e => !e.IsHq);

        // Filter by listing price discrepancy (configurable threshold)
        // Uses average of lowest listing and most recent sale for that world as reference
        var priceTrackingSettings = _configService.Config.PriceTracking;
        if (priceTrackingSettings.FilterSalesByListingPrice && _itemCombo.SelectedItemId > 0)
        {
            var itemId = (int)_itemCombo.SelectedItemId;
            var listingsService = _priceTrackingService.ListingsService;
            var threshold = priceTrackingSettings.SaleDiscrepancyThreshold / 100.0;
            var minRatio = 1.0 - threshold;
            var maxRatio = 1.0 + threshold;
            entries = entries.Where(e =>
            {
                if (!e.WorldId.HasValue) return true; // Keep entries without world info
                
                var listing = listingsService.GetListing(itemId, e.WorldId.Value);
                var listingPrice = listing != null ? (e.IsHq ? listing.MinPriceHq : listing.MinPriceNq) : 0;
                var recentSalePrice = DbService.GetMostRecentSalePriceForWorld(itemId, e.WorldId.Value, e.IsHq);
                
                // Calculate reference price as average of listing and recent sale (if both available)
                double referencePrice;
                if (listingPrice > 0 && recentSalePrice > 0)
                    referencePrice = (listingPrice + recentSalePrice) / 2.0;
                else if (listingPrice > 0)
                    referencePrice = listingPrice;
                else if (recentSalePrice > 0)
                    referencePrice = recentSalePrice;
                else
                    return true; // No reference data available, keep entry
                
                // Check if sale price is within threshold of reference price (either direction)
                var ratio = e.PricePerUnit / referencePrice;
                return ratio >= minRatio && ratio <= maxRatio;
            });
        }

        var filteredEntries = entries.Take(_maxEntries).ToList();

        // Summary
        if (filteredEntries.Count > 0)
        {
            var avgPrice = filteredEntries.Average(e => e.PricePerUnit);
            var minPrice = filteredEntries.Min(e => e.PricePerUnit);
            var maxPrice = filteredEntries.Max(e => e.PricePerUnit);
            ImGui.TextUnformatted($"Showing {filteredEntries.Count} sales | Avg: {FormatGil(avgPrice)} | Min: {FormatGil(minPrice)} | Max: {FormatGil(maxPrice)}");
            ImGui.Spacing();
        }

        // Table header
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        var availHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginTable("SalesHistoryTable", 5, tableFlags, new Vector2(0, availHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var sale in filteredEntries)
            {
                ImGui.TableNextRow();

                // Time
                ImGui.TableNextColumn();
                var timeAgo = GetTimeAgo(sale.SaleDateTime);
                ImGui.TextUnformatted(timeAgo);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(sale.SaleDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                }

                // Price (with HQ indicator)
                ImGui.TableNextColumn();
                var priceText = FormatGil(sale.PricePerUnit);
                if (sale.IsHq)
                {
                    ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), priceText);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("High Quality");
                    }
                }
                else
                {
                    ImGui.TextUnformatted(priceText);
                }

                // Quantity
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sale.Quantity.ToString());

                // Total
                ImGui.TableNextColumn();
                var total = (long)sale.PricePerUnit * sale.Quantity;
                ImGui.TextUnformatted(FormatGil(total));

                // World
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sale.WorldName ?? "Unknown");
            }

            ImGui.EndTable();
        }
    }

    private async Task FetchHistoryAsync(uint itemId)
    {
        if (_isLoading) return;

        _isLoading = true;
        _errorMessage = null;

        try
        {
            var scope = _universalisService.GetConfiguredScope();
            if (string.IsNullOrEmpty(scope))
            {
                _errorMessage = "No world/DC configured in settings.";
                return;
            }

            _currentHistory = await _universalisService.GetHistoryAsync(scope, itemId, _maxEntries);
            _loadedItemId = itemId;
            _lastFetchTime = DateTime.UtcNow;

            if (_currentHistory == null)
            {
                _errorMessage = "Failed to fetch history from Universalis.";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error: {ex.Message}";
            LogService.Debug($"[ItemSalesHistoryTool] Fetch error: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static string FormatGil(double value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000:F1}M";
        if (value >= 1_000)
            return $"{value / 1_000:F1}K";
        return $"{value:N0}";
    }

    private static string GetTimeAgo(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;

        if (span.TotalMinutes < 1)
            return "Just now";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30)
            return $"{(int)(span.TotalDays / 7)}w ago";

        return dateTime.ToString("MMM d");
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        if (!ImGui.CollapsingHeader("Item Sales History Settings", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        // Default max entries
        var maxEntries = _maxEntries;
        if (ImGui.SliderInt("Default Max Entries", ref maxEntries, 10, 500))
        {
            _maxEntries = maxEntries;
        }
        ImGui.TextDisabled("Number of sale entries to fetch and display.");
    }
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["MaxEntries"] = _maxEntries,
            ["ShowHqOnly"] = _showHqOnly,
            ["ShowNqOnly"] = _showNqOnly
        };
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        _maxEntries = GetSetting(settings, "MaxEntries", _maxEntries);
        _showHqOnly = GetSetting(settings, "ShowHqOnly", _showHqOnly);
        _showNqOnly = GetSetting(settings, "ShowNqOnly", _showNqOnly);
    }

    public override void Dispose()
    {
        // No resources to dispose
    }
}
