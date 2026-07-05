using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using Kaleidoscope.Gui.Widgets.Common;
using Kaleidoscope.Gui.Widgets.Graph;
using Kaleidoscope.Gui.Widgets.Table;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// DataTool partial class containing tool settings, context menus, and import/export logic.
/// </summary>
public sealed partial class DataTool
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
                        _tableView.RequestRefresh();
                        _graphView.MarkDirty();
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
                        _tableView.RequestRefresh();
                        _graphView.MarkDirty();
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
            _tableView.RequestRefresh();
            _graphView.MarkDirty();
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
            _tableView.RequestRefresh();
            _graphView.MarkDirty();
            NotifyToolSettingsChanged();
        }
        
        // Include retainers
        var includeRetainers = settings.IncludeRetainers;
        if (ImGui.Checkbox("Include Retainers", ref includeRetainers))
        {
            settings.IncludeRetainers = includeRetainers;
            _tableView.RequestRefresh();
            _graphView.MarkDirty();
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
                _tableView.RequestRefresh();
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
                _graphView.MarkDirty();
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
            _tableView.RequestRefresh();
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
            column => _tableWidget.GetColumnHeader(column),
            onSettingsChanged: () => NotifyToolSettingsChanged(),
            onRefreshNeeded: () => { _tableView.RequestRefresh(); _graphView.MarkDirty(); },
            sectionTitle: "Item / Currency Management",
            emptyMessage: "No items or currencies configured.",
            itemLabel: "Item",
            currencyLabel: "Currency",
            state: _columnState,
            isItemHistoricalTrackingEnabled: (itemId) => _configService.Config.ItemsWithHistoricalTracking.Contains(itemId),
            onItemHistoricalTrackingToggled: (itemId, enabled) =>
            {
                if (enabled)
                {
                    _configService.Config.ItemsWithHistoricalTracking.Add(itemId);
                    // Seed a baseline sample right away instead of waiting for the next quantity change.
                    _itemCountHistoryService?.RequestSample(itemId);
                }
                else
                {
                    _configService.Config.ItemsWithHistoricalTracking.Remove(itemId);
                }
                _configService.MarkDirty();
                _tableView.RequestRefresh();
                _graphView.MarkDirty();
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
                _tableView.RequestRefresh();
                _graphView.MarkDirty();
            });
        
        // Source Merging
        // Compute available row identifiers based on grouping mode
        var currentGroupingMode = settings.GroupingMode;
        var availableCharIds = _tableView.CachedTableData?.Rows?.Select(r => r.CharacterId).Distinct().ToList() 
                               ?? new List<ulong>();
        
        // For non-Character modes, compute available group keys
        List<string>? availableGroupKeys = null;
        if (currentGroupingMode != TableGroupingMode.Character && _tableView.CachedTableData?.Rows != null)
        {
            availableGroupKeys = currentGroupingMode switch
            {
                TableGroupingMode.World => _tableView.CachedTableData.Rows
                    .Select(r => string.IsNullOrEmpty(r.WorldName) ? "Unknown World" : r.WorldName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                TableGroupingMode.DataCenter => _tableView.CachedTableData.Rows
                    .Select(r => string.IsNullOrEmpty(r.DataCenterName) ? "Unknown DC" : r.DataCenterName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                TableGroupingMode.Region => _tableView.CachedTableData.Rows
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
            onRefreshNeeded: () => { _tableView.RequestRefresh(); _graphView.MarkDirty(); },
            state: _mergeRowState);
        
        // Special Grouping
        SpecialGroupingWidget.Draw(
            settings.SpecialGrouping,
            settings.Columns,
            onSettingsChanged: () => NotifyToolSettingsChanged(),
            onRefreshNeeded: () => { _tableView.RequestRefresh(); _graphView.MarkDirty(); },
            onAddColumn: (id, isCurrency) => AddColumn(id, isCurrency));
    }
    
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var settings = _instanceSettings;
        
        var columns = ColumnManagementWidget.ExportColumns(settings.Columns);
        
        var mergedColumnGroups = settings.MergedColumnGroups.Select(g =>
        {
            var d = new Dictionary<string, object?>
            {
                ["Name"] = g.Name,
                ["ColumnIndices"] = g.ColumnIndices.ToList(),
                ["Width"] = g.Width,
                ["ShowInTable"] = g.ShowInTable,
                ["ShowInGraph"] = g.ShowInGraph,
                ["DisplayOrder"] = g.DisplayOrder
            };
            ExportColorArray(d, "Color", g.Color);
            return d;
        }).ToList();
        
        var mergedRowGroups = settings.MergedRowGroups.Select(g =>
        {
            var d = new Dictionary<string, object?>
            {
                ["Name"] = g.Name,
                ["CharacterIds"] = g.CharacterIds.ToList()
            };
            ExportColorArray(d, "Color", g.Color);
            return d;
        }).ToList();
        
        var result = new Dictionary<string, object?>
        {
            // View mode
            ["ViewMode"] = (int)settings.ViewMode,
            
            // Shared settings
            ["Columns"] = columns,
            ["IncludeRetainers"] = settings.IncludeRetainers,
            ["ShowActionButtons"] = settings.ShowActionButtons,
            ["HideZeroRows"] = settings.HideZeroRows,
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
            ["SortColumnIndex"] = settings.SortColumnIndex,
            ["SortAscending"] = settings.SortAscending,
            ["SortColumns"] = settings.SortColumns.Select(sc => new Dictionary<string, object?>
            {
                ["ColumnIndex"] = sc.ColumnIndex,
                ["Ascending"] = sc.Ascending
            }).ToList(),
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
            ["ShowRetainerBreakdownInGraph"] = settings.ShowRetainerBreakdownInGraph
        };

        // Graph-specific (shared serializer; graph number format is exported above)
        GraphSettingsSerializer.Export(settings, result);

        // Colors (using array format helper)
        ExportColorArray(result, "CharacterColumnColor", settings.CharacterColumnColor);
        ExportColorArray(result, "HeaderColor", settings.HeaderColor);
        ExportColorArray(result, "EvenRowColor", settings.EvenRowColor);
        ExportColorArray(result, "OddRowColor", settings.OddRowColor);
        
        return result;
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
        target.HideZeroRows = GetSetting(settings, "HideZeroRows", target.HideZeroRows);
        
        // Table number format
        if (settings.ContainsKey("TableNumberFormatStyle"))
        {
            target.TableNumberFormat.Style = (NumberFormatStyle)GetSetting(settings, "TableNumberFormatStyle", (int)target.TableNumberFormat.Style);
            target.TableNumberFormat.DecimalPlaces = GetSetting(settings, "TableNumberFormatDecimalPlaces", target.TableNumberFormat.DecimalPlaces);
        }
        
        // Graph number format
        if (settings.ContainsKey("GraphNumberFormatStyle"))
        {
            target.GraphNumberFormat.Style = (NumberFormatStyle)GetSetting(settings, "GraphNumberFormatStyle", (int)target.GraphNumberFormat.Style);
            target.GraphNumberFormat.DecimalPlaces = GetSetting(settings, "GraphNumberFormatDecimalPlaces", target.GraphNumberFormat.DecimalPlaces);
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
        
        // Import multi-column sort entries
        if (settings.TryGetValue("SortColumns", out var sortColumnsObj) && sortColumnsObj is System.Collections.IEnumerable sortColumnsList)
        {
            target.SortColumns.Clear();
            foreach (var item in sortColumnsList)
            {
                var dict = ConvertToDictionary(item);
                if (dict != null)
                {
                    target.SortColumns.Add(new SortColumnEntry
                    {
                        ColumnIndex = GetSetting(dict, "ColumnIndex", 0),
                        Ascending = GetSetting(dict, "Ascending", true)
                    });
                }
            }
        }
        
        target.UseFullNameWidth = GetSetting(settings, "UseFullNameWidth", target.UseFullNameWidth);
        target.AutoSizeEqualColumns = GetSetting(settings, "AutoSizeEqualColumns", target.AutoSizeEqualColumns);
        target.HorizontalAlignment = (TableHorizontalAlignment)GetSetting(settings, "HorizontalAlignment", (int)target.HorizontalAlignment);
        target.VerticalAlignment = (TableVerticalAlignment)GetSetting(settings, "VerticalAlignment", (int)target.VerticalAlignment);
        target.CharacterColumnHorizontalAlignment = (TableHorizontalAlignment)GetSetting(settings, "CharacterColumnHorizontalAlignment", (int)target.CharacterColumnHorizontalAlignment);
        target.CharacterColumnVerticalAlignment = (TableVerticalAlignment)GetSetting(settings, "CharacterColumnVerticalAlignment", (int)target.CharacterColumnVerticalAlignment);
        target.HeaderHorizontalAlignment = (TableHorizontalAlignment)GetSetting(settings, "HeaderHorizontalAlignment", (int)target.HeaderHorizontalAlignment);
        target.HeaderVerticalAlignment = (TableVerticalAlignment)GetSetting(settings, "HeaderVerticalAlignment", (int)target.HeaderVerticalAlignment);
        target.HideCharacterColumnInAllMode = GetSetting(settings, "HideCharacterColumnInAllMode", target.HideCharacterColumnInAllMode);
        target.TextColorMode = (TableTextColorMode)GetSetting(settings, "TextColorMode", (int)target.TextColorMode);
        target.ShowRetainerBreakdown = GetSetting(settings, "ShowRetainerBreakdown", target.ShowRetainerBreakdown);
        target.ShowRetainerBreakdownInGraph = GetSetting(settings, "ShowRetainerBreakdownInGraph", target.ShowRetainerBreakdownInGraph);
        
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
        
        // Graph-specific (shared serializer; graph number format is imported above)
        GraphSettingsSerializer.Import(target, settings);
        
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
        _tableView.RequestRefresh();
        _graphView.MarkDirty();
        
        // Tell the table widget to discard its cached column widths so it picks up
        // the freshly imported values on the next frame instead of overwriting them.
        _tableWidget.ResetColumnWidthState();
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
            LogService.Debug(LogCategory.UI, $"[DataTool] Failed to import {typeName}: {ex.Message}");
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
                ColumnIndices = ImportList<int>(dict, "ColumnIndices") ?? new List<int>(),
                DisplayOrder = GetSetting(dict, "DisplayOrder", -1)
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
