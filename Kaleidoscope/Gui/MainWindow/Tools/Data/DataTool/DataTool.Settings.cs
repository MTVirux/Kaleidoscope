using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using MTGui.Common;
using MTGui.Graph;
using MTGui.Table;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// DataTool partial class containing tool settings, context menus, and import/export logic.
/// </summary>
public partial class DataTool
{
    protected override bool HasToolSettings => true;
    
    /// <summary>
    /// Provides custom context menu options for the DataTool.
    /// Allows toggling between Graph and Table view modes.
    /// </summary>
    public override IReadOnlyList<ToolContextMenuOption>? GetContextMenuOptions()
    {
        var isGraphView = Settings.ViewMode == DataToolViewMode.Graph;
        
        return new List<ToolContextMenuOption>
        {
            new ToolContextMenuOption
            {
                Label = "Table View",
                Icon = "📊",
                IsChecked = !isGraphView,
                Tooltip = "Display data in a table format",
                OnClick = () =>
                {
                    if (isGraphView)
                    {
                        Settings.ViewMode = DataToolViewMode.Table;
                        UpdateTitle();
                        _pendingTableRefresh = true;
                        _graphCacheIsDirty = true;
                        NotifyToolSettingsChanged();
                    }
                }
            },
            new ToolContextMenuOption
            {
                Label = "Graph View",
                Icon = "📈",
                IsChecked = isGraphView,
                Tooltip = "Display data as a time-series graph",
                OnClick = () =>
                {
                    if (!isGraphView)
                    {
                        Settings.ViewMode = DataToolViewMode.Graph;
                        UpdateTitle();
                        _pendingTableRefresh = true;
                        _graphCacheIsDirty = true;
                        NotifyToolSettingsChanged();
                    }
                }
            }
        };
    }
    
    protected override void DrawToolSettings()
    {
        var settings = Settings;
        
        // View Mode Section
        ImGui.TextUnformatted("View Mode");
        ImGui.Separator();
        
        var viewMode = (int)settings.ViewMode;
        if (ImGui.Combo("View", ref viewMode, "Table\0Graph\0"))
        {
            settings.ViewMode = (DataToolViewMode)viewMode;
            UpdateTitle();
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Display Options (shared between both modes)
        ImGui.TextUnformatted("Display Options");
        ImGui.Separator();
        
        // Grouping mode
        var groupingMode = (int)settings.GroupingMode;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Group By", ref groupingMode, "Character\0World\0Data Center\0Region\0All\0"))
        {
            settings.GroupingMode = (TableGroupingMode)groupingMode;
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
        
        // Include retainers
        var includeRetainers = settings.IncludeRetainers;
        if (ImGui.Checkbox("Include Retainers", ref includeRetainers))
        {
            settings.IncludeRetainers = includeRetainers;
            _pendingTableRefresh = true;
            _graphCacheIsDirty = true;
            NotifyToolSettingsChanged();
        }
        
        // Show retainer breakdown options (available when IncludeRetainers is enabled)
        if (settings.IncludeRetainers)
        {
            ImGui.Indent(16f);
            
            // Table mode retainer breakdown
            var showRetainerBreakdownTable = settings.ShowRetainerBreakdown;
            if (ImGui.Checkbox("Retainer Breakdown (Table)", ref showRetainerBreakdownTable))
            {
                settings.ShowRetainerBreakdown = showRetainerBreakdownTable;
                _pendingTableRefresh = true;
                NotifyToolSettingsChanged();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Show expandable rows for each character to view per-retainer item counts.");
            }
            
            // Graph mode retainer breakdown
            var showRetainerBreakdownGraph = settings.ShowRetainerBreakdownInGraph;
            if (ImGui.Checkbox("Retainer Breakdown (Graph)", ref showRetainerBreakdownGraph))
            {
                settings.ShowRetainerBreakdownInGraph = showRetainerBreakdownGraph;
                _graphCacheIsDirty = true;
                NotifyToolSettingsChanged();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Show separate series for each retainer's inventory in graph view.\n\n" +
                      "Note: Historical tracking must be enabled for each item,\n" +
                      "and you must open each retainer's inventory at least once to collect data.");
            }
            
            // Show warning if any items don't have historical tracking (only relevant for graph mode)
            if (showRetainerBreakdownGraph)
            {
                var itemsWithoutTracking = settings.Columns.Count(c => !c.IsCurrency && !_configService.Config.ItemsWithHistoricalTracking.Contains(c.Id));
                if (itemsWithoutTracking > 0)
                {
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"⚠ {itemsWithoutTracking} item(s) without historical tracking");
                }
            }
            
            ImGui.Unindent(16f);
        }
        
        // Show action buttons
        var showActionButtons = settings.ShowActionButtons;
        if (ImGui.Checkbox("Show Action Buttons", ref showActionButtons))
        {
            settings.ShowActionButtons = showActionButtons;
            NotifyToolSettingsChanged();
        }
        
        // Hide zero rows
        var hideZeroRows = settings.HideZeroRows;
        if (ImGui.Checkbox("Hide Zero Rows", ref hideZeroRows))
        {
            settings.HideZeroRows = hideZeroRows;
            _pendingTableRefresh = true;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Hide rows where all column values are zero.");
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Column/Series Management with integrated merge functionality
        ColumnManagementWidget.Draw(
            settings.Columns,
            settings.MergedColumnGroups,
            column => GetSeriesDisplayName(column),
            onSettingsChanged: () => NotifyToolSettingsChanged(),
            onRefreshNeeded: () => { _pendingTableRefresh = true; _graphCacheIsDirty = true; },
            sectionTitle: "Item / Currency Management",
            emptyMessage: "No items or currencies configured.",
            itemLabel: "Item",
            currencyLabel: "Currency",
            widgetId: $"datatool_{GetHashCode()}",
            isItemHistoricalTrackingEnabled: (itemId) => _configService.Config.ItemsWithHistoricalTracking.Contains(itemId),
            onItemHistoricalTrackingToggled: (itemId, enabled) =>
            {
                if (enabled)
                {
                    _configService.Config.ItemsWithHistoricalTracking.Add(itemId);
                }
                else
                {
                    _configService.Config.ItemsWithHistoricalTracking.Remove(itemId);
                }
                _configService.MarkDirty();
                _pendingTableRefresh = true;
                _graphCacheIsDirty = true;
            },
            isCurrencyHistoricalTrackingEnabled: (currencyId) => _configService.Config.EnabledTrackedDataTypes.Contains((TrackedDataType)currencyId),
            onCurrencyHistoricalTrackingToggled: (currencyId, enabled) =>
            {
                var dataType = (TrackedDataType)currencyId;
                if (enabled)
                {
                    _configService.Config.EnabledTrackedDataTypes.Add(dataType);
                }
                else
                {
                    _configService.Config.EnabledTrackedDataTypes.Remove(dataType);
                }
                _configService.MarkDirty();
                _pendingTableRefresh = true;
                _graphCacheIsDirty = true;
            });
        
        // Source Merging
        // Compute available row identifiers based on grouping mode
        var currentGroupingMode = settings.GroupingMode;
        var availableCharIds = _cachedTableData?.Rows?.Select(r => r.CharacterId).Distinct().ToList() 
                               ?? new List<ulong>();
        
        // For non-Character modes, compute available group keys
        List<string>? availableGroupKeys = null;
        if (currentGroupingMode != TableGroupingMode.Character && _cachedTableData?.Rows != null)
        {
            availableGroupKeys = currentGroupingMode switch
            {
                TableGroupingMode.World => _cachedTableData.Rows
                    .Select(r => string.IsNullOrEmpty(r.WorldName) ? "Unknown World" : r.WorldName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                TableGroupingMode.DataCenter => _cachedTableData.Rows
                    .Select(r => string.IsNullOrEmpty(r.DataCenterName) ? "Unknown DC" : r.DataCenterName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                TableGroupingMode.Region => _cachedTableData.Rows
                    .Select(r => string.IsNullOrEmpty(r.RegionName) ? "Unknown Region" : r.RegionName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                TableGroupingMode.All => new List<string> { "All Characters" },
                _ => null
            };
        }
        
        MergeManagementWidget.DrawMergedRows(
            settings.MergedRowGroups,
            groupingMode: currentGroupingMode,
            getCharacterName: GetCharacterDisplayName,
            availableCharacterIds: availableCharIds,
            availableGroupKeys: availableGroupKeys,
            onSettingsChanged: () => NotifyToolSettingsChanged(),
            onRefreshNeeded: () => { _pendingTableRefresh = true; _graphCacheIsDirty = true; },
            widgetId: $"datatool_rows_{GetHashCode()}");
        
        // Special Grouping
        SpecialGroupingWidget.Draw(
            settings.SpecialGrouping,
            settings.Columns,
            onSettingsChanged: () => NotifyToolSettingsChanged(),
            onRefreshNeeded: () => { _pendingTableRefresh = true; _graphCacheIsDirty = true; },
            onAddColumn: (id, isCurrency) => AddColumn(id, isCurrency));
    }
    
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var settings = _instanceSettings;
        
        // Use centralized column export
        var columns = ColumnManagementWidget.ExportColumns(settings.Columns);
        
        // Serialize merged column groups
        var mergedColumnGroups = settings.MergedColumnGroups.Select(g => new Dictionary<string, object?>
        {
            ["Name"] = g.Name,
            ["ColumnIndices"] = g.ColumnIndices.ToList(),
            ["Color"] = g.Color.HasValue ? new float[] { g.Color.Value.X, g.Color.Value.Y, g.Color.Value.Z, g.Color.Value.W } : null,
            ["Width"] = g.Width,
            ["ShowInTable"] = g.ShowInTable,
            ["ShowInGraph"] = g.ShowInGraph
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
            // View mode
            ["ViewMode"] = (int)settings.ViewMode,
            
            // Shared settings
            ["Columns"] = columns,
            ["IncludeRetainers"] = settings.IncludeRetainers,
            ["ShowActionButtons"] = settings.ShowActionButtons,
            ["TableNumberFormatStyle"] = (int)settings.TableNumberFormat.Style,
            ["TableNumberFormatDecimalPlaces"] = settings.TableNumberFormat.DecimalPlaces,
            ["GraphNumberFormatStyle"] = (int)settings.GraphNumberFormat.Style,
            ["GraphNumberFormatDecimalPlaces"] = settings.GraphNumberFormat.DecimalPlaces,
            ["UseCharacterFilter"] = settings.UseCharacterFilter,
            ["SelectedCharacterIds"] = settings.SelectedCharacterIds.ToList(),
            ["GroupingMode"] = (int)settings.GroupingMode,
            ["SpecialGrouping"] = SpecialGroupingWidget.ExportSettings(settings.SpecialGrouping),
            
            // Table-specific
            ["MergedColumnGroups"] = mergedColumnGroups,
            ["MergedRowGroups"] = mergedRowGroups,
            ["ShowTotalRow"] = settings.ShowTotalRow,
            ["Sortable"] = settings.Sortable,
            ["CharacterColumnWidth"] = settings.CharacterColumnWidth,
            ["CharacterColumnColor"] = settings.CharacterColumnColor.HasValue ? new float[] { settings.CharacterColumnColor.Value.X, settings.CharacterColumnColor.Value.Y, settings.CharacterColumnColor.Value.Z, settings.CharacterColumnColor.Value.W } : null,
            ["SortColumnIndex"] = settings.SortColumnIndex,
            ["SortAscending"] = settings.SortAscending,
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
            ["HideCharacterColumnInAllMode"] = settings.HideCharacterColumnInAllMode,
            ["TextColorMode"] = (int)settings.TextColorMode,
            ["ShowRetainerBreakdown"] = settings.ShowRetainerBreakdown,
            ["ShowRetainerBreakdownInGraph"] = settings.ShowRetainerBreakdownInGraph,
            
            // Graph-specific
            ["ColorMode"] = (int)settings.ColorMode,
            ["LegendWidth"] = settings.LegendWidth,
            ["LegendHeightPercent"] = settings.LegendHeightPercent,
            ["ShowLegend"] = settings.ShowLegend,
            ["LegendCollapsed"] = settings.LegendCollapsed,
            ["LegendPosition"] = (int)settings.LegendPosition,
            ["GraphType"] = (int)settings.GraphType,
            ["ShowXAxisTimestamps"] = settings.ShowXAxisTimestamps,
            ["ShowCrosshair"] = settings.ShowCrosshair,
            ["ShowGridLines"] = settings.ShowGridLines,
            ["ShowCurrentPriceLine"] = settings.ShowCurrentPriceLine,
            ["ShowValueLabel"] = settings.ShowValueLabel,
            ["ValueLabelOffsetX"] = settings.ValueLabelOffsetX,
            ["ValueLabelOffsetY"] = settings.ValueLabelOffsetY,
            ["AutoScrollEnabled"] = settings.AutoScrollEnabled,
            ["AutoScrollTimeValue"] = settings.AutoScrollTimeValue,
            ["AutoScrollTimeUnit"] = (int)settings.AutoScrollTimeUnit,
            ["AutoScrollNowPosition"] = settings.AutoScrollNowPosition,
            ["ShowControlsDrawer"] = settings.ShowControlsDrawer,
            ["TimeRangeValue"] = settings.TimeRangeValue,
            ["TimeRangeUnit"] = (int)settings.TimeRangeUnit
        };
    }
    
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        var target = _instanceSettings;
        
        // View mode
        target.ViewMode = (DataToolViewMode)GetSetting(settings, "ViewMode", (int)target.ViewMode);
        
        // Columns
        if (settings.TryGetValue("Columns", out var columnsObj) && columnsObj != null)
        {
            target.Columns.Clear();
            target.Columns.AddRange(ColumnManagementWidget.ImportColumns(columnsObj));
        }
        
        // Shared settings
        target.IncludeRetainers = GetSetting(settings, "IncludeRetainers", target.IncludeRetainers);
        target.ShowActionButtons = GetSetting(settings, "ShowActionButtons", target.ShowActionButtons);
        
        // Table number format
        if (settings.ContainsKey("TableNumberFormatStyle"))
        {
            target.TableNumberFormat.Style = (NumberFormatStyle)GetSetting(settings, "TableNumberFormatStyle", (int)target.TableNumberFormat.Style);
            target.TableNumberFormat.DecimalPlaces = GetSetting(settings, "TableNumberFormatDecimalPlaces", target.TableNumberFormat.DecimalPlaces);
        }
        else if (settings.ContainsKey("NumberFormatStyle"))
        {
            // Backward compatibility: migrate old shared NumberFormat to table format
            target.TableNumberFormat.Style = (NumberFormatStyle)GetSetting(settings, "NumberFormatStyle", (int)target.TableNumberFormat.Style);
            target.TableNumberFormat.DecimalPlaces = GetSetting(settings, "NumberFormatDecimalPlaces", target.TableNumberFormat.DecimalPlaces);
        }
        else if (settings.ContainsKey("UseCompactNumbers"))
        {
            // Backward compatibility: migrate old UseCompactNumbers setting
            var useCompact = GetSetting(settings, "UseCompactNumbers", false);
            target.TableNumberFormat.Style = useCompact ? NumberFormatStyle.Compact : NumberFormatStyle.Standard;
        }
        
        // Graph number format
        if (settings.ContainsKey("GraphNumberFormatStyle"))
        {
            target.GraphNumberFormat.Style = (NumberFormatStyle)GetSetting(settings, "GraphNumberFormatStyle", (int)target.GraphNumberFormat.Style);
            target.GraphNumberFormat.DecimalPlaces = GetSetting(settings, "GraphNumberFormatDecimalPlaces", target.GraphNumberFormat.DecimalPlaces);
        }
        else if (settings.ContainsKey("NumberFormatStyle"))
        {
            // Backward compatibility: migrate old shared NumberFormat to graph format too
            target.GraphNumberFormat.Style = (NumberFormatStyle)GetSetting(settings, "NumberFormatStyle", (int)target.GraphNumberFormat.Style);
            target.GraphNumberFormat.DecimalPlaces = GetSetting(settings, "NumberFormatDecimalPlaces", target.GraphNumberFormat.DecimalPlaces);
        }
        else if (settings.ContainsKey("UseCompactNumbers"))
        {
            // Backward compatibility: migrate old UseCompactNumbers setting
            var useCompact = GetSetting(settings, "UseCompactNumbers", false);
            target.GraphNumberFormat.Style = useCompact ? NumberFormatStyle.Compact : NumberFormatStyle.Standard;
        }
        
        target.UseCharacterFilter = GetSetting(settings, "UseCharacterFilter", target.UseCharacterFilter);
        
        var selectedIds = ImportList<ulong>(settings, "SelectedCharacterIds");
        if (selectedIds != null)
        {
            target.SelectedCharacterIds.Clear();
            target.SelectedCharacterIds.AddRange(selectedIds);
        }
        
        target.GroupingMode = (TableGroupingMode)GetSetting(settings, "GroupingMode", (int)target.GroupingMode);
        
        // Special grouping
        if (settings.TryGetValue("SpecialGrouping", out var specialGroupingObj))
        {
            var specialGroupingDict = ConvertToDictionary(specialGroupingObj);
            SpecialGroupingWidget.ImportSettings(target.SpecialGrouping, specialGroupingDict);
        }
        
        // Table-specific
        target.ShowTotalRow = GetSetting(settings, "ShowTotalRow", target.ShowTotalRow);
        target.Sortable = GetSetting(settings, "Sortable", target.Sortable);
        target.CharacterColumnWidth = GetSetting(settings, "CharacterColumnWidth", target.CharacterColumnWidth);
        target.SortColumnIndex = GetSetting(settings, "SortColumnIndex", target.SortColumnIndex);
        target.SortAscending = GetSetting(settings, "SortAscending", target.SortAscending);
        target.UseFullNameWidth = GetSetting(settings, "UseFullNameWidth", target.UseFullNameWidth);
        target.AutoSizeEqualColumns = GetSetting(settings, "AutoSizeEqualColumns", target.AutoSizeEqualColumns);
        target.HorizontalAlignment = (MTTableHorizontalAlignment)GetSetting(settings, "HorizontalAlignment", (int)target.HorizontalAlignment);
        target.VerticalAlignment = (MTTableVerticalAlignment)GetSetting(settings, "VerticalAlignment", (int)target.VerticalAlignment);
        target.CharacterColumnHorizontalAlignment = (MTTableHorizontalAlignment)GetSetting(settings, "CharacterColumnHorizontalAlignment", (int)target.CharacterColumnHorizontalAlignment);
        target.CharacterColumnVerticalAlignment = (MTTableVerticalAlignment)GetSetting(settings, "CharacterColumnVerticalAlignment", (int)target.CharacterColumnVerticalAlignment);
        target.HeaderHorizontalAlignment = (MTTableHorizontalAlignment)GetSetting(settings, "HeaderHorizontalAlignment", (int)target.HeaderHorizontalAlignment);
        target.HeaderVerticalAlignment = (MTTableVerticalAlignment)GetSetting(settings, "HeaderVerticalAlignment", (int)target.HeaderVerticalAlignment);
        target.HideCharacterColumnInAllMode = GetSetting(settings, "HideCharacterColumnInAllMode", target.HideCharacterColumnInAllMode);
        target.TextColorMode = (TableTextColorMode)GetSetting(settings, "TextColorMode", (int)target.TextColorMode);
        target.ShowRetainerBreakdown = GetSetting(settings, "ShowRetainerBreakdown", target.ShowRetainerBreakdown);
        
        // Import merged column groups
        if (settings.TryGetValue("MergedColumnGroups", out var mergedColGroupsObj) && mergedColGroupsObj != null)
        {
            target.MergedColumnGroups.Clear();
            var groups = ImportMergedColumnGroups(mergedColGroupsObj);
            target.MergedColumnGroups.AddRange(groups);
        }
        
        // Import merged row groups
        if (settings.TryGetValue("MergedRowGroups", out var mergedRowGroupsObj) && mergedRowGroupsObj != null)
        {
            target.MergedRowGroups.Clear();
            var groups = ImportMergedRowGroups(mergedRowGroupsObj);
            target.MergedRowGroups.AddRange(groups);
        }
        
        // Colors
        target.CharacterColumnColor = ImportColorArray(settings, "CharacterColumnColor");
        target.HeaderColor = ImportColorArray(settings, "HeaderColor");
        target.EvenRowColor = ImportColorArray(settings, "EvenRowColor");
        target.OddRowColor = ImportColorArray(settings, "OddRowColor");
        
        // Hidden characters
        target.HiddenCharacters = ImportHashSet(settings, "HiddenCharacters", target.HiddenCharacters);
        
        // Graph-specific
        target.ColorMode = (Models.GraphColorMode)GetSetting(settings, "ColorMode", (int)target.ColorMode);
        target.LegendWidth = GetSetting(settings, "LegendWidth", target.LegendWidth);
        target.LegendHeightPercent = GetSetting(settings, "LegendHeightPercent", target.LegendHeightPercent);
        target.ShowLegend = GetSetting(settings, "ShowLegend", target.ShowLegend);
        target.LegendCollapsed = GetSetting(settings, "LegendCollapsed", target.LegendCollapsed);
        target.LegendPosition = (MTLegendPosition)GetSetting(settings, "LegendPosition", (int)target.LegendPosition);
        target.GraphType = (MTGraphType)GetSetting(settings, "GraphType", (int)target.GraphType);
        target.ShowXAxisTimestamps = GetSetting(settings, "ShowXAxisTimestamps", target.ShowXAxisTimestamps);
        target.ShowCrosshair = GetSetting(settings, "ShowCrosshair", target.ShowCrosshair);
        target.ShowGridLines = GetSetting(settings, "ShowGridLines", target.ShowGridLines);
        target.ShowCurrentPriceLine = GetSetting(settings, "ShowCurrentPriceLine", target.ShowCurrentPriceLine);
        target.ShowValueLabel = GetSetting(settings, "ShowValueLabel", target.ShowValueLabel);
        target.ValueLabelOffsetX = GetSetting(settings, "ValueLabelOffsetX", target.ValueLabelOffsetX);
        target.ValueLabelOffsetY = GetSetting(settings, "ValueLabelOffsetY", target.ValueLabelOffsetY);
        target.AutoScrollEnabled = GetSetting(settings, "AutoScrollEnabled", target.AutoScrollEnabled);
        target.AutoScrollTimeValue = GetSetting(settings, "AutoScrollTimeValue", target.AutoScrollTimeValue);
        target.AutoScrollTimeUnit = (MTTimeUnit)GetSetting(settings, "AutoScrollTimeUnit", (int)target.AutoScrollTimeUnit);
        target.AutoScrollNowPosition = GetSetting(settings, "AutoScrollNowPosition", target.AutoScrollNowPosition);
        target.ShowControlsDrawer = GetSetting(settings, "ShowControlsDrawer", target.ShowControlsDrawer);
        target.TimeRangeValue = GetSetting(settings, "TimeRangeValue", target.TimeRangeValue);
        target.TimeRangeUnit = (MTTimeUnit)GetSetting(settings, "TimeRangeUnit", (int)target.TimeRangeUnit);
        
        // Update character combo
        if (_characterCombo != null)
        {
            _characterCombo.MultiSelectEnabled = true;
            if (target.UseCharacterFilter && target.SelectedCharacterIds.Count > 0)
            {
                _characterCombo.SetSelection(target.SelectedCharacterIds);
            }
            else
            {
                _characterCombo.SelectAll();
            }
        }
        
        UpdateTitle();
        _pendingTableRefresh = true;
        _graphCacheIsDirty = true;
    }
    
    /// <summary>
    /// Generic helper to import a list of items from various serialized formats.
    /// </summary>
    private static List<T> ImportMergedGroups<T>(object? obj, Func<Dictionary<string, object?>, T?> itemFactory, string typeName) where T : class
    {
        var result = new List<T>();
        if (obj == null) return result;
        
        try
        {
            System.Collections.IEnumerable? enumerable = null;
            
            if (obj is Newtonsoft.Json.Linq.JArray jArray)
                enumerable = jArray;
            else if (obj is System.Collections.IEnumerable e)
                enumerable = e;
            
            if (enumerable == null) return result;
            
            foreach (var item in enumerable)
            {
                var dict = ConvertToDictionary(item);
                if (dict == null) continue;
                
                var parsed = itemFactory(dict);
                if (parsed != null)
                    result.Add(parsed);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"Failed to import {typeName}: {ex.Message}");
        }
        
        return result;
    }
    
    /// <summary>
    /// Imports merged column groups from various serialized formats.
    /// </summary>
    private static List<MergedColumnGroup> ImportMergedColumnGroups(object? obj)
    {
        return ImportMergedGroups(obj, dict =>
        {
            var group = new MergedColumnGroup
            {
                Name = GetSetting(dict, "Name", "Merged") ?? "Merged",
                Width = GetSetting(dict, "Width", 80f),
                Color = ImportColorArray(dict, "Color"),
                ShowInTable = GetSetting(dict, "ShowInTable", true),
                ShowInGraph = GetSetting(dict, "ShowInGraph", true),
                ColumnIndices = ImportList<int>(dict, "ColumnIndices") ?? new List<int>()
            };
            
            return group;
        }, nameof(MergedColumnGroup));
    }
    
    /// <summary>
    /// Imports merged row groups from various serialized formats.
    /// </summary>
    private static List<MergedRowGroup> ImportMergedRowGroups(object? obj)
    {
        return ImportMergedGroups(obj, dict =>
        {
            var group = new MergedRowGroup
            {
                Name = GetSetting(dict, "Name", "Merged") ?? "Merged",
                Color = ImportColorArray(dict, "Color"),
                CharacterIds = ImportList<ulong>(dict, "CharacterIds") ?? new List<ulong>()
            };
            
            return group;
        }, nameof(MergedRowGroup));
    }
}
