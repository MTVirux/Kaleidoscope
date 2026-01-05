using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Models;
using MTGui.Common;
using MTGui.Graph;
using MTGui.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable graph widget for displaying numerical sample data.
/// This is a thin wrapper around MTGui.Graph.MTGraph that maintains backward compatibility
/// with the existing Kaleidoscope API and provides ISettingsProvider integration.
/// </summary>
public class MTGraphWidget : ISettingsProvider
{
    private readonly MTGraph _graph;
    
    // === ISettingsProvider implementation fields ===
    private IGraphWidgetSettings? _boundSettings;
    private Action? _onSettingsChanged;
    private string _settingsName = "Graph Settings";
    private bool _showLegendSettings = true;
    private bool _hideCharacterColorMode;
    
    /// <summary>
    /// Display names for legend positions.
    /// </summary>
    private static readonly string[] LegendPositionNames = 
        { "Outside (right)", "Inside Top-Left", "Inside Top-Right", "Inside Bottom-Left", "Inside Bottom-Right" };
    
    /// <summary>
    /// Display names for auto-scroll time units.
    /// </summary>
    private static readonly string[] AutoScrollTimeUnitNames = 
        { "Seconds", "Minutes", "Hours", "Days", "Weeks" };

    #region Constructors

    /// <summary>
    /// Creates a new MTGraphWidget with default configuration.
    /// </summary>
    public MTGraphWidget() : this(new MTGraphConfig()) { }

    /// <summary>
    /// Creates a new MTGraphWidget with custom configuration.
    /// </summary>
    /// <param name="config">The graph configuration.</param>
    public MTGraphWidget(MTGraphConfig config)
    {
        _graph = new MTGraph(config);
        
        // Forward auto-scroll settings changes
        _graph.OnAutoScrollSettingsChanged += (enabled, value, unit, position) =>
        {
            OnAutoScrollSettingsChanged?.Invoke(enabled, value, unit, position);
            
            // Also update bound settings if available
            if (_boundSettings != null)
            {
                _boundSettings.AutoScrollEnabled = enabled;
                _boundSettings.AutoScrollTimeValue = value;
                _boundSettings.AutoScrollTimeUnit = unit;
                _boundSettings.AutoScrollNowPosition = position;
                _onSettingsChanged?.Invoke();
            }
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the underlying MTGraph instance.
    /// </summary>
    public MTGraph Graph => _graph;
    
    /// <summary>
    /// Gets the current graph configuration.
    /// </summary>
    public MTGraphConfig Config => _graph.Config;
    
    /// <summary>
    /// Gets or sets the groups for the legend. Groups can be toggled to show/hide all their member series.
    /// When set, groups are displayed in the legend before individual series, allowing users to toggle
    /// visibility of all series within a group at once.
    /// </summary>
    public IReadOnlyList<MTGraphSeriesGroup>? Groups
    {
        get => _graph.Groups;
        set => _graph.Groups = value;
    }
    
    /// <summary>
    /// Gets whether the mouse is over an overlay element (legend, controls drawer).
    /// </summary>
    public bool IsMouseOverOverlay => _graph.IsMouseOverOverlay;
    
    /// <summary>
    /// Gets whether the mouse is currently over the inside legend.
    /// </summary>
    public bool IsMouseOverInsideLegend => _graph.IsMouseOverOverlay;
    
    /// <summary>
    /// Gets whether the mouse is currently over the controls drawer.
    /// </summary>
    public bool IsMouseOverControlsDrawer => _graph.IsMouseOverOverlay;
    
    /// <summary>
    /// Gets or sets whether auto-scroll (follow mode) is enabled.
    /// </summary>
    public bool AutoScrollEnabled
    {
        get => _graph.Config.AutoScrollEnabled;
        set => _graph.Config.AutoScrollEnabled = value;
    }
    
    /// <summary>
    /// Gets or sets the auto-scroll time value.
    /// </summary>
    public int AutoScrollTimeValue
    {
        get => _graph.Config.AutoScrollTimeValue;
        set => _graph.Config.AutoScrollTimeValue = value;
    }
    
    /// <summary>
    /// Gets or sets the auto-scroll time unit.
    /// </summary>
    public MTTimeUnit AutoScrollTimeUnit
    {
        get => _graph.Config.AutoScrollTimeUnit;
        set => _graph.Config.AutoScrollTimeUnit = value;
    }
    
    /// <summary>
    /// Gets the current minimum Y-axis value.
    /// </summary>
    public float MinValue => _graph.Config.MinValue;
    
    /// <summary>
    /// Gets the current maximum Y-axis value.
    /// </summary>
    public float MaxValue => _graph.Config.MaxValue;
    
    /// <summary>
    /// Gets or sets the hidden series names.
    /// </summary>
    public IReadOnlyCollection<string> HiddenSeries => GetHiddenSeriesSet();

    #endregion

    #region Events

    /// <summary>
    /// Event fired when auto-scroll settings are changed via the controls drawer.
    /// Parameters: (bool autoScrollEnabled, int timeValue, MTTimeUnit timeUnit, float nowPosition)
    /// </summary>
    public event Action<bool, int, MTTimeUnit, float>? OnAutoScrollSettingsChanged;

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates the Y-axis bounds without recreating the widget.
    /// </summary>
    public void UpdateBounds(float minValue, float maxValue)
    {
        _graph.UpdateBounds(minValue, maxValue);
    }

    /// <summary>
    /// Updates display options from external configuration.
    /// </summary>
    public void UpdateDisplayOptions(
        bool showValueLabel, 
        float valueLabelOffsetX = 0f, 
        float valueLabelOffsetY = 0f, 
        float legendWidth = 140f, 
        bool showLegend = true, 
        MTGraphType graphType = MTGraphType.Area, 
        bool showXAxisTimestamps = true,
        bool showCrosshair = true,
        bool showGridLines = true,
        bool showCurrentPriceLine = true,
        MTLegendPosition legendPosition = MTLegendPosition.InsideTopLeft,
        float legendHeightPercent = 25f,
        bool autoScrollEnabled = false,
        int autoScrollTimeValue = 1,
        MTTimeUnit autoScrollTimeUnit = MTTimeUnit.Hours,
        float autoScrollNowPosition = 75f,
        bool showControlsDrawer = true)
    {
        var config = _graph.Config;
        config.ShowValueLabel = showValueLabel;
        config.ValueLabelOffsetX = valueLabelOffsetX;
        config.ValueLabelOffsetY = valueLabelOffsetY;
        config.LegendWidth = legendWidth;
        config.ShowLegend = showLegend;
        config.GraphType = graphType;
        config.ShowXAxisTimestamps = showXAxisTimestamps;
        config.ShowCrosshair = showCrosshair;
        config.ShowGridLines = showGridLines;
        config.ShowCurrentPriceLine = showCurrentPriceLine;
        config.LegendPosition = legendPosition;
        config.LegendHeightPercent = legendHeightPercent;
        config.AutoScrollEnabled = autoScrollEnabled;
        config.AutoScrollTimeValue = autoScrollTimeValue;
        config.AutoScrollTimeUnit = autoScrollTimeUnit;
        config.AutoScrollNowPosition = autoScrollNowPosition;
        config.ShowControlsDrawer = showControlsDrawer;
    }

    /// <summary>
    /// Sets the hidden series from an external collection.
    /// </summary>
    public void SetHiddenSeries(IEnumerable<string>? seriesNames)
    {
        // Clear all by showing all series first, then hide the ones we want hidden
        foreach (var name in GetHiddenSeriesSet().ToList())
        {
            _graph.ShowSeries(name);
        }
        
        if (seriesNames != null)
        {
            foreach (var name in seriesNames)
            {
                _graph.HideSeries(name);
            }
        }
    }
    
    /// <summary>
    /// Invalidates the cached prepared graph data, forcing recomputation on the next draw.
    /// </summary>
    public void InvalidatePreparedDataCache()
    {
        _graph.ClearCache();
    }

    #endregion

    #region Render Methods

    /// <summary>
    /// Renders the graph with the provided samples using ImPlot (trading platform style).
    /// </summary>
    /// <param name="samples">The sample data to plot.</param>
    public void RenderGraph(IReadOnlyList<float> samples)
    {
        SyncFromBoundSettings();
        _graph.Render(samples);
        SyncToBoundSettings();
    }

    /// <summary>
    /// Renders multiple data series overlaid on the same graph with time-aligned data.
    /// </summary>
    /// <param name="series">List of data series with names and timestamped values.</param>
    public void RenderMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series)
    {
        SyncFromBoundSettings();
        _graph.RenderMultipleSeries(series);
        SyncToBoundSettings();
    }
    
    /// <summary>
    /// Renders multiple data series overlaid on the same graph with time-aligned data, using custom colors.
    /// </summary>
    /// <param name="series">List of data series with names, timestamped values, and optional colors.</param>
    public void RenderMultipleSeries(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples, Vector4? color)> series)
    {
        SyncFromBoundSettings();
        _graph.RenderMultipleSeries(series);
        SyncToBoundSettings();
    }

    #endregion

    #region ISettingsProvider Implementation
    
    /// <inheritdoc />
    public bool HasSettings => true;
    
    /// <inheritdoc />
    public string SettingsName => _settingsName;
    
    /// <summary>
    /// Binds the widget to an IGraphWidgetSettings object for automatic synchronization.
    /// </summary>
    /// <param name="settings">The settings object to bind.</param>
    /// <param name="onSettingsChanged">Callback when settings change.</param>
    /// <param name="settingsName">Display name for the settings section.</param>
    /// <param name="showLegendSettings">Whether to show legend settings in the UI.</param>
    /// <param name="hideCharacterColorMode">Whether to hide the 'Use preferred character colors' option from the color mode dropdown.</param>
    public void BindSettings(
        IGraphWidgetSettings settings,
        Action? onSettingsChanged = null,
        string? settingsName = null,
        bool showLegendSettings = true,
        bool hideCharacterColorMode = false)
    {
        _boundSettings = settings;
        _onSettingsChanged = onSettingsChanged;
        if (settingsName != null) _settingsName = settingsName;
        _showLegendSettings = showLegendSettings;
        _hideCharacterColorMode = hideCharacterColorMode;
        
        SyncFromBoundSettings();
    }
    
    /// <summary>
    /// Synchronizes the internal config from the bound settings object.
    /// </summary>
    public void SyncFromBoundSettings()
    {
        if (_boundSettings == null) return;
        
        var config = _graph.Config;
        config.LegendWidth = _boundSettings.LegendWidth;
        config.LegendHeightPercent = _boundSettings.LegendHeightPercent;
        config.ShowLegend = _boundSettings.ShowLegend;
        config.LegendCollapsed = _boundSettings.LegendCollapsed;
        config.LegendPosition = _boundSettings.LegendPosition;
        config.GraphType = _boundSettings.GraphType;
        config.ShowXAxisTimestamps = _boundSettings.ShowXAxisTimestamps;
        config.ShowCrosshair = _boundSettings.ShowCrosshair;
        config.ShowGridLines = _boundSettings.ShowGridLines;
        config.ShowCurrentPriceLine = _boundSettings.ShowCurrentPriceLine;
        config.ShowValueLabel = _boundSettings.ShowValueLabel;
        config.ValueLabelOffsetX = _boundSettings.ValueLabelOffsetX;
        config.ValueLabelOffsetY = _boundSettings.ValueLabelOffsetY;
        config.AutoScrollEnabled = _boundSettings.AutoScrollEnabled;
        config.AutoScrollTimeValue = _boundSettings.AutoScrollTimeValue;
        config.AutoScrollTimeUnit = _boundSettings.AutoScrollTimeUnit;
        config.AutoScrollNowPosition = _boundSettings.AutoScrollNowPosition;
        config.ShowControlsDrawer = _boundSettings.ShowControlsDrawer;
        config.NumberFormat = _boundSettings.NumberFormat;
    }
    
    /// <summary>
    /// Synchronizes interactive changes from the config back to bound settings.
    /// Called after rendering to persist user interactions like legend collapse toggle.
    /// </summary>
    private void SyncToBoundSettings()
    {
        if (_boundSettings == null) return;
        
        var config = _graph.Config;
        
        // Only sync settings that can change during rendering (via user interaction)
        if (_boundSettings.LegendCollapsed != config.LegendCollapsed)
        {
            _boundSettings.LegendCollapsed = config.LegendCollapsed;
            _onSettingsChanged?.Invoke();
        }
    }
    
    /// <inheritdoc />
    public bool DrawSettings()
    {
        if (_boundSettings == null)
        {
            ImGui.TextDisabled("No settings bound to this graph widget.");
            return false;
        }
        
        var changed = false;
        var settings = _boundSettings;
        var config = _graph.Config;
        
        // Color mode setting
        var colorMode = (int)settings.ColorMode;
        ImGui.SetNextItemWidth(200);
        if (_hideCharacterColorMode)
        {
            // Only show "Don't use" and "Use preferred item colors" options
            if (ImGui.Combo("Color Mode", ref colorMode, "Don't use\0Use preferred item colors\0"))
            {
                settings.ColorMode = (GraphColorMode)colorMode;
                changed = true;
            }
            ShowSettingsTooltip("How to determine series colors: use item/currency preferred colors or default palette.");
        }
        else
        {
            if (ImGui.Combo("Color Mode", ref colorMode, "Don't use\0Use preferred item colors\0Use preferred character colors\0"))
            {
                settings.ColorMode = (GraphColorMode)colorMode;
                changed = true;
            }
            ShowSettingsTooltip("How to determine series colors: use item/currency preferred colors, character preferred colors, or default palette.");
        }
        
        ImGui.Spacing();
        
        // Number format setting
        if (NumberFormatSettingsUI.Draw($"graph_{GetHashCode()}", settings.NumberFormat, "Number Format"))
        {
            config.NumberFormat = settings.NumberFormat;
            changed = true;
        }
        ShowSettingsTooltip("How numbers are formatted in the graph (axis labels, tooltips, etc.).");
        
        ImGui.Spacing();
        
        // Legend settings (only if enabled and multi-series)
        if (_showLegendSettings)
        {
            var showLegend = settings.ShowLegend;
            if (ImGui.Checkbox("Show legend", ref showLegend))
            {
                settings.ShowLegend = showLegend;
                config.ShowLegend = showLegend;
                changed = true;
            }
            ShowSettingsTooltip("Shows a legend panel with series names and values.");
            
            if (showLegend)
            {
                var legendPosition = (int)settings.LegendPosition;
                if (ImGui.Combo("Legend position", ref legendPosition, LegendPositionNames, LegendPositionNames.Length))
                {
                    settings.LegendPosition = (MTLegendPosition)legendPosition;
                    config.LegendPosition = settings.LegendPosition;
                    changed = true;
                }
                ShowSettingsTooltip("Where to display the legend: outside the graph or inside at a corner.");
                
                if (settings.LegendPosition == MTLegendPosition.Outside)
                {
                    var legendWidth = settings.LegendWidth;
                    if (ImGui.SliderFloat("Legend width", ref legendWidth, 60f, 250f, "%.0f px"))
                    {
                        settings.LegendWidth = legendWidth;
                        config.LegendWidth = legendWidth;
                        changed = true;
                    }
                    ShowSettingsTooltip("Width of the scrollable legend panel.");
                }
                else
                {
                    var legendHeight = settings.LegendHeightPercent;
                    if (ImGui.SliderFloat("Legend height", ref legendHeight, 10f, 80f, "%.0f %%"))
                    {
                        settings.LegendHeightPercent = legendHeight;
                        config.LegendHeightPercent = legendHeight;
                        changed = true;
                    }
                    ShowSettingsTooltip("Maximum height of the inside legend as a percentage of the graph height.");
                }
            }
            
            ImGui.Spacing();
        }
        
        // Value label settings
        var showValueLabel = settings.ShowValueLabel;
        if (ImGui.Checkbox("Show current value label", ref showValueLabel))
        {
            settings.ShowValueLabel = showValueLabel;
            config.ShowValueLabel = showValueLabel;
            changed = true;
        }
        ShowSettingsTooltip("Shows the current value as a small label near the latest data point.");
        
        if (showValueLabel)
        {
            var labelOffsetX = settings.ValueLabelOffsetX;
            if (ImGui.SliderFloat("Label X offset", ref labelOffsetX, -100f, 100f, "%.0f"))
            {
                settings.ValueLabelOffsetX = labelOffsetX;
                config.ValueLabelOffsetX = labelOffsetX;
                changed = true;
            }
            ShowSettingsTooltip("Horizontal offset for the value label. Negative = left, positive = right.");
            
            var labelOffsetY = settings.ValueLabelOffsetY;
            if (ImGui.SliderFloat("Label Y offset", ref labelOffsetY, -50f, 50f, "%.0f"))
            {
                settings.ValueLabelOffsetY = labelOffsetY;
                config.ValueLabelOffsetY = labelOffsetY;
                changed = true;
            }
            ShowSettingsTooltip("Vertical offset for the value label. Negative = up, positive = down.");
        }
        
        ImGui.Spacing();
        if (MTTreeHelpers.DrawSection("Graph Style"))
        {
            // Graph type
            var graphType = settings.GraphType;
            if (GraphTypeSelectorWidget.Draw("Graph type", ref graphType))
            {
                settings.GraphType = graphType;
                config.GraphType = graphType;
                changed = true;
            }
            ShowSettingsTooltip("The visual style for the graph (Area, Line, Stairs, Bars).");
            
            // X-axis timestamps
            var showXAxisTimestamps = settings.ShowXAxisTimestamps;
            if (ImGui.Checkbox("Show X-axis timestamps", ref showXAxisTimestamps))
            {
                settings.ShowXAxisTimestamps = showXAxisTimestamps;
                config.ShowXAxisTimestamps = showXAxisTimestamps;
                changed = true;
            }
            ShowSettingsTooltip("Shows time labels on the X-axis.");
            
            // Crosshair
            var showCrosshair = settings.ShowCrosshair;
            if (ImGui.Checkbox("Show crosshair", ref showCrosshair))
            {
                settings.ShowCrosshair = showCrosshair;
                config.ShowCrosshair = showCrosshair;
                changed = true;
            }
            ShowSettingsTooltip("Shows crosshair lines when hovering over the graph.");
            
            // Grid lines
            var showGridLines = settings.ShowGridLines;
            if (ImGui.Checkbox("Show grid lines", ref showGridLines))
            {
                settings.ShowGridLines = showGridLines;
                config.ShowGridLines = showGridLines;
                changed = true;
            }
            ShowSettingsTooltip("Shows horizontal grid lines for easier value reading.");
            
            // Current price line
            var showCurrentPriceLine = settings.ShowCurrentPriceLine;
            if (ImGui.Checkbox("Show current price line", ref showCurrentPriceLine))
            {
                settings.ShowCurrentPriceLine = showCurrentPriceLine;
                config.ShowCurrentPriceLine = showCurrentPriceLine;
                changed = true;
            }
            ShowSettingsTooltip("Shows a horizontal line at the current (latest) value.");
            
            MTTreeHelpers.EndSection();
        }
        
        ImGui.Spacing();
        if (MTTreeHelpers.DrawSection("Auto-Scroll"))
        {
            var autoScrollEnabled = settings.AutoScrollEnabled;
            if (ImGui.Checkbox("Enable auto-scroll", ref autoScrollEnabled))
            {
                settings.AutoScrollEnabled = autoScrollEnabled;
                config.AutoScrollEnabled = autoScrollEnabled;
                changed = true;
            }
            ShowSettingsTooltip("When enabled, the graph automatically scrolls to show the most recent data.");
            
            if (autoScrollEnabled)
            {
                // Time value
                var timeValue = settings.AutoScrollTimeValue;
                if (ImGui.InputInt("Time window value", ref timeValue))
                {
                    timeValue = Math.Max(1, timeValue);
                    settings.AutoScrollTimeValue = timeValue;
                    config.AutoScrollTimeValue = timeValue;
                    changed = true;
                }
                
                // Time unit
                var timeUnit = (int)settings.AutoScrollTimeUnit;
                if (ImGui.Combo("Time window unit", ref timeUnit, AutoScrollTimeUnitNames, AutoScrollTimeUnitNames.Length))
                {
                    settings.AutoScrollTimeUnit = (MTTimeUnit)timeUnit;
                    config.AutoScrollTimeUnit = settings.AutoScrollTimeUnit;
                    changed = true;
                }
                
                // Now position
                var nowPosition = settings.AutoScrollNowPosition;
                if (ImGui.SliderFloat("'Now' position", ref nowPosition, 0f, 100f, "%.0f %%"))
                {
                    settings.AutoScrollNowPosition = nowPosition;
                    config.AutoScrollNowPosition = nowPosition;
                    changed = true;
                }
                ShowSettingsTooltip("Position of 'now' on the X-axis. 0% = left edge, 100% = right edge.");
            }
            
            var showControlsDrawer = settings.ShowControlsDrawer;
            if (ImGui.Checkbox("Show controls drawer", ref showControlsDrawer))
            {
                settings.ShowControlsDrawer = showControlsDrawer;
                config.ShowControlsDrawer = showControlsDrawer;
                changed = true;
            }
            ShowSettingsTooltip("Shows a collapsible controls panel in the bottom-left corner of the graph.");
            
            MTTreeHelpers.EndSection();
        }
        
        if (changed)
        {
            _onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
    /// <summary>
    /// Shows a tooltip for a settings control when hovered.
    /// </summary>
    private static void ShowSettingsTooltip(string tooltip)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
            ImGui.TextUnformatted(tooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Gets the set of hidden series names from the underlying graph.
    /// </summary>
    private HashSet<string> GetHiddenSeriesSet()
    {
        return new HashSet<string>(_graph.HiddenSeries);
    }

    #endregion
}
