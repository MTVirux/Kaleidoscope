using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Universalis;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.ItemTable;

/// <summary>
/// Tool component that displays a customizable table of items/currencies across characters.
/// Users can add items and currencies to track, customize column names and colors.
/// </summary>
public class ItemTableTool : ToolComponent
{
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly InventoryCacheService? _inventoryCacheService;
    private readonly TrackedDataRegistry? _trackedDataRegistry;
    private readonly ItemDataService? _itemDataService;
    private readonly IDataManager? _dataManager;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    private readonly PriceTrackingService? _priceTrackingService;
    
    private readonly ItemTableWidget _tableWidget;
    private readonly ItemIconCombo? _itemCombo;
    
    // Instance-specific settings (not shared with other tool instances)
    private readonly ItemTableSettings _instanceSettings;
    
    // Cached data
    private PreparedItemTableData? _cachedData;
    private DateTime _lastRefresh = DateTime.MinValue;
    private volatile bool _pendingRefresh = true;
    private CharacterNameFormat _cachedNameFormat;
    private CharacterSortOrder _cachedSortOrder;
    
    private ItemTableSettings Settings => _instanceSettings;
    private KaleidoscopeDbService DbService => _samplerService.DbService;
    private TimeSeriesCacheService CacheService => _samplerService.CacheService;
    
    public ItemTableTool(
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null,
        ITextureProvider? textureProvider = null,
        FavoritesService? favoritesService = null,
        AutoRetainerIpcService? autoRetainerService = null,
        PriceTrackingService? priceTrackingService = null)
    {
        _samplerService = samplerService;
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _trackedDataRegistry = trackedDataRegistry;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        _autoRetainerService = autoRetainerService;
        _priceTrackingService = priceTrackingService;
        
        // Initialize instance-specific settings with defaults
        _instanceSettings = new ItemTableSettings();
        
        Title = "Item Table";
        Size = new Vector2(500, 300);
        
        // Create the table widget
        _tableWidget = new ItemTableWidget(
            new ItemTableWidget.TableConfig
            {
                TableId = "CustomItemTable",
                NoDataText = "No data yet. Add items or currencies to track."
            },
            itemDataService,
            trackedDataRegistry,
            configService.Config,
            samplerService.CacheService);
        
        // Bind to instance-specific settings (not global config)
        _tableWidget.BindSettings(
            _instanceSettings,
            () => NotifyToolSettingsChanged(),
            "Table Settings");
        
        // Create item combo if we have the required services
        if (_dataManager != null && _itemDataService != null && textureProvider != null && favoritesService != null)
        {
            _itemCombo = new ItemIconCombo(
                textureProvider,
                _dataManager,
                favoritesService,
                null, // No price tracking service - include all items
                "ItemTableAdd",
                marketableOnly: false);
        }
        
        // Register widget as settings provider
        RegisterSettingsProvider(_tableWidget);
    }
    
    public override void DrawContent()
    {
        try
        {
            var settings = Settings;
            
            // Check if name format changed - force refresh
            var currentFormat = _configService.Config.CharacterNameFormat;
            if (_cachedNameFormat != currentFormat)
            {
                _cachedNameFormat = currentFormat;
                _pendingRefresh = true;
            }
            
            // Check if sort order changed - force refresh
            var currentSortOrder = _configService.Config.CharacterSortOrder;
            if (_cachedSortOrder != currentSortOrder)
            {
                _cachedSortOrder = currentSortOrder;
                _pendingRefresh = true;
            }
            
            // Auto-refresh every 0.5s
            var shouldAutoRefresh = (DateTime.UtcNow - _lastRefresh).TotalSeconds > 0.5;
            
            if (_pendingRefresh || shouldAutoRefresh)
            {
                RefreshData();
            }
            
            // Draw action buttons (if enabled)
            if (settings.ShowActionButtons)
            {
                DrawActionButtons();
                ImGui.Separator();
            }
            
            // Draw the table
            _tableWidget.Draw(_cachedData, settings);
            
            // Draw popups for adding items/currencies
            DrawAddItemPopup();
            DrawAddCurrencyPopup();
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {ex.Message}");
            LogService.Debug($"[ItemTableTool] Draw error: {ex.Message}");
        }
    }
    
    private void DrawActionButtons()
    {
        if (ImGui.Button("+ Add Item"))
        {
            ImGui.OpenPopup("AddItemPopup");
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("+ Add Currency"))
        {
            ImGui.OpenPopup("AddCurrencyPopup");
        }
        
        // Show column count
        var columnCount = Settings.Columns.Count;
        ImGui.SameLine();
        ImGui.TextDisabled($"({columnCount} column{(columnCount != 1 ? "s" : "")})");
    }
    
    private void DrawAddItemPopup()
    {
        if (ImGui.BeginPopup("AddItemPopup"))
        {
            ImGui.TextUnformatted("Add Item Column");
            ImGui.Separator();
            
            if (_itemCombo != null)
            {
                if (_itemCombo.Draw(_itemCombo.SelectedItem?.Name ?? "Select item...", _itemCombo.SelectedItemId, 250, 300))
                {
                    // Item selected
                    if (_itemCombo.SelectedItemId > 0)
                    {
                        AddColumn(_itemCombo.SelectedItemId, isCurrency: false);
                        _itemCombo.ClearSelection();
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("Item picker not available.");
            }
            
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.EndPopup();
        }
    }
    
    private void DrawAddCurrencyPopup()
    {
        if (ImGui.BeginPopup("AddCurrencyPopup"))
        {
            ImGui.TextUnformatted("Add Currency Column");
            ImGui.Separator();
            
            if (_trackedDataRegistry != null)
            {
                // Group currencies by category
                var categories = _trackedDataRegistry.Definitions.Values
                    .GroupBy(d => d.Category)
                    .OrderBy(g => g.Key);
                
                foreach (var category in categories)
                {
                    if (ImGui.TreeNode($"{category.Key}##cat"))
                    {
                        foreach (var def in category.OrderBy(d => d.DisplayName))
                        {
                            // Check if already added
                            var alreadyAdded = Settings.Columns.Any(c => c.IsCurrency && c.Id == (uint)def.Type);
                            
                            if (alreadyAdded)
                            {
                                ImGui.TextDisabled($"✓ {def.DisplayName}");
                            }
                            else if (ImGui.Selectable(def.DisplayName))
                            {
                                AddColumn((uint)def.Type, isCurrency: true);
                                ImGui.CloseCurrentPopup();
                            }
                        }
                        ImGui.TreePop();
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("Currency registry not available.");
            }
            
            ImGui.Spacing();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.EndPopup();
        }
    }
    
    private void AddColumn(uint id, bool isCurrency)
    {
        // Check if already exists
        if (Settings.Columns.Any(c => c.Id == id && c.IsCurrency == isCurrency))
            return;
        
        Settings.Columns.Add(new ItemColumnConfig
        {
            Id = id,
            IsCurrency = isCurrency
        });
        
        _pendingRefresh = true;
        NotifyToolSettingsChanged();
    }
    
    private void RefreshData()
    {
        try
        {
            var settings = Settings;
            var columns = settings.Columns;
            
            if (columns.Count == 0)
            {
                _cachedData = new PreparedItemTableData
                {
                    Rows = Array.Empty<ItemTableCharacterRow>(),
                    Columns = columns
                };
                _lastRefresh = DateTime.UtcNow;
                _pendingRefresh = false;
                return;
            }
            
            // Get all character names with disambiguation
            var characterNames = DbService.GetAllCharacterNamesDict();
            var disambiguatedNames = CacheService.GetDisambiguatedNames(characterNames.Keys);
            var rows = new Dictionary<ulong, ItemTableCharacterRow>();
            
            // Get world data for DC/Region lookups (from PriceTrackingService)
            var worldData = _priceTrackingService?.WorldData;
            
            // Get character world info from AutoRetainer (maps CID to world name)
            var characterWorlds = new Dictionary<ulong, string>();
            if (_autoRetainerService != null && _autoRetainerService.IsAvailable)
            {
                var arData = _autoRetainerService.GetAllCharacterData();
                foreach (var (_, world, _, cid) in arData)
                {
                    if (!string.IsNullOrEmpty(world))
                    {
                        characterWorlds[cid] = world;
                    }
                }
            }
            
            // Initialize rows for all known characters
            foreach (var (charId, name) in characterNames)
            {
                var displayName = disambiguatedNames.TryGetValue(charId, out var formatted) 
                    ? formatted : name ?? $"CID:{charId}";
                
                // Get world info for this character
                var worldName = characterWorlds.TryGetValue(charId, out var w) ? w : string.Empty;
                var dcName = !string.IsNullOrEmpty(worldName) ? worldData?.GetDataCenterForWorld(worldName)?.Name ?? string.Empty : string.Empty;
                var regionName = !string.IsNullOrEmpty(worldName) ? worldData?.GetRegionForWorld(worldName) ?? string.Empty : string.Empty;
                
                rows[charId] = new ItemTableCharacterRow
                {
                    CharacterId = charId,
                    Name = displayName,
                    WorldName = worldName,
                    DataCenterName = dcName,
                    RegionName = regionName,
                    ItemCounts = new Dictionary<uint, long>()
                };
            }
            
            // Populate data for each column
            foreach (var column in columns)
            {
                if (column.IsCurrency)
                {
                    // Get currency data from sampler database (time series)
                    PopulateCurrencyData(column, rows);
                }
                else
                {
                    // Get item data from inventory cache
                    PopulateItemData(column, rows, settings.IncludeRetainers);
                }
            }
            
            // Build result with sorted rows
            var sortedRows = CharacterSortHelper.SortByCharacter(
                rows.Values,
                _configService,
                _autoRetainerService,
                r => r.CharacterId,
                r => r.Name).ToList();
            
            _cachedData = new PreparedItemTableData
            {
                Rows = sortedRows,
                Columns = columns
            };
            
            _lastRefresh = DateTime.UtcNow;
            _pendingRefresh = false;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemTableTool] RefreshData error: {ex.Message}");
        }
    }
    
    private void PopulateCurrencyData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows)
    {
        try
        {
            var dataType = (TrackedDataType)column.Id;
            var variableName = dataType.ToString();
            
            // Get latest value for each character
            var allPoints = DbService.GetAllPointsBatch(variableName, null);
            
            if (allPoints.TryGetValue(variableName, out var points))
            {
                var latestByChar = points
                    .GroupBy(p => p.characterId)
                    .Select(g => (charId: g.Key, value: g.OrderByDescending(p => p.timestamp).First().value));
                
                foreach (var (charId, value) in latestByChar)
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
            LogService.Debug($"[ItemTableTool] PopulateCurrencyData error: {ex.Message}");
        }
    }
    
    private void PopulateItemData(ItemColumnConfig column, Dictionary<ulong, ItemTableCharacterRow> rows, bool includeRetainers)
    {
        try
        {
            if (_inventoryCacheService == null) return;
            
            var allInventories = _inventoryCacheService.GetAllInventories();
            
            foreach (var cache in allInventories)
            {
                // Skip retainers if not included
                if (!includeRetainers && cache.SourceType == Models.Inventory.InventorySourceType.Retainer)
                    continue;
                
                if (!rows.TryGetValue(cache.CharacterId, out var row))
                    continue;
                
                // Sum up item count
                var count = cache.Items
                    .Where(i => i.ItemId == column.Id)
                    .Sum(i => (long)i.Quantity);
                
                if (!row.ItemCounts.ContainsKey(column.Id))
                    row.ItemCounts[column.Id] = 0;
                
                row.ItemCounts[column.Id] += count;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemTableTool] PopulateItemData error: {ex.Message}");
        }
    }
    
    protected override bool HasToolSettings => true;
    
    protected override void DrawToolSettings()
    {
        var settings = Settings;
        
        // Display Options Section
        ImGui.TextUnformatted("Display Options");
        ImGui.Separator();
        
        // Show action buttons
        var showActionButtons = settings.ShowActionButtons;
        if (ImGui.Checkbox("Show Action Buttons", ref showActionButtons))
        {
            settings.ShowActionButtons = showActionButtons;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show the Add Item, Add Currency, and Refresh buttons");
        }
        
        // Compact numbers
        var useCompactNumbers = settings.UseCompactNumbers;
        if (ImGui.Checkbox("Compact Numbers", ref useCompactNumbers))
        {
            settings.UseCompactNumbers = useCompactNumbers;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Display large numbers in compact form (e.g., 10M instead of 10,000,000)");
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        ImGui.TextUnformatted("Column Management");
        ImGui.Separator();
        
        if (settings.Columns.Count == 0)
        {
            ImGui.TextDisabled("No columns configured. Add items or currencies above.");
        }
        else
        {
            // Track which column to delete or swap (can't modify list during iteration)
            int deleteIndex = -1;
            int swapUpIndex = -1;
            int swapDownIndex = -1;
            
            for (int i = 0; i < settings.Columns.Count; i++)
            {
                var column = settings.Columns[i];
                var defaultName = _tableWidget.GetColumnHeader(new ItemColumnConfig { Id = column.Id, IsCurrency = column.IsCurrency });
                
                ImGui.PushID(i);
                
                // Color picker (small button)
                var color = column.Color ?? new Vector4(1f, 1f, 1f, 1f);
                var hasColor = column.Color.HasValue;
                
                // Use a colored button as a color indicator/picker trigger
                if (hasColor)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, color);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * 1.1f);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, color * 0.9f);
                }
                
                if (ImGui.Button(hasColor ? "##color" : "○##color", new Vector2(20, 0)))
                {
                    // Toggle color on/off when clicking the button
                    if (hasColor)
                    {
                        column.Color = null;
                        NotifyToolSettingsChanged();
                    }
                    else
                    {
                        ImGui.OpenPopup("ColorPicker");
                    }
                }
                
                if (hasColor)
                {
                    ImGui.PopStyleColor(3);
                }
                
                // Color picker popup
                if (ImGui.BeginPopup("ColorPicker"))
                {
                    if (ImGui.ColorPicker4("##picker", ref color, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoSmallPreview))
                    {
                        column.Color = color;
                        NotifyToolSettingsChanged();
                    }
                    ImGui.EndPopup();
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(hasColor ? "Click to remove color" : "Click to set color");
                }
                
                ImGui.SameLine();
                
                // Custom name input
                var customName = column.CustomName ?? string.Empty;
                ImGui.SetNextItemWidth(120);
                if (ImGui.InputTextWithHint("##name", defaultName, ref customName, 64))
                {
                    column.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName;
                    NotifyToolSettingsChanged();
                }
                
                ImGui.SameLine();
                
                // Type label
                ImGui.TextDisabled(column.IsCurrency ? "[C]" : "[I]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(column.IsCurrency ? "Currency" : "Item");
                }
                
                ImGui.SameLine();
                
                // Store history checkbox (only for items, not currencies)
                if (!column.IsCurrency)
                {
                    var storeHistory = column.StoreHistory;
                    if (ImGui.Checkbox("##history", ref storeHistory))
                    {
                        column.StoreHistory = storeHistory;
                        NotifyToolSettingsChanged();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Store historical time-series data for this item");
                    }
                    ImGui.SameLine();
                }
                
                // Move up button
                ImGui.BeginDisabled(i == 0);
                if (ImGui.Button("▲##up", new Vector2(20, 0)))
                {
                    swapUpIndex = i;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Move up");
                }
                
                ImGui.SameLine();
                
                // Move down button
                ImGui.BeginDisabled(i == settings.Columns.Count - 1);
                if (ImGui.Button("▼##down", new Vector2(20, 0)))
                {
                    swapDownIndex = i;
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Move down");
                }
                
                ImGui.SameLine();
                
                // Delete button
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.15f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.2f, 0.2f, 1f));
                if (ImGui.Button("×##del", new Vector2(20, 0)))
                {
                    deleteIndex = i;
                }
                ImGui.PopStyleColor(2);
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Remove column");
                }
                
                ImGui.PopID();
            }
            
            // Process reordering and deletion after iteration
            if (swapUpIndex > 0)
            {
                var temp = settings.Columns[swapUpIndex - 1];
                settings.Columns[swapUpIndex - 1] = settings.Columns[swapUpIndex];
                settings.Columns[swapUpIndex] = temp;
                NotifyToolSettingsChanged();
            }
            else if (swapDownIndex >= 0 && swapDownIndex < settings.Columns.Count - 1)
            {
                var temp = settings.Columns[swapDownIndex + 1];
                settings.Columns[swapDownIndex + 1] = settings.Columns[swapDownIndex];
                settings.Columns[swapDownIndex] = temp;
                NotifyToolSettingsChanged();
            }
            else if (deleteIndex >= 0)
            {
                settings.Columns.RemoveAt(deleteIndex);
                _pendingRefresh = true;
                NotifyToolSettingsChanged();
            }
        }
    }
    
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var settings = _instanceSettings;
        
        // Serialize columns as a list of dictionaries
        var columns = settings.Columns.Select(c => new Dictionary<string, object?>
        {
            ["Id"] = c.Id,
            ["CustomName"] = c.CustomName,
            ["IsCurrency"] = c.IsCurrency,
            ["Color"] = c.Color.HasValue ? new float[] { c.Color.Value.X, c.Color.Value.Y, c.Color.Value.Z, c.Color.Value.W } : null,
            ["Width"] = c.Width,
            ["StoreHistory"] = c.StoreHistory
        }).ToList();
        
        // Serialize merged column groups
        var mergedColumnGroups = settings.MergedColumnGroups.Select(g => new Dictionary<string, object?>
        {
            ["Name"] = g.Name,
            ["ColumnIndices"] = g.ColumnIndices.ToList(),
            ["Color"] = g.Color.HasValue ? new float[] { g.Color.Value.X, g.Color.Value.Y, g.Color.Value.Z, g.Color.Value.W } : null,
            ["Width"] = g.Width
        }).ToList();
        
        // Serialize merged row groups
        var mergedRowGroups = settings.MergedRowGroups.Select(g => new Dictionary<string, object?>
        {
            ["Name"] = g.Name,
            ["CharacterIds"] = g.CharacterIds.ToList(),
            ["Color"] = g.Color.HasValue ? new float[] { g.Color.Value.X, g.Color.Value.Y, g.Color.Value.Z, g.Color.Value.W } : null
        }).ToList();
        
        return new Dictionary<string, object?>
        {
            ["Columns"] = columns,
            ["MergedColumnGroups"] = mergedColumnGroups,
            ["MergedRowGroups"] = mergedRowGroups,
            ["ShowTotalRow"] = settings.ShowTotalRow,
            ["Sortable"] = settings.Sortable,
            ["IncludeRetainers"] = settings.IncludeRetainers,
            ["CharacterColumnWidth"] = settings.CharacterColumnWidth,
            ["CharacterColumnColor"] = settings.CharacterColumnColor.HasValue ? new float[] { settings.CharacterColumnColor.Value.X, settings.CharacterColumnColor.Value.Y, settings.CharacterColumnColor.Value.Z, settings.CharacterColumnColor.Value.W } : null,
            ["SortColumnIndex"] = settings.SortColumnIndex,
            ["SortAscending"] = settings.SortAscending,
            ["ShowActionButtons"] = settings.ShowActionButtons,
            ["UseCompactNumbers"] = settings.UseCompactNumbers,
            ["HeaderColor"] = settings.HeaderColor.HasValue ? new float[] { settings.HeaderColor.Value.X, settings.HeaderColor.Value.Y, settings.HeaderColor.Value.Z, settings.HeaderColor.Value.W } : null,
            ["EvenRowColor"] = settings.EvenRowColor.HasValue ? new float[] { settings.EvenRowColor.Value.X, settings.EvenRowColor.Value.Y, settings.EvenRowColor.Value.Z, settings.EvenRowColor.Value.W } : null,
            ["OddRowColor"] = settings.OddRowColor.HasValue ? new float[] { settings.OddRowColor.Value.X, settings.OddRowColor.Value.Y, settings.OddRowColor.Value.Z, settings.OddRowColor.Value.W } : null,
            ["UseFullNameWidth"] = settings.UseFullNameWidth,
            ["AutoSizeEqualColumns"] = settings.AutoSizeEqualColumns,
            ["HorizontalAlignment"] = (int)settings.HorizontalAlignment,
            ["VerticalAlignment"] = (int)settings.VerticalAlignment,
            ["CharacterColumnHorizontalAlignment"] = (int)settings.CharacterColumnHorizontalAlignment,
            ["CharacterColumnVerticalAlignment"] = (int)settings.CharacterColumnVerticalAlignment,
            ["HeaderHorizontalAlignment"] = (int)settings.HeaderHorizontalAlignment,
            ["HeaderVerticalAlignment"] = (int)settings.HeaderVerticalAlignment,
            ["HiddenCharacters"] = settings.HiddenCharacters.ToList(),
            ["GroupingMode"] = (int)settings.GroupingMode,
            ["HideCharacterColumnInAllMode"] = settings.HideCharacterColumnInAllMode,
            ["TextColorMode"] = (int)settings.TextColorMode
        };
    }
    
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        var target = _instanceSettings;
        
        // Import columns
        if (settings.TryGetValue("Columns", out var columnsObj) && columnsObj != null)
        {
            target.Columns.Clear();
            
            try
            {
                // Handle Newtonsoft.Json JArray (used by ConfigManager)
                if (columnsObj is Newtonsoft.Json.Linq.JArray jArray)
                {
                    foreach (var columnToken in jArray)
                    {
                        if (columnToken is not Newtonsoft.Json.Linq.JObject columnObj) continue;
                        
                        var column = new ItemColumnConfig
                        {
                            Id = columnObj["Id"]?.ToObject<uint>() ?? 0,
                            CustomName = columnObj["CustomName"]?.ToObject<string>(),
                            IsCurrency = columnObj["IsCurrency"]?.ToObject<bool>() ?? false,
                            Width = columnObj["Width"]?.ToObject<float>() ?? 80f,
                            StoreHistory = columnObj["StoreHistory"]?.ToObject<bool>() ?? false
                        };
                        
                        var colorToken = columnObj["Color"];
                        if (colorToken is Newtonsoft.Json.Linq.JArray colorArr && colorArr.Count >= 4)
                        {
                            column.Color = new System.Numerics.Vector4(
                                colorArr[0].ToObject<float>(),
                                colorArr[1].ToObject<float>(),
                                colorArr[2].ToObject<float>(),
                                colorArr[3].ToObject<float>());
                        }
                        
                        target.Columns.Add(column);
                    }
                }
                // Fallback: Handle System.Text.Json.JsonElement (in case it's used elsewhere)
                else if (columnsObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var columnJson in jsonElement.EnumerateArray())
                    {
                        var column = new ItemColumnConfig
                        {
                            Id = columnJson.TryGetProperty("Id", out var idProp) ? idProp.GetUInt32() : 0,
                            CustomName = columnJson.TryGetProperty("CustomName", out var nameProp) && nameProp.ValueKind != System.Text.Json.JsonValueKind.Null ? nameProp.GetString() : null,
                            IsCurrency = columnJson.TryGetProperty("IsCurrency", out var currProp) && currProp.GetBoolean(),
                            Width = columnJson.TryGetProperty("Width", out var widthProp) ? widthProp.GetSingle() : 80f,
                            StoreHistory = columnJson.TryGetProperty("StoreHistory", out var histProp) && histProp.GetBoolean()
                        };
                        
                        if (columnJson.TryGetProperty("Color", out var colorProp) && colorProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var colorArr = colorProp.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                            if (colorArr.Length >= 4)
                                column.Color = new System.Numerics.Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                        }
                        
                        target.Columns.Add(column);
                    }
                }
                // Handle in-memory List<Dictionary<string, object?>> (from direct ExportToolSettings)
                else if (columnsObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is not IDictionary<string, object?> columnDict) continue;
                        
                        var column = new ItemColumnConfig
                        {
                            Id = columnDict.TryGetValue("Id", out var idVal) && idVal != null ? Convert.ToUInt32(idVal) : 0,
                            CustomName = columnDict.TryGetValue("CustomName", out var nameVal) ? nameVal?.ToString() : null,
                            IsCurrency = columnDict.TryGetValue("IsCurrency", out var currVal) && currVal is bool b && b,
                            Width = columnDict.TryGetValue("Width", out var widthVal) && widthVal != null ? Convert.ToSingle(widthVal) : 80f,
                            StoreHistory = columnDict.TryGetValue("StoreHistory", out var histVal) && histVal is bool h && h
                        };
                        
                        if (columnDict.TryGetValue("Color", out var colorVal) && colorVal is float[] colorArr && colorArr.Length >= 4)
                        {
                            column.Color = new System.Numerics.Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                        }
                        
                        target.Columns.Add(column);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[ItemTableTool] Error importing columns: {ex.Message}");
            }
        }
        
        // Import scalar settings
        target.ShowTotalRow = GetSetting(settings, "ShowTotalRow", target.ShowTotalRow);
        target.Sortable = GetSetting(settings, "Sortable", target.Sortable);
        target.IncludeRetainers = GetSetting(settings, "IncludeRetainers", target.IncludeRetainers);
        target.CharacterColumnWidth = GetSetting(settings, "CharacterColumnWidth", target.CharacterColumnWidth);
        target.SortColumnIndex = GetSetting(settings, "SortColumnIndex", target.SortColumnIndex);
        target.SortAscending = GetSetting(settings, "SortAscending", target.SortAscending);
        target.ShowActionButtons = GetSetting(settings, "ShowActionButtons", target.ShowActionButtons);
        target.UseCompactNumbers = GetSetting(settings, "UseCompactNumbers", target.UseCompactNumbers);
        target.UseFullNameWidth = GetSetting(settings, "UseFullNameWidth", target.UseFullNameWidth);
        target.AutoSizeEqualColumns = GetSetting(settings, "AutoSizeEqualColumns", target.AutoSizeEqualColumns);
        target.HorizontalAlignment = (Widgets.TableHorizontalAlignment)GetSetting(settings, "HorizontalAlignment", (int)target.HorizontalAlignment);
        target.VerticalAlignment = (Widgets.TableVerticalAlignment)GetSetting(settings, "VerticalAlignment", (int)target.VerticalAlignment);
        target.CharacterColumnHorizontalAlignment = (Widgets.TableHorizontalAlignment)GetSetting(settings, "CharacterColumnHorizontalAlignment", (int)target.CharacterColumnHorizontalAlignment);
        target.CharacterColumnVerticalAlignment = (Widgets.TableVerticalAlignment)GetSetting(settings, "CharacterColumnVerticalAlignment", (int)target.CharacterColumnVerticalAlignment);
        target.HeaderHorizontalAlignment = (Widgets.TableHorizontalAlignment)GetSetting(settings, "HeaderHorizontalAlignment", (int)target.HeaderHorizontalAlignment);
        target.HeaderVerticalAlignment = (Widgets.TableVerticalAlignment)GetSetting(settings, "HeaderVerticalAlignment", (int)target.HeaderVerticalAlignment);
        
        // Import colors
        target.CharacterColumnColor = ImportColor(settings, "CharacterColumnColor");
        target.HeaderColor = ImportColor(settings, "HeaderColor");
        target.EvenRowColor = ImportColor(settings, "EvenRowColor");
        target.OddRowColor = ImportColor(settings, "OddRowColor");
        
        // Import hidden characters
        target.HiddenCharacters = ImportUlongHashSet(settings, "HiddenCharacters") ?? new HashSet<ulong>();
        
        // Import merged column groups
        target.MergedColumnGroups = ImportMergedColumnGroups(settings, "MergedColumnGroups") ?? new List<Widgets.MergedColumnGroup>();
        
        // Import merged row groups
        target.MergedRowGroups = ImportMergedRowGroups(settings, "MergedRowGroups") ?? new List<Widgets.MergedRowGroup>();
        
        // Import grouping mode
        target.GroupingMode = (Widgets.TableGroupingMode)GetSetting(settings, "GroupingMode", (int)target.GroupingMode);
        target.HideCharacterColumnInAllMode = GetSetting(settings, "HideCharacterColumnInAllMode", target.HideCharacterColumnInAllMode);
        
        // Import text color mode
        target.TextColorMode = (Widgets.TableTextColorMode)GetSetting(settings, "TextColorMode", (int)target.TextColorMode);
        
        _pendingRefresh = true;
    }
    
    private static System.Numerics.Vector4? ImportColor(Dictionary<string, object?>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return null;
        
        try
        {
            // Handle Newtonsoft.Json JArray (used by ConfigManager)
            if (value is Newtonsoft.Json.Linq.JArray jArray && jArray.Count >= 4)
            {
                return new System.Numerics.Vector4(
                    jArray[0].ToObject<float>(),
                    jArray[1].ToObject<float>(),
                    jArray[2].ToObject<float>(),
                    jArray[3].ToObject<float>());
            }
            // Fallback: Handle System.Text.Json.JsonElement
            if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var arr = jsonElement.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                if (arr.Length >= 4)
                    return new System.Numerics.Vector4(arr[0], arr[1], arr[2], arr[3]);
            }
            // Handle in-memory float[] (from direct ExportToolSettings)
            if (value is float[] floatArr && floatArr.Length >= 4)
            {
                return new System.Numerics.Vector4(floatArr[0], floatArr[1], floatArr[2], floatArr[3]);
            }
        }
        catch { }
        
        return null;
    }
    
    private static HashSet<ulong>? ImportUlongHashSet(Dictionary<string, object?>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return null;
        
        try
        {
            // Handle Newtonsoft.Json JArray (used by ConfigManager)
            if (value is Newtonsoft.Json.Linq.JArray jArray)
            {
                return new HashSet<ulong>(jArray.Select(v => v.ToObject<ulong>()));
            }
            // Handle List<ulong> directly
            if (value is List<ulong> list)
            {
                return new HashSet<ulong>(list);
            }
            // Handle IEnumerable<object>
            if (value is System.Collections.IEnumerable enumerable)
            {
                var result = new HashSet<ulong>();
                foreach (var item in enumerable)
                {
                    if (item is ulong ul)
                        result.Add(ul);
                    else if (item is long l)
                        result.Add((ulong)l);
                    else if (ulong.TryParse(item?.ToString(), out var parsed))
                        result.Add(parsed);
                }
                return result;
            }
        }
        catch { }
        
        return null;
    }
    
    private static List<Widgets.MergedColumnGroup>? ImportMergedColumnGroups(Dictionary<string, object?>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return null;
        
        var result = new List<Widgets.MergedColumnGroup>();
        
        try
        {
            // Handle Newtonsoft.Json JArray (used by ConfigManager)
            if (value is Newtonsoft.Json.Linq.JArray jArray)
            {
                foreach (var item in jArray)
                {
                    if (item is not Newtonsoft.Json.Linq.JObject jObj) continue;
                    
                    var group = new Widgets.MergedColumnGroup
                    {
                        Name = jObj["Name"]?.ToObject<string>() ?? "Merged",
                        Width = jObj["Width"]?.ToObject<float>() ?? 80f
                    };
                    
                    // Import column indices
                    if (jObj["ColumnIndices"] is Newtonsoft.Json.Linq.JArray indicesArr)
                    {
                        group.ColumnIndices = indicesArr.Select(v => v.ToObject<int>()).ToList();
                    }
                    
                    // Import color
                    if (jObj["Color"] is Newtonsoft.Json.Linq.JArray colorArr && colorArr.Count >= 4)
                    {
                        group.Color = new System.Numerics.Vector4(
                            colorArr[0].ToObject<float>(),
                            colorArr[1].ToObject<float>(),
                            colorArr[2].ToObject<float>(),
                            colorArr[3].ToObject<float>());
                    }
                    
                    result.Add(group);
                }
                return result;
            }
            // Handle in-memory List<Dictionary<string, object?>> (from direct ExportToolSettings)
            else if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is not IDictionary<string, object?> dict) continue;
                    
                    var group = new Widgets.MergedColumnGroup
                    {
                        Name = dict.TryGetValue("Name", out var nameVal) ? nameVal?.ToString() ?? "Merged" : "Merged",
                        Width = dict.TryGetValue("Width", out var widthVal) && widthVal != null ? Convert.ToSingle(widthVal) : 80f
                    };
                    
                    // Import column indices
                    if (dict.TryGetValue("ColumnIndices", out var indicesVal) && indicesVal is System.Collections.IEnumerable indicesEnum)
                    {
                        group.ColumnIndices = new List<int>();
                        foreach (var idx in indicesEnum)
                        {
                            if (idx != null)
                                group.ColumnIndices.Add(Convert.ToInt32(idx));
                        }
                    }
                    
                    // Import color
                    if (dict.TryGetValue("Color", out var colorVal) && colorVal is float[] colorArr && colorArr.Length >= 4)
                    {
                        group.Color = new System.Numerics.Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                    }
                    
                    result.Add(group);
                }
                return result.Count > 0 ? result : null;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemTableTool] Error importing merged column groups: {ex.Message}");
        }
        
        return result.Count > 0 ? result : null;
    }
    
    private static List<Widgets.MergedRowGroup>? ImportMergedRowGroups(Dictionary<string, object?>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return null;
        
        var result = new List<Widgets.MergedRowGroup>();
        
        try
        {
            // Handle Newtonsoft.Json JArray (used by ConfigManager)
            if (value is Newtonsoft.Json.Linq.JArray jArray)
            {
                foreach (var item in jArray)
                {
                    if (item is not Newtonsoft.Json.Linq.JObject jObj) continue;
                    
                    var group = new Widgets.MergedRowGroup
                    {
                        Name = jObj["Name"]?.ToObject<string>() ?? "Merged"
                    };
                    
                    // Import character IDs
                    if (jObj["CharacterIds"] is Newtonsoft.Json.Linq.JArray idsArr)
                    {
                        group.CharacterIds = idsArr.Select(v => v.ToObject<ulong>()).ToList();
                    }
                    
                    // Import color
                    if (jObj["Color"] is Newtonsoft.Json.Linq.JArray colorArr && colorArr.Count >= 4)
                    {
                        group.Color = new System.Numerics.Vector4(
                            colorArr[0].ToObject<float>(),
                            colorArr[1].ToObject<float>(),
                            colorArr[2].ToObject<float>(),
                            colorArr[3].ToObject<float>());
                    }
                    
                    result.Add(group);
                }
                return result;
            }
            // Handle in-memory List<Dictionary<string, object?>> (from direct ExportToolSettings)
            else if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is not IDictionary<string, object?> dict) continue;
                    
                    var group = new Widgets.MergedRowGroup
                    {
                        Name = dict.TryGetValue("Name", out var nameVal) ? nameVal?.ToString() ?? "Merged" : "Merged"
                    };
                    
                    // Import character IDs
                    if (dict.TryGetValue("CharacterIds", out var idsVal) && idsVal is System.Collections.IEnumerable idsEnum)
                    {
                        group.CharacterIds = new List<ulong>();
                        foreach (var id in idsEnum)
                        {
                            if (id != null)
                                group.CharacterIds.Add(Convert.ToUInt64(id));
                        }
                    }
                    
                    // Import color
                    if (dict.TryGetValue("Color", out var colorVal) && colorVal is float[] colorArr && colorArr.Length >= 4)
                    {
                        group.Color = new System.Numerics.Vector4(colorArr[0], colorArr[1], colorArr[2], colorArr[3]);
                    }
                    
                    result.Add(group);
                }
                return result.Count > 0 ? result : null;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemTableTool] Error importing merged row groups: {ex.Message}");
        }
        
        return result.Count > 0 ? result : null;
    }
    
    public override void Dispose()
    {
        base.Dispose();
    }
}
