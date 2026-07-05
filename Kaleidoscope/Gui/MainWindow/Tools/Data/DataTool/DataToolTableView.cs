using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using Kaleidoscope.Services.Characters;
using Kaleidoscope.Services.Inventory;
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// Owns the table view's cached data and rendering. Constructed by and delegated to from
/// <see cref="DataTool"/>, which retains the shared settings, data-source plumbing, and view-mode switch.
/// </summary>
internal sealed class DataToolTableView
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly ConfigurationService _configService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly AutoRetainerService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    private readonly ItemTableWidget _tableWidget;
    private readonly Func<(long timeSeries, long character, long resources)> _getCacheVersions;
    private readonly Action<string> _logDebug;

    // Completed table data, published by the background rebuild and consumed by the render thread.
    // Reference assignment/read is atomic; the draw path reads it into a local each frame.
    private volatile PreparedItemTableData? _cachedTableData;
    private (long timeSeries, long character, long resources) _lastCacheVersions;

    // Refresh triggers/state. _pendingRefresh is a settings/manual trigger that bypasses the version
    // debounce; version-driven refreshes are rate-limited to RefreshDebounce. Only one background
    // rebuild runs at a time; a trigger arriving mid-flight is remembered and run once afterwards.
    private volatile bool _pendingRefresh = true;
    private volatile bool _refreshInFlight;
    private bool _pendingVersionRefresh;
    private DateTime _lastRefreshLaunchUtc = DateTime.MinValue;
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromSeconds(2);

    private TimeSeriesCacheService CacheService => _currencyTrackerService.CacheService;
    private CharacterDataCacheService CharacterDataCache => _currencyTrackerService.CharacterDataCache;

    public DataToolTableView(
        CurrencyTrackerService currencyTrackerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService,
        AutoRetainerService? autoRetainerService,
        PriceTrackingService? priceTrackingService,
        ItemTableWidget tableWidget,
        Func<(long timeSeries, long character, long resources)> getCacheVersions,
        Action<string> logDebug)
    {
        _currencyTrackerService = currencyTrackerService;
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        _tableWidget = tableWidget;
        _getCacheVersions = getCacheVersions;
        _logDebug = logDebug;
    }

    /// <summary>The most recently prepared table data (read by the settings panel for merge/hide options).</summary>
    public PreparedItemTableData? CachedTableData => _cachedTableData;

    /// <summary>Marks the cached table data as stale so it is rebuilt on the next draw.</summary>
    public void RequestRefresh() => _pendingRefresh = true;

    /// <summary>
    /// Detects whether any upstream cache version advanced since this view last checked, comparing
    /// against a per-view snapshot so detection does not depend on the sibling view's draw cadence.
    /// </summary>
    private bool HasCacheVersionChanged()
    {
        var current = _getCacheVersions();
        if (current != _lastCacheVersions)
        {
            _lastCacheVersions = current;
            return true;
        }
        return false;
    }

    public void Draw(DataToolSettings settings)
    {
        using (ProfilerService.BeginStaticChildScope("TableView"))
        {
            MaybeStartRefresh(settings);

            // Render the most recently completed snapshot; the background rebuild swaps in new data.
            var data = _cachedTableData;
            using (ProfilerService.BeginStaticChildScope("DrawTable"))
            {
                _tableWidget.Draw(data, settings);
            }
        }
    }

    /// <summary>
    /// Decides on the render thread whether to launch a background rebuild, snapshots the inputs it
    /// needs (mutating settings collections and framework-thread-only AutoRetainer data), and runs the
    /// heavy DB work off the render thread. Only one rebuild runs at a time; a trigger arriving while a
    /// rebuild is in flight is remembered and run once afterwards. Version-driven refreshes are
    /// debounced; settings/manual refreshes (via <see cref="RequestRefresh"/>) bypass the debounce.
    /// </summary>
    private void MaybeStartRefresh(DataToolSettings settings)
    {
        if (HasCacheVersionChanged())
            _pendingVersionRefresh = true;

        if (_refreshInFlight)
            return;

        var versionRefreshDue = _pendingVersionRefresh &&
            (DateTime.UtcNow - _lastRefreshLaunchUtc) >= RefreshDebounce;
        if (!_pendingRefresh && !versionRefreshDue)
            return;

        var input = BuildTableRefreshInput(settings);

        _pendingRefresh = false;
        _pendingVersionRefresh = false;
        _lastRefreshLaunchUtc = DateTime.UtcNow;
        _refreshInFlight = true;

        _ = Task.Run(() =>
        {
            try
            {
                using (ProfilerService.BeginStaticChildScope("RefreshTableData"))
                {
                    var data = BuildTableData(input);
                    if (data != null)
                        _cachedTableData = data;
                }
            }
            catch (Exception ex)
            {
                _logDebug($"RefreshTableData error: {ex.Message}");
            }
            finally
            {
                _refreshInFlight = false;
            }
        });
    }

    /// <summary>
    /// Captures, on the render thread, an immutable snapshot of everything a rebuild reads: the
    /// mutable settings collections (copied so the background never enumerates a mutating list) and
    /// the AutoRetainer world map and registration order (IPC is framework-thread-only).
    /// </summary>
    private TableRefreshInput BuildTableRefreshInput(DataToolSettings settings)
    {
        var sg = settings.SpecialGrouping;
        var sgCopy = new SpecialGroupingSettings
        {
            Enabled = sg.Enabled,
            ActiveGrouping = sg.ActiveGrouping,
            EnabledElements = new HashSet<CrystalElement>(sg.EnabledElements),
            EnabledTiers = new HashSet<CrystalTier>(sg.EnabledTiers),
            AllGilEnabled = sg.AllGilEnabled,
            MergeGilCurrencies = sg.MergeGilCurrencies,
            AllCrystalsEnabled = sg.AllCrystalsEnabled
        };

        HashSet<ulong>? allowed = null;
        if (settings.UseCharacterFilter && settings.SelectedCharacterIds.Count > 0)
            allowed = settings.SelectedCharacterIds.ToHashSet();

        // AutoRetainer world map (CID -> world) is framework-thread-only IPC; read it here.
        var characterWorlds = new Dictionary<ulong, string>();
        if (_autoRetainerService != null && _autoRetainerService.IsAvailable)
        {
            foreach (var (_, world, _, cid) in _autoRetainerService.GetAllCharacterData())
            {
                if (!string.IsNullOrEmpty(world))
                    characterWorlds[cid] = world;
            }
        }

        // AutoRetainer registration order (used only by the AutoRetainer sort) is also IPC-bound.
        var sortOrder = _configService.Config.CharacterSortOrder;
        Dictionary<ulong, int>? arOrderLookup = null;
        if (sortOrder == CharacterSortOrder.AutoRetainer)
        {
            var arOrder = _autoRetainerService?.GetRegisteredCharacterIds();
            if (arOrder != null && arOrder.Count > 0)
            {
                arOrderLookup = new Dictionary<ulong, int>(arOrder.Count);
                for (var i = 0; i < arOrder.Count; i++)
                    arOrderLookup[arOrder[i]] = i;
            }
        }

        return new TableRefreshInput
        {
            Columns = settings.Columns.ToList(),
            SpecialGrouping = sgCopy,
            AllowedCharacters = allowed,
            IncludeRetainers = settings.IncludeRetainers,
            ShowRetainerBreakdown = settings.ShowRetainerBreakdown,
            CharacterWorlds = characterWorlds,
            SortOrder = sortOrder,
            ArOrderLookup = arOrderLookup
        };
    }

    /// <summary>
    /// Builds the prepared table data from a render-thread snapshot. Runs on a background thread:
    /// every service it touches (character/time-series/inventory caches, world data) is safe for
    /// concurrent reads, and all framework-thread-only inputs arrive pre-snapshotted in <paramref name="input"/>.
    /// </summary>
    private PreparedItemTableData? BuildTableData(TableRefreshInput input)
    {
        var allColumns = input.Columns;

        // Apply special grouping filter to get visible columns
        List<ItemColumnConfig> columns;
        using (ProfilerService.BeginStaticChildScope("ApplyGroupingFilter"))
        {
            columns = SpecialGroupingHelper.ApplySpecialGroupingFilter(allColumns, input.SpecialGrouping).ToList();
        }

        if (columns.Count == 0)
        {
            return new PreparedItemTableData
            {
                Rows = Array.Empty<ItemTableCharacterRow>(),
                Columns = columns
            };
        }

        // Get all character names with disambiguation (from cache, no DB access)
        IReadOnlyDictionary<ulong, string?> characterNames;
        IReadOnlyDictionary<ulong, string> disambiguatedNames;
        Dictionary<ulong, string?> gameNames;
        using (ProfilerService.BeginStaticChildScope("GetCharacterNames"))
        {
            characterNames = CharacterDataCache.GetAllCharacterNamesDict();
            disambiguatedNames = CharacterDataCache.GetDisambiguatedNames(characterNames.Keys);

            // Build game name lookup for IPC calls (e.g., Lifestream relog)
            var extendedNames = CharacterDataCache.GetAllCharacterNamesExtended();
            gameNames = extendedNames.ToDictionary(x => x.characterId, x => x.gameName);
        }
        var rows = new Dictionary<ulong, ItemTableCharacterRow>();

        // Get world data for DC/Region lookups (from PriceTrackingService; effectively immutable snapshot)
        var worldData = _priceTrackingService?.WorldData;

        // Character world info (CID -> world), snapshotted from AutoRetainer on the render thread
        var characterWorlds = input.CharacterWorlds;

        // Get character filter (if using multi-select)
        HashSet<ulong>? allowedCharacters = input.AllowedCharacters;

        // Initialize rows for all known characters (filtered if applicable)
        foreach (var (charId, name) in characterNames)
        {
            // Skip characters not in the allowed set (if filtering is enabled)
            if (allowedCharacters != null && !allowedCharacters.Contains(charId))
                continue;

            var displayName = disambiguatedNames.TryGetValue(charId, out var formatted)
                ? formatted : name ?? $"CID:{charId}";

            // Get world info for this character
            var charWorldName = characterWorlds.TryGetValue(charId, out var w) ? w : string.Empty;
            var dcName = !string.IsNullOrEmpty(charWorldName) ? worldData?.GetDataCenterForWorld(charWorldName)?.Name ?? string.Empty : string.Empty;
            var regionName = !string.IsNullOrEmpty(charWorldName) ? worldData?.GetRegionForWorld(charWorldName) ?? string.Empty : string.Empty;

            rows[charId] = new ItemTableCharacterRow
            {
                CharacterId = charId,
                Name = displayName,
                GameName = gameNames.TryGetValue(charId, out var gn) ? gn ?? displayName : displayName,
                WorldName = charWorldName,
                DataCenterName = dcName,
                RegionName = regionName,
                ItemCounts = new Dictionary<uint, long>()
            };
        }

        // Fetch inventories once for all item columns (cache-first, avoids per-column DB calls)
        List<Kaleidoscope.Models.Inventory.InventoryCacheEntry>? allInventories = null;
        var hasItemColumns = columns.Any(c => !c.IsCurrency);
        if (hasItemColumns && _inventoryCacheService != null)
        {
            using (ProfilerService.BeginStaticChildScope("GetAllInventories"))
            {
                allInventories = _inventoryCacheService.GetAllInventories();
            }
        }

        // Populate data for each column
        using (ProfilerService.BeginStaticChildScope("PopulateColumns"))
        {
            foreach (var column in columns)
            {
                if (column.IsCurrency)
                {
                    PopulateCurrencyData(column, rows);
                }
                else
                {
                    PopulateItemData(column, rows, input.IncludeRetainers, input.ShowRetainerBreakdown, allInventories);
                }
            }
        }

        // Apply gil merging if enabled
        if (input.SpecialGrouping.AllGilEnabled && input.SpecialGrouping.MergeGilCurrencies)
        {
            ApplyGilMerging(rows);
        }

        // Sort rows
        List<ItemTableCharacterRow> sortedRows;
        using (ProfilerService.BeginStaticChildScope("SortRows"))
        {
            sortedRows = SortRows(rows.Values, input.SortOrder, input.ArOrderLookup);
        }

        return new PreparedItemTableData
        {
            Rows = sortedRows,
            Columns = columns
        };
    }

    /// <summary>
    /// Applies the configured character sort order to freshly built rows on the background thread.
    /// Uses the AutoRetainer order snapshotted on the render thread rather than calling AutoRetainer
    /// IPC (which is framework-thread-only); alphabetical orders are name-only and delegate to
    /// <see cref="CharacterSortHelper"/> with no AutoRetainer dependency.
    /// </summary>
    private List<ItemTableCharacterRow> SortRows(
        IEnumerable<ItemTableCharacterRow> rows,
        CharacterSortOrder sortOrder,
        Dictionary<ulong, int>? arOrderLookup)
    {
        if (sortOrder == CharacterSortOrder.AutoRetainer && arOrderLookup != null)
        {
            // Registered characters first in AR order; the rest fall to the end, ordered by name.
            return rows
                .OrderBy(r => arOrderLookup.TryGetValue(r.CharacterId, out var order) ? order : int.MaxValue)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return CharacterSortHelper.SortByCharacter(
            rows,
            _configService,
            null,
            r => r.CharacterId,
            r => r.Name).ToList();
    }

    /// <summary>
    /// Immutable snapshot of the settings and framework-thread-only data a table rebuild needs,
    /// captured on the render thread so the background rebuild never enumerates a mutating settings
    /// collection or calls AutoRetainer IPC off the framework thread.
    /// </summary>
    private sealed class TableRefreshInput
    {
        public required IReadOnlyList<ItemColumnConfig> Columns { get; init; }
        public required SpecialGroupingSettings SpecialGrouping { get; init; }
        public required HashSet<ulong>? AllowedCharacters { get; init; }
        public required bool IncludeRetainers { get; init; }
        public required bool ShowRetainerBreakdown { get; init; }
        public required Dictionary<ulong, string> CharacterWorlds { get; init; }
        public required CharacterSortOrder SortOrder { get; init; }
        public required Dictionary<ulong, int>? ArOrderLookup { get; init; }
    }

    private void PopulateCurrencyData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        using (ProfilerService.BeginStaticChildScope("PopulateCurrency"))
        {
            try
            {
                var dataType = (TrackedDataType)column.Id;
                var variableName = dataType.ToString();

                using (ProfilerService.BeginStaticChildScope("CacheGetLatestValues"))
                {
                    // Cache-first: get latest values from TimeSeriesCacheService
                    var latestValues = CacheService.GetLatestValuesForVariable(variableName);

                    foreach (var (charId, value) in latestValues)
                    {
                        if (rows.TryGetValue(charId, out var row))
                        {
                            row.ItemCounts[column.Id] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug($"PopulateCurrencyData error: {ex.Message}");
            }
        }
    }

    private void PopulateItemData(
        ItemColumnConfig column,
        Dictionary<ulong, ItemTableCharacterRow> rows,
        bool includeRetainers,
        bool showRetainerBreakdown,
        List<Kaleidoscope.Models.Inventory.InventoryCacheEntry>? allInventories)
    {
        using (ProfilerService.BeginStaticChildScope("PopulateItem"))
        {
            try
            {
                if (allInventories == null) return;

                foreach (var cache in allInventories)
                {
                    if (!rows.TryGetValue(cache.CharacterId, out var row))
                        continue;

                    var count = cache.Items
                        .Where(i => i.ItemId == column.Id)
                        .Sum(i => (long)i.Quantity);

                    row.ItemCounts.TryAdd(column.Id, 0);

                    if (cache.SourceType == Kaleidoscope.Models.Inventory.InventorySourceType.Player)
                    {
                        // Always add player inventory to total
                        row.ItemCounts[column.Id] += count;

                        // If showing breakdown, also track player-only counts
                        if (showRetainerBreakdown)
                        {
                            row.PlayerItemCounts ??= new Dictionary<uint, long>();
                            row.PlayerItemCounts.TryAdd(column.Id, 0);
                            row.PlayerItemCounts[column.Id] += count;
                        }
                    }
                    else if (cache.SourceType == Kaleidoscope.Models.Inventory.InventorySourceType.Retainer)
                    {
                        if (cache.RetainerId == 0) continue;

                        // Add retainer inventory to total if includeRetainers is enabled
                        if (includeRetainers)
                        {
                            row.ItemCounts[column.Id] += count;
                        }

                        // If showing breakdown, always register the retainer so it appears as a tree
                        // child even when it has 0 of this item (e.g. freshly imported retainers).
                        if (showRetainerBreakdown)
                        {
                            var retainerKey = (cache.RetainerId, cache.Name ?? $"Retainer {cache.RetainerId}");
                            row.RetainerBreakdown ??= new Dictionary<(ulong, string), Dictionary<uint, long>>();

                            if (!row.RetainerBreakdown.TryGetValue(retainerKey, out var retainerCounts))
                            {
                                retainerCounts = new Dictionary<uint, long>();
                                row.RetainerBreakdown[retainerKey] = retainerCounts;
                            }

                            retainerCounts.TryAdd(column.Id, 0);
                            if (count > 0)
                                retainerCounts[column.Id] += count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug($"PopulateItemData error: {ex.Message}");
            }
        }
    }

    private void ApplyGilMerging(Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        var gilId = (uint)TrackedDataType.Gil;
        var fcGilId = (uint)TrackedDataType.FreeCompanyGil;
        var retainerGilId = (uint)TrackedDataType.RetainerGil;

        foreach (var row in rows.Values)
        {
            long totalGil = 0;

            if (row.ItemCounts.TryGetValue(gilId, out var gil))
                totalGil += gil;
            if (row.ItemCounts.TryGetValue(fcGilId, out var fcGil))
                totalGil += fcGil;
            if (row.ItemCounts.TryGetValue(retainerGilId, out var retainerGil))
                totalGil += retainerGil;

            row.ItemCounts[gilId] = totalGil;
        }
    }
}
