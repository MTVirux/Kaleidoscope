using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Gui.Widgets.Combo;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets.Tree;
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.MainWindow.Tools.PriceTracking;

/// <summary>
/// Tool component that shows sale history for a selected item from Universalis.
/// </summary>
[ToolType("ItemSalesHistory", "Item Sales History", "Universalis",
    "View sale history for any marketable item from Universalis",
    RequiredServices = new[] { typeof(PriceTrackingService), typeof(ItemDataService), typeof(SalePriceCacheService) })]
public sealed class ItemSalesHistoryTool : ToolComponent
{
    public override string ToolName => "Item Sales History";
    
    private readonly UniversalisService _universalisService;
    private readonly PriceTrackingService _priceTrackingService;
    private readonly ConfigurationService _configService;
    private readonly ItemDataService _itemDataService;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly SalePriceCacheService _salePriceCacheService;
    private readonly ItemComboDropdown _itemCombo;

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
    private bool _showActionButtons = true;

    // Cached filtered view + summary. The per-frame filter (including per-entry outlier lookups)
    // and the min/avg/max summary are rebuilt only when an input that affects them changes.
    private List<HistorySale>? _cachedFilteredEntries;
    private string _cachedSummary = string.Empty;
    private bool _hasCachedSummary;
    private uint _cacheSelectedItemId = uint.MaxValue;
    private MarketHistory? _cacheHistoryRef;
    private bool _cacheShowHqOnly;
    private bool _cacheShowNqOnly;
    private bool _cacheFilterByListing;
    private int _cacheThreshold = -1;
    private int _cacheMaxEntries = -1;

    public ItemSalesHistoryTool(
        UniversalisService universalisService,
        PriceTrackingService priceTrackingService,
        ConfigurationService configService,
        ItemDataService itemDataService,
        CurrencyTrackerService currencyTrackerService,
        SalePriceCacheService salePriceCacheService,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        FavoritesService favoritesService)
    {
        _universalisService = universalisService;
        _priceTrackingService = priceTrackingService;
        _configService = configService;
        _itemDataService = itemDataService;
        _currencyTrackerService = currencyTrackerService;
        _salePriceCacheService = salePriceCacheService;

        _itemCombo = new ItemComboDropdown(
            textureProvider,
            dataManager,
            favoritesService,
            priceTrackingService,
            "ItemSalesHistory",
            marketableOnly: true,
            configService: _configService,
            trackedDataRegistry: _currencyTrackerService.Registry,
            excludeCurrencies: true);

        Title = "Item Sales History";
        Size = new Vector2(450, 400);
    }

    public override void RenderToolContent()
    {
        try
        {
            if (_showActionButtons)
            {
                DrawItemSelector();
                ImGui.Separator();
                DrawFilters();
                ImGui.Separator();
            }
            using (ProfilerService.BeginStaticChildScope("DrawSalesHistory"))
            {
                DrawSalesHistory();
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(UiColors.ErrorText, $"Error: {ex.Message}");
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    private void DrawItemSelector()
    {
        ImGui.TextUnformatted("Select Item:");
        ImGui.SameLine();

        // Item picker with icons and favorites
        if (_itemCombo.Draw(250))
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
            _configService.MarkDirty();
        }
        if (ImGui.IsItemHovered())
        {
            var threshold = _configService.Config.PriceTracking.SaleDiscrepancyThreshold;
            var refType = _configService.Config.PriceTracking.UseMedianForReference ? "median" : "average";
            var filterType = _configService.Config.PriceTracking.UseStdDevFilter ? "std dev" : $"{threshold}%";
            ImGui.SetTooltip($"Ignore sales outside {filterType} threshold.\nReference = {refType}(lowest 5 listings, last 5 sales) per world.\nConfigure in Settings > Universalis.");
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
            ImGui.TextColored(UiColors.ErrorMessage, _errorMessage);
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

        if (FilteredHistoryNeedsRebuild())
        {
            RebuildFilteredHistory();

            var pts = _configService.Config.PriceTracking;
            _cacheSelectedItemId = _itemCombo.SelectedItemId;
            _cacheHistoryRef = _currentHistory;
            _cacheShowHqOnly = _showHqOnly;
            _cacheShowNqOnly = _showNqOnly;
            _cacheFilterByListing = pts.FilterSalesByListingPrice;
            _cacheThreshold = pts.SaleDiscrepancyThreshold;
            _cacheMaxEntries = _maxEntries;
        }

        var filteredEntries = _cachedFilteredEntries!;

        // Summary
        if (_hasCachedSummary)
        {
            ImGui.TextUnformatted(_cachedSummary);
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
                var timeAgo = FormatUtils.FormatTimeAgo(sale.SaleDateTime);
                ImGui.TextUnformatted(timeAgo);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(sale.SaleDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                }

                // Price (with HQ indicator)
                ImGui.TableNextColumn();
                var priceText = FormatUtils.FormatGil(sale.PricePerUnit);
                if (sale.IsHq)
                {
                    ImGui.TextColored(UiColors.HqPrice, priceText);
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
                ImGui.TextUnformatted(FormatUtils.FormatGil(total));

                // World
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(sale.WorldName ?? "Unknown");
            }

            ImGui.EndTable();
        }
    }

    private bool FilteredHistoryNeedsRebuild()
    {
        var pts = _configService.Config.PriceTracking;
        return _cachedFilteredEntries == null
            || _cacheSelectedItemId != _itemCombo.SelectedItemId
            || !ReferenceEquals(_cacheHistoryRef, _currentHistory)
            || _cacheShowHqOnly != _showHqOnly
            || _cacheShowNqOnly != _showNqOnly
            || _cacheFilterByListing != pts.FilterSalesByListingPrice
            || _cacheThreshold != pts.SaleDiscrepancyThreshold
            || _cacheMaxEntries != _maxEntries;
    }

    private void RebuildFilteredHistory()
    {
        var result = new List<HistorySale>();
        _cachedFilteredEntries = result;
        _hasCachedSummary = false;
        _cachedSummary = string.Empty;

        if (_currentHistory?.Entries == null)
            return;

        IEnumerable<HistorySale> entries = _currentHistory.Entries;
        if (_showHqOnly)
            entries = entries.Where(e => e.IsHq);
        else if (_showNqOnly)
            entries = entries.Where(e => !e.IsHq);

        // Filter by listing price discrepancy (configurable threshold).
        // Uses average of lowest listing and most recent sale for that world as reference.
        var priceTrackingSettings = _configService.Config.PriceTracking;
        if (priceTrackingSettings.FilterSalesByListingPrice && _itemCombo.SelectedItemId > 0)
        {
            var itemId = (int)_itemCombo.SelectedItemId;
            var listingsService = _priceTrackingService.ListingsService;
            var threshold = priceTrackingSettings.SaleDiscrepancyThreshold / 100.0;
            entries = entries.Where(e =>
            {
                if (!e.WorldId.HasValue) return true; // Keep entries without world info

                var listing = listingsService.GetListing(itemId, e.WorldId.Value);
                var listingPrice = listing != null ? (e.IsHq ? listing.MinPriceHq : listing.MinPriceNq) : 0;
                var recentSalePrice = _salePriceCacheService.GetMostRecentSalePriceForWorld(itemId, e.WorldId.Value, e.IsHq);

                var referencePrice = SaleOutlierFilter.ComputeReferencePrice(listingPrice, recentSalePrice);
                return !SaleOutlierFilter.IsRatioOutlier(e.PricePerUnit, referencePrice, threshold, out _);
            });
        }

        result.AddRange(entries.Take(_maxEntries));

        if (result.Count > 0)
        {
            var avgPrice = result.Average(e => e.PricePerUnit);
            var minPrice = result.Min(e => e.PricePerUnit);
            var maxPrice = result.Max(e => e.PricePerUnit);
            _cachedSummary = $"Showing {result.Count} sales | Avg: {FormatUtils.FormatGil(avgPrice)} | Min: {FormatUtils.FormatGil(minPrice)} | Max: {FormatUtils.FormatGil(maxPrice)}";
            _hasCachedSummary = true;
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
            LogDebug($"Fetch error: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        // Default max entries
        var maxEntries = _maxEntries;
        if (ImGui.SliderInt("Default Max Entries", ref maxEntries, 10, 500))
        {
            _maxEntries = maxEntries;
            NotifyToolSettingsChanged();
        }
        ImGui.TextDisabled("Number of sale entries to fetch and display.");

        ImGui.Spacing();

        if (ImGui.Checkbox("Show Action Buttons", ref _showActionButtons))
        {
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show or hide the item selector, quality filters, and refresh button.");
        }
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
            ["ShowNqOnly"] = _showNqOnly,
            ["ShowActionButtons"] = _showActionButtons
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
        _showActionButtons = GetSetting(settings, "ShowActionButtons", true);
    }

    public override void Dispose()
    {
        // No resources to dispose
    }
}
