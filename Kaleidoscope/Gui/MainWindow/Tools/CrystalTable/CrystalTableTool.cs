using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.CrystalTable;

/// <summary>
/// Tool component that displays crystal counts (elements × tiers) for all characters in a table.
/// Shows shards, crystals, and clusters for each element across all tracked characters.
/// </summary>
public class CrystalTableTool : ToolComponent
{
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly InventoryChangeService? _inventoryChangeService;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    
    // Instance-specific settings (not shared with other tool instances)
    private readonly CrystalTableSettings _instanceSettings;

    // Element names and colors
    private static readonly string[] ElementNames = { "Fire", "Ice", "Wind", "Earth", "Lightning", "Water" };
    private static readonly string[] TierNames = { "Shard", "Crystal", "Cluster" };
    
    private static readonly Vector4[] ElementColors =
    {
        new(1.0f, 0.3f, 0.2f, 1.0f),  // Fire - red/orange
        new(0.4f, 0.7f, 1.0f, 1.0f),  // Ice - light blue
        new(0.3f, 0.9f, 0.5f, 1.0f),  // Wind - green
        new(0.8f, 0.6f, 0.3f, 1.0f),  // Earth - brown/tan
        new(0.7f, 0.3f, 0.9f, 1.0f),  // Lightning - purple
        new(0.3f, 0.5f, 1.0f, 1.0f)   // Water - blue
    };

    // Cached data
    private List<CharacterCrystalData> _characterData = new();
    private DateTime _lastRefresh = DateTime.MinValue;
    private const double RefreshIntervalSeconds = 5.0;
    private volatile bool _pendingRefresh = true;
    private CharacterSortOrder _cachedSortOrder;

    private CrystalTableSettings Settings => _instanceSettings;
    private KaleidoscopeDbService DbService => _samplerService.DbService;

    public CrystalTableTool(
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryChangeService? inventoryChangeService = null,
        AutoRetainerIpcService? autoRetainerService = null)
    {
        _samplerService = samplerService;
        _configService = configService;
        _inventoryChangeService = inventoryChangeService;
        _autoRetainerService = autoRetainerService;
        
        // Initialize instance-specific settings with defaults
        _instanceSettings = new CrystalTableSettings();

        Title = "Crystal Table";
        Size = new Vector2(500, 300);

        // Subscribe to inventory changes for auto-refresh
        if (_inventoryChangeService != null)
        {
            _inventoryChangeService.OnCrystalsChanged += OnCrystalsChanged;
        }
    }

    private void OnCrystalsChanged()
    {
        _pendingRefresh = true;
    }

    public override void Dispose()
    {
        if (_inventoryChangeService != null)
        {
            _inventoryChangeService.OnCrystalsChanged -= OnCrystalsChanged;
        }
        base.Dispose();
    }

    /// <summary>
    /// Represents crystal data for a single character.
    /// </summary>
    private class CharacterCrystalData
    {
        public ulong CharacterId { get; set; }
        public string Name { get; set; } = string.Empty;
        // [element, tier] = count
        public long[,] Crystals { get; set; } = new long[6, 3];
        
        public long GetElementTotal(int element) => Crystals[element, 0] + Crystals[element, 1] + Crystals[element, 2];
        public long GetTierTotal(int tier) => Enumerable.Range(0, 6).Sum(e => Crystals[e, tier]);
        public long GetGrandTotal() => Enumerable.Range(0, 6).Sum(e => GetElementTotal(e));
        
        public long GetFilteredElementTotal(int element, CrystalTableSettings settings)
        {
            long total = 0;
            for (int tier = 0; tier < 3; tier++)
            {
                if (settings.IsTierVisible(tier))
                    total += Crystals[element, tier];
            }
            return total;
        }
        
        public long GetFilteredTierTotal(int tier, CrystalTableSettings settings)
        {
            long total = 0;
            for (int element = 0; element < 6; element++)
            {
                if (settings.IsElementVisible(element))
                    total += Crystals[element, tier];
            }
            return total;
        }
    }

    private void RefreshData()
    {
        try
        {
            // Get all crystal data from the database using batch query
            var allCrystalData = DbService.GetAllPointsBatch("Crystal_", null);
            var characterNames = DbService.GetAllCharacterNamesDict();

            // Build character data dictionary
            var charDataDict = new Dictionary<ulong, CharacterCrystalData>();

            foreach (var (variableName, points) in allCrystalData)
            {
                if (!TryParseVariableName(variableName, out var element, out var tier)) continue;

                // Group by character and get the latest value for each
                var latestByChar = points
                    .GroupBy(p => p.characterId)
                    .Select(g => (charId: g.Key, value: g.OrderByDescending(p => p.timestamp).First().value));

                foreach (var (charId, value) in latestByChar)
                {
                    if (!charDataDict.TryGetValue(charId, out var charData))
                    {
                        var name = characterNames.TryGetValue(charId, out var n) ? n ?? $"CID:{charId}" : $"CID:{charId}";
                        charData = new CharacterCrystalData { CharacterId = charId, Name = name };
                        charDataDict[charId] = charData;
                    }

                    charData.Crystals[element, tier] = value;
                }
            }

            // Sort by configured order and store
            _characterData = CharacterSortHelper.SortByCharacter(
                charDataDict.Values,
                _configService,
                _autoRetainerService,
                c => c.CharacterId,
                c => c.Name).ToList();

            _lastRefresh = DateTime.UtcNow;
            _pendingRefresh = false;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[CrystalTableTool] RefreshData error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a crystal variable name to extract element and tier indices.
    /// </summary>
    private static bool TryParseVariableName(string variableName, out int element, out int tier)
    {
        element = -1;
        tier = -1;

        // Format: Crystal_{Element}_{Tier}
        if (!variableName.StartsWith("Crystal_")) return false;

        var parts = variableName.Split('_');
        if (parts.Length != 3) return false;

        element = Array.IndexOf(ElementNames, parts[1]);
        tier = Array.IndexOf(TierNames, parts[2]);

        return element >= 0 && tier >= 0;
    }

    public override void DrawContent()
    {
        try
        {
            // Check if sort order changed - force refresh
            var currentSortOrder = _configService.Config.CharacterSortOrder;
            if (_cachedSortOrder != currentSortOrder)
            {
                _cachedSortOrder = currentSortOrder;
                _pendingRefresh = true;
            }
            
            // Auto-refresh on pending changes or time interval
            if (_pendingRefresh || (DateTime.UtcNow - _lastRefresh).TotalSeconds > RefreshIntervalSeconds)
            {
                RefreshData();
            }

            if (_characterData.Count == 0)
            {
                ImGui.TextUnformatted("No crystal data yet. Data will appear as you play.");
                return;
            }

            var settings = Settings;

            // Determine table layout based on settings
            if (settings.GroupByElement && settings.GroupByTier)
            {
                DrawDetailedTable();
            }
            else if (settings.GroupByElement)
            {
                DrawByElementTable();
            }
            else
            {
                DrawByTierTable();
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[CrystalTableTool] Draw error: {ex.Message}");
        }
    }

    /// <summary>
    /// Draws a table grouped by element (columns: Character, then visible elements).
    /// Each element column shows the combined total of all visible tiers for that element.
    /// </summary>
    private void DrawByElementTable()
    {
        var settings = Settings;
        
        // Get visible elements
        var visibleElements = Enumerable.Range(0, 6).Where(e => settings.IsElementVisible(e)).ToList();
        if (visibleElements.Count == 0)
        {
            ImGui.TextUnformatted("No elements visible. Enable at least one element in settings.");
            return;
        }
        
        var columnCount = 1 + visibleElements.Count; // Character + visible elements

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable;

        if (!ImGui.BeginTable("CrystalTableByElement", columnCount, flags))
            return;

        try
        {
            // Setup columns
            ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.None, 120);
            foreach (var element in visibleElements)
            {
                ImGui.TableSetupColumn(ElementNames[element], ImGuiTableColumnFlags.None, 60);
            }
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            // Sort data if sortable
            var data = GetSortedData();

            // Draw rows
            foreach (var charData in data)
            {
                ImGui.TableNextRow();

                // Character name
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(charData.Name);

                // Element totals (filtered by visible tiers)
                foreach (var element in visibleElements)
                {
                    ImGui.TableNextColumn();
                    var total = charData.GetFilteredElementTotal(element, settings);
                    if (settings.ColorizeByElement)
                    {
                        ImGui.TextColored(ElementColors[element], FormatNumber(total));
                    }
                    else
                    {
                        ImGui.TextUnformatted(FormatNumber(total));
                    }
                }
            }

            // Total row if enabled
            if (settings.ShowTotalRow && data.Count > 1)
            {
                ImGui.TableNextRow();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f)));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted("TOTAL");

                foreach (var element in visibleElements)
                {
                    ImGui.TableNextColumn();
                    var sum = data.Sum(c => c.GetFilteredElementTotal(element, settings));
                    if (settings.ColorizeByElement)
                    {
                        ImGui.TextColored(ElementColors[element], FormatNumber(sum));
                    }
                    else
                    {
                        ImGui.TextUnformatted(FormatNumber(sum));
                    }
                }
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    /// <summary>
    /// Draws a table grouped by tier (columns: Character, then visible tiers).
    /// Each tier column shows the combined total of all visible elements for that tier.
    /// </summary>
    private void DrawByTierTable()
    {
        var settings = Settings;
        
        // Get visible tiers
        var visibleTiers = Enumerable.Range(0, 3).Where(t => settings.IsTierVisible(t)).ToList();
        if (visibleTiers.Count == 0)
        {
            ImGui.TextUnformatted("No tiers visible. Enable at least one tier in settings.");
            return;
        }
        
        var columnCount = 1 + visibleTiers.Count; // Character + visible tiers

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable;

        if (!ImGui.BeginTable("CrystalTableByTier", columnCount, flags))
            return;

        try
        {
            // Setup columns
            ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.None, 120);
            foreach (var tier in visibleTiers)
            {
                ImGui.TableSetupColumn(TierNames[tier] + "s", ImGuiTableColumnFlags.None, 70);
            }
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            // Sort data
            var data = GetSortedData();

            // Draw rows
            foreach (var charData in data)
            {
                ImGui.TableNextRow();

                // Character name
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(charData.Name);

                // Tier totals (filtered by visible elements)
                foreach (var tier in visibleTiers)
                {
                    ImGui.TableNextColumn();
                    var total = charData.GetFilteredTierTotal(tier, settings);
                    ImGui.TextUnformatted(FormatNumber(total));
                }
            }

            // Total row if enabled
            if (settings.ShowTotalRow && data.Count > 1)
            {
                ImGui.TableNextRow();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f)));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted("TOTAL");

                foreach (var tier in visibleTiers)
                {
                    ImGui.TableNextColumn();
                    var sum = data.Sum(c => c.GetFilteredTierTotal(tier, settings));
                    ImGui.TextUnformatted(FormatNumber(sum));
                }
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    /// <summary>
    /// Draws a detailed table with columns for each visible element×tier combination.
    /// Column order depends on SortColumnsByElement setting.
    /// </summary>
    private void DrawDetailedTable()
    {
        var settings = Settings;
        
        // Get visible elements and tiers
        var visibleElements = Enumerable.Range(0, 6).Where(e => settings.IsElementVisible(e)).ToList();
        var visibleTiers = Enumerable.Range(0, 3).Where(t => settings.IsTierVisible(t)).ToList();
        
        if (visibleElements.Count == 0 || visibleTiers.Count == 0)
        {
            ImGui.TextUnformatted("No data visible. Enable at least one element and one tier in settings.");
            return;
        }
        
        var columnCount = 1 + (visibleElements.Count * visibleTiers.Count);

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY;
        if (settings.Sortable) flags |= ImGuiTableFlags.Sortable;

        if (!ImGui.BeginTable("CrystalTableDetailed", columnCount, flags))
            return;

        try
        {
            // Setup columns - order depends on SortColumnsByElement
            ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.None, 100);
            
            if (settings.SortColumnsByElement)
            {
                // Element first: Fi-Sha, Fi-Cry, Fi-Clu, Ic-Sha, Ic-Cry, Ic-Clu, ...
                foreach (var element in visibleElements)
                {
                    foreach (var tier in visibleTiers)
                    {
                        var header = $"{ElementNames[element][..2]}-{TierNames[tier][..3]}";
                        ImGui.TableSetupColumn(header, ImGuiTableColumnFlags.None, 55);
                    }
                }
            }
            else
            {
                // Tier first: Fi-Sha, Ic-Sha, Wi-Sha, ..., Fi-Cry, Ic-Cry, ...
                foreach (var tier in visibleTiers)
                {
                    foreach (var element in visibleElements)
                    {
                        var header = $"{ElementNames[element][..2]}-{TierNames[tier][..3]}";
                        ImGui.TableSetupColumn(header, ImGuiTableColumnFlags.None, 55);
                    }
                }
            }
            
            ImGui.TableSetupScrollFreeze(1, 1);
            ImGui.TableHeadersRow();

            // Sort data
            var data = GetSortedData();

            // Draw rows
            foreach (var charData in data)
            {
                ImGui.TableNextRow();

                // Character name
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(charData.Name);

                // Each element × tier (order matches column setup)
                if (settings.SortColumnsByElement)
                {
                    foreach (var element in visibleElements)
                    {
                        foreach (var tier in visibleTiers)
                        {
                            DrawCrystalCell(charData.Crystals[element, tier], element, settings);
                        }
                    }
                }
                else
                {
                    foreach (var tier in visibleTiers)
                    {
                        foreach (var element in visibleElements)
                        {
                            DrawCrystalCell(charData.Crystals[element, tier], element, settings);
                        }
                    }
                }
            }

            // Total row if enabled
            if (settings.ShowTotalRow && data.Count > 1)
            {
                ImGui.TableNextRow();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f)));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted("TOTAL");

                if (settings.SortColumnsByElement)
                {
                    foreach (var element in visibleElements)
                    {
                        foreach (var tier in visibleTiers)
                        {
                            var sum = data.Sum(c => c.Crystals[element, tier]);
                            DrawCrystalCell(sum, element, settings);
                        }
                    }
                }
                else
                {
                    foreach (var tier in visibleTiers)
                    {
                        foreach (var element in visibleElements)
                        {
                            var sum = data.Sum(c => c.Crystals[element, tier]);
                            DrawCrystalCell(sum, element, settings);
                        }
                    }
                }
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    private void DrawCrystalCell(long value, int element, CrystalTableSettings settings)
    {
        ImGui.TableNextColumn();
        if (settings.ColorizeByElement)
        {
            ImGui.TextColored(ElementColors[element], FormatNumber(value));
        }
        else
        {
            ImGui.TextUnformatted(FormatNumber(value));
        }
    }

    private List<CharacterCrystalData> GetSortedData()
    {
        // Apply configured character sort order
        return CharacterSortHelper.SortByCharacter(
            _characterData,
            _configService,
            _autoRetainerService,
            c => c.CharacterId,
            c => c.Name).ToList();
    }

    private static string FormatNumber(long value)
    {
        return value >= 1_000_000 ? $"{value / 1_000_000.0:F1}M" :
               value >= 1_000 ? $"{value / 1_000.0:F1}K" :
               value.ToString("N0");
    }

    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        var settings = Settings;

        ImGui.TextUnformatted("Grouping Options");
        ImGui.Separator();

        var groupByElement = settings.GroupByElement;
        if (ImGui.Checkbox("Group by Element", ref groupByElement))
        {
            settings.GroupByElement = groupByElement;
            NotifyToolSettingsChanged();
        }
        ShowSettingTooltip("Shows columns for each element (Fire, Ice, etc.).", "On");

        var groupByTier = settings.GroupByTier;
        if (ImGui.Checkbox("Group by Tier", ref groupByTier))
        {
            settings.GroupByTier = groupByTier;
            NotifyToolSettingsChanged();
        }
        ShowSettingTooltip("Shows columns for each tier (Shard, Crystal, Cluster).", "Off");

        // Show mode explanation
        ImGui.TextDisabled(GetModeDescription(settings));

        // Column order option (only shown in detailed mode)
        if (settings.GroupByElement && settings.GroupByTier)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Column Order");
            ImGui.Separator();

            var sortByElement = settings.SortColumnsByElement;
            if (ImGui.RadioButton("Element first", sortByElement))
            {
                settings.SortColumnsByElement = true;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Groups columns by element: Fi-Sha, Fi-Cry, Fi-Clu, Ic-Sha, Ic-Cry, ...", "On");

            ImGui.SameLine();
            if (ImGui.RadioButton("Tier first", !sortByElement))
            {
                settings.SortColumnsByElement = false;
                NotifyToolSettingsChanged();
            }
            ShowSettingTooltip("Groups columns by tier: Fi-Sha, Ic-Sha, Wi-Sha, ..., Fi-Cry, Ic-Cry, ...", "Off");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Table Options");
        ImGui.Separator();

        var showTotalRow = settings.ShowTotalRow;
        if (ImGui.Checkbox("Show total row", ref showTotalRow))
        {
            settings.ShowTotalRow = showTotalRow;
            NotifyToolSettingsChanged();
        }
        ShowSettingTooltip("Shows a summary row at the bottom with totals across all characters.", "On");

        var colorize = settings.ColorizeByElement;
        if (ImGui.Checkbox("Colorize by element", ref colorize))
        {
            settings.ColorizeByElement = colorize;
            NotifyToolSettingsChanged();
        }
        ShowSettingTooltip("Colors the element values using their characteristic colors.", "On");

        var sortable = settings.Sortable;
        if (ImGui.Checkbox("Enable column sorting", ref sortable))
        {
            settings.Sortable = sortable;
            NotifyToolSettingsChanged();
        }
        ShowSettingTooltip("Allows clicking column headers to sort the table.", "On");

        ImGui.Spacing();
        ImGui.TextUnformatted("Element Filters");
        ImGui.Separator();

        // Element filter row 1
        var showFire = settings.ShowFire;
        if (ImGui.Checkbox("Fire", ref showFire))
        {
            settings.ShowFire = showFire;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        var showIce = settings.ShowIce;
        if (ImGui.Checkbox("Ice", ref showIce))
        {
            settings.ShowIce = showIce;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        var showWind = settings.ShowWind;
        if (ImGui.Checkbox("Wind", ref showWind))
        {
            settings.ShowWind = showWind;
            NotifyToolSettingsChanged();
        }

        // Element filter row 2
        var showEarth = settings.ShowEarth;
        if (ImGui.Checkbox("Earth", ref showEarth))
        {
            settings.ShowEarth = showEarth;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        var showLightning = settings.ShowLightning;
        if (ImGui.Checkbox("Lightning", ref showLightning))
        {
            settings.ShowLightning = showLightning;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        var showWater = settings.ShowWater;
        if (ImGui.Checkbox("Water", ref showWater))
        {
            settings.ShowWater = showWater;
            NotifyToolSettingsChanged();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Tier Filters");
        ImGui.Separator();

        var showShards = settings.ShowShards;
        if (ImGui.Checkbox("Shards", ref showShards))
        {
            settings.ShowShards = showShards;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        var showCrystals = settings.ShowCrystals;
        if (ImGui.Checkbox("Crystals", ref showCrystals))
        {
            settings.ShowCrystals = showCrystals;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        var showClusters = settings.ShowClusters;
        if (ImGui.Checkbox("Clusters", ref showClusters))
        {
            settings.ShowClusters = showClusters;
            NotifyToolSettingsChanged();
        }
    }

    private static string GetModeDescription(CrystalTableSettings settings)
    {
        if (settings.GroupByElement && settings.GroupByTier)
            return "Mode: Detailed (Element × Tier)";
        if (settings.GroupByElement)
            return "Mode: By Element (tiers combined)";
        return "Mode: By Tier (elements combined)";
    }
    
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var s = _instanceSettings;
        return new Dictionary<string, object?>
        {
            ["GroupByElement"] = s.GroupByElement,
            ["GroupByTier"] = s.GroupByTier,
            ["ShowTotalRow"] = s.ShowTotalRow,
            ["ColorizeByElement"] = s.ColorizeByElement,
            ["Sortable"] = s.Sortable,
            ["SortColumnsByElement"] = s.SortColumnsByElement,
            ["ShowFire"] = s.ShowFire,
            ["ShowIce"] = s.ShowIce,
            ["ShowWind"] = s.ShowWind,
            ["ShowEarth"] = s.ShowEarth,
            ["ShowLightning"] = s.ShowLightning,
            ["ShowWater"] = s.ShowWater,
            ["ShowShards"] = s.ShowShards,
            ["ShowCrystals"] = s.ShowCrystals,
            ["ShowClusters"] = s.ShowClusters
        };
    }
    
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        var t = _instanceSettings;
        
        t.GroupByElement = GetSetting(settings, "GroupByElement", t.GroupByElement);
        t.GroupByTier = GetSetting(settings, "GroupByTier", t.GroupByTier);
        t.ShowTotalRow = GetSetting(settings, "ShowTotalRow", t.ShowTotalRow);
        t.ColorizeByElement = GetSetting(settings, "ColorizeByElement", t.ColorizeByElement);
        t.Sortable = GetSetting(settings, "Sortable", t.Sortable);
        t.SortColumnsByElement = GetSetting(settings, "SortColumnsByElement", t.SortColumnsByElement);
        t.ShowFire = GetSetting(settings, "ShowFire", t.ShowFire);
        t.ShowIce = GetSetting(settings, "ShowIce", t.ShowIce);
        t.ShowWind = GetSetting(settings, "ShowWind", t.ShowWind);
        t.ShowEarth = GetSetting(settings, "ShowEarth", t.ShowEarth);
        t.ShowLightning = GetSetting(settings, "ShowLightning", t.ShowLightning);
        t.ShowWater = GetSetting(settings, "ShowWater", t.ShowWater);
        t.ShowShards = GetSetting(settings, "ShowShards", t.ShowShards);
        t.ShowCrystals = GetSetting(settings, "ShowCrystals", t.ShowCrystals);
        t.ShowClusters = GetSetting(settings, "ShowClusters", t.ShowClusters);
        
        _pendingRefresh = true;
    }
}
