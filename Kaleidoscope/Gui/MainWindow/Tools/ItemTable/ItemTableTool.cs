using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
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
    
    private readonly ItemTableWidget _tableWidget;
    private readonly ItemPickerWidget? _itemPicker;
    
    // Cached data
    private PreparedItemTableData? _cachedData;
    private DateTime _lastRefresh = DateTime.MinValue;
    private volatile bool _pendingRefresh = true;
    
    private ItemTableSettings Settings => _configService.Config.ItemTable;
    private KaleidoscopeDbService DbService => _samplerService.DbService;
    
    public ItemTableTool(
        SamplerService samplerService,
        ConfigurationService configService,
        InventoryCacheService? inventoryCacheService = null,
        TrackedDataRegistry? trackedDataRegistry = null,
        ItemDataService? itemDataService = null,
        IDataManager? dataManager = null)
    {
        _samplerService = samplerService;
        _configService = configService;
        _inventoryCacheService = inventoryCacheService;
        _trackedDataRegistry = trackedDataRegistry;
        _itemDataService = itemDataService;
        _dataManager = dataManager;
        
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
            trackedDataRegistry);
        
        // Bind settings
        _tableWidget.BindSettings(
            Settings,
            () => _configService.Save(),
            "Table Settings");
        
        // Create item picker if we have the required services
        if (_dataManager != null && _itemDataService != null)
        {
            _itemPicker = new ItemPickerWidget(_dataManager, _itemDataService);
        }
        
        // Register widget as settings provider
        RegisterSettingsProvider(_tableWidget);
    }
    
    public override void DrawContent()
    {
        try
        {
            var settings = Settings;
            
            // Auto-refresh on pending changes or time interval (if enabled)
            var shouldAutoRefresh = settings.AutoRefresh && 
                (DateTime.UtcNow - _lastRefresh).TotalSeconds > settings.RefreshIntervalSeconds;
            
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
        
        ImGui.SameLine();
        
        if (ImGui.Button("Refresh"))
        {
            _pendingRefresh = true;
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
            
            if (_itemPicker != null)
            {
                if (_itemPicker.Draw("##ItemToAdd", marketableOnly: false, width: 250))
                {
                    // Item selected
                    if (_itemPicker.SelectedItemId.HasValue)
                    {
                        AddColumn(_itemPicker.SelectedItemId.Value, isCurrency: false);
                        _itemPicker.ClearSelection();
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
        _configService.Save();
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
            
            // Get all character names
            var characterNames = DbService.GetAllCharacterNamesDict();
            var rows = new Dictionary<ulong, ItemTableCharacterRow>();
            
            // Initialize rows for all known characters
            foreach (var (charId, name) in characterNames)
            {
                rows[charId] = new ItemTableCharacterRow
                {
                    CharacterId = charId,
                    Name = name ?? $"CID:{charId}",
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
            
            // Build result
            _cachedData = new PreparedItemTableData
            {
                Rows = rows.Values.OrderBy(r => r.Name).ToList(),
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
            _configService.Save();
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
            _configService.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Display large numbers in compact form (e.g., 10M instead of 10,000,000)");
        }
        
        // Auto-refresh section
        ImGui.Spacing();
        var autoRefresh = settings.AutoRefresh;
        if (ImGui.Checkbox("Auto-Refresh", ref autoRefresh))
        {
            settings.AutoRefresh = autoRefresh;
            _configService.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically refresh data at regular intervals");
        }
        
        if (autoRefresh)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            var refreshInterval = settings.RefreshIntervalSeconds;
            if (ImGui.DragFloat("##RefreshInterval", ref refreshInterval, 0.5f, 1f, 60f, "%.1fs"))
            {
                settings.RefreshIntervalSeconds = refreshInterval;
                _configService.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh interval in seconds");
            }
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
                        _configService.Save();
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
                        _configService.Save();
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
                    _configService.Save();
                }
                
                ImGui.SameLine();
                
                // Type label
                ImGui.TextDisabled(column.IsCurrency ? "[C]" : "[I]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(column.IsCurrency ? "Currency" : "Item");
                }
                
                ImGui.SameLine();
                
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
                _configService.Save();
            }
            else if (swapDownIndex >= 0 && swapDownIndex < settings.Columns.Count - 1)
            {
                var temp = settings.Columns[swapDownIndex + 1];
                settings.Columns[swapDownIndex + 1] = settings.Columns[swapDownIndex];
                settings.Columns[swapDownIndex] = temp;
                _configService.Save();
            }
            else if (deleteIndex >= 0)
            {
                settings.Columns.RemoveAt(deleteIndex);
                _pendingRefresh = true;
                _configService.Save();
            }
        }
        
        ImGui.Spacing();
        
        if (settings.Columns.Count > 0 && ImGui.Button("Clear All"))
        {
            settings.Columns.Clear();
            _pendingRefresh = true;
            _configService.Save();
        }
    }
    
    public override void Dispose()
    {
        base.Dispose();
    }
}
