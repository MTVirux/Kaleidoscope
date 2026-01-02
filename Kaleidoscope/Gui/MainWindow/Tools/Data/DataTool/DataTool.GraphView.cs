using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;
using MTGui.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// DataTool partial class containing graph view rendering logic.
/// 
/// Series loading methods are in DataTool.GraphSeriesLoaders.cs.
/// </summary>
public partial class DataTool
{
    private bool _cachedShowRetainerBreakdownInGraph;

    private void DrawGraphView()
    {
        using (ProfilerService.BeginStaticChildScope("GraphView"))
        {
            _graphWidget.SyncFromBoundSettings();
            
            if (NeedsGraphCacheRefresh())
            {
                using (ProfilerService.BeginStaticChildScope("RefreshGraphData"))
                {
                    RefreshGraphData();
                }
                
                _graphWidget.Groups = _cachedSeriesGroups;
            }
            
            if (_cachedSeriesData != null && _cachedSeriesData.Count > 0)
            {
                using (ProfilerService.BeginStaticChildScope("RenderGraph"))
                {
                    _graphWidget.RenderMultipleSeries(_cachedSeriesData);
                }
            }
            else
            {
                if (Settings.Columns.Count == 0)
                {
                    ImGui.TextDisabled("No items or currencies configured. Add some to start tracking.");
                }
                else
                {
                    ImGui.TextDisabled("No historical data available.");
                }
            }
        }
    }
    
    private bool NeedsGraphCacheRefresh()
    {
        if (_graphCacheIsDirty) return true;
        
        var settings = Settings;
        if (_cachedSeriesCount != settings.Columns.Count) return true;
        if (_cachedTimeRangeValue != settings.TimeRangeValue) return true;
        if (_cachedTimeRangeUnit != settings.TimeRangeUnit) return true;
        if (_cachedIncludeRetainers != settings.IncludeRetainers) return true;
        if (_cachedShowRetainerBreakdownInGraph != settings.ShowRetainerBreakdownInGraph) return true;
        if (_cachedGroupingMode != settings.GroupingMode) return true;
        if (_cachedNameFormat != _configService.Config.CharacterNameFormat) return true;
        
        return (DateTime.UtcNow - _lastGraphRefresh).TotalSeconds > 5.0;
    }
    
    private void RefreshGraphData()
    {
        var settings = Settings;
        
        _lastGraphRefresh = DateTime.UtcNow;
        _cachedSeriesCount = settings.Columns.Count;
        _cachedTimeRangeValue = settings.TimeRangeValue;
        _cachedTimeRangeUnit = settings.TimeRangeUnit;
        _cachedIncludeRetainers = settings.IncludeRetainers;
        _cachedShowRetainerBreakdownInGraph = settings.ShowRetainerBreakdownInGraph;
        _cachedNameFormat = _configService.Config.CharacterNameFormat;
        _cachedGroupingMode = settings.GroupingMode;
        _graphCacheIsDirty = false;
        
        var mergedIndicesWithGraph = new HashSet<int>();
        foreach (var group in settings.MergedColumnGroups.Where(g => g.ShowInGraph))
        {
            foreach (var idx in group.ColumnIndices)
            {
                mergedIndicesWithGraph.Add(idx);
            }
        }
        
        List<ItemColumnConfig> series;
        using (ProfilerService.BeginStaticChildScope("ApplyGroupingFilter"))
        {
            var filteredColumns = SpecialGroupingHelper.ApplySpecialGroupingFilter(settings.Columns, settings.SpecialGrouping);
            series = filteredColumns
                .Where((c, idx) => c.ShowInGraph && !mergedIndicesWithGraph.Contains(idx))
                .ToList();
        }
        
        var timeRange = GetTimeRange();
        var startTime = timeRange.HasValue ? DateTime.UtcNow - timeRange.Value : (DateTime?)null;
        
        HashSet<ulong>? allowedCharacters = null;
        if (settings.UseCharacterFilter && settings.SelectedCharacterIds.Count > 0)
        {
            allowedCharacters = settings.SelectedCharacterIds.ToHashSet();
        }
        
        var seriesList = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)>();
        
        var seriesByItem = new Dictionary<string, List<string>>();
        var itemColors = new Dictionary<string, Vector4>();
        
        var totalItemCount = series.Count + settings.MergedColumnGroups.Count(g => g.ShowInGraph);
        var isSingleItem = totalItemCount == 1;
        
        using (ProfilerService.BeginStaticChildScope("LoadAllSeries"))
        {
            var itemIndex = 0;
            foreach (var seriesConfig in series)
            {
                var itemName = GetSeriesDisplayName(seriesConfig);
                var seriesData = LoadSeriesData(seriesConfig, settings, startTime, allowedCharacters, isSingleItem);
                if (seriesData != null)
                {
                    if (!isSingleItem)
                    {
                        if (!seriesByItem.ContainsKey(itemName))
                        {
                            seriesByItem[itemName] = new List<string>();
                            var color = GetEffectiveSeriesColor(seriesConfig, settings, itemIndex);
                            itemColors[itemName] = color;
                        }
                        foreach (var s in seriesData)
                        {
                            seriesByItem[itemName].Add(s.name);
                        }
                    }
                    seriesList.AddRange(seriesData);
                }
                itemIndex++;
            }
            
            foreach (var group in settings.MergedColumnGroups.Where(g => g.ShowInGraph))
            {
                var groupName = group.Name;
                var mergedSeriesData = LoadMergedSeriesData(group, settings, startTime, allowedCharacters, isSingleItem);
                if (mergedSeriesData != null)
                {
                    if (!isSingleItem)
                    {
                        if (!seriesByItem.ContainsKey(groupName))
                        {
                            seriesByItem[groupName] = new List<string>();
                            if (group.Color.HasValue)
                            {
                                itemColors[groupName] = group.Color.Value;
                            }
                            else
                            {
                                itemColors[groupName] = GetDefaultSeriesColor(itemIndex);
                            }
                        }
                        foreach (var s in mergedSeriesData)
                        {
                            seriesByItem[groupName].Add(s.name);
                        }
                    }
                    seriesList.AddRange(mergedSeriesData);
                }
                itemIndex++;
            }
        }
        
        _cachedSeriesData = seriesList.Count > 0 ? seriesList : null;
        
        // Build groups for the legend (only when there are multiple items with multiple series each)
        if (!isSingleItem && seriesByItem.Count > 1)
        {
            var groups = new List<MTGraphSeriesGroup>();
            foreach (var (itemName, seriesNames) in seriesByItem)
            {
                // Only create a group if the item has multiple series
                if (seriesNames.Count > 1)
                {
                    var color = itemColors.TryGetValue(itemName, out var c) 
                        ? new Vector3(c.X, c.Y, c.Z) 
                        : new Vector3(0.6f, 0.6f, 0.6f);
                    
                    groups.Add(new MTGraphSeriesGroup
                    {
                        Name = itemName,
                        Color = color,
                        SeriesNames = seriesNames
                    });
                }
            }
            _cachedSeriesGroups = groups.Count > 0 ? groups : null;
        }
        else
        {
            _cachedSeriesGroups = null;
        }
    }
    
    private TimeSpan? GetTimeRange()
    {
        var settings = Settings;
        return TimeRangeSelectorWidget.GetTimeSpan(settings.TimeRangeValue, settings.TimeRangeUnit);
    }
}
