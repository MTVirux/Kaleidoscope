using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.DataTracker;

/// <summary>
/// Generic component for tracking any data type.
/// Manages sampling, persistence, and display of tracked data.
/// Uses event-driven updates from InventoryChangeService instead of polling.
/// </summary>
public class DataTrackerComponent : IDisposable
{
    private readonly DataTrackerHelper _helper;
    private readonly CharacterPickerWidget _characterPicker;
    private readonly ImplotGraphWidget _graphWidget;
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly TrackedDataRegistry _registry;
    private readonly InventoryChangeService? _inventoryChangeService;

    private bool _pointsPopupOpen = false;
    private volatile bool _pendingUpdate = true; // Start with pending to get initial sample
    private bool _disposed = false;

    // Graph bounds (editable via settings)
    private float _graphMinValue = 0f;
    private float _graphMaxValue;

    /// <summary>
    /// The data type this component tracks.
    /// </summary>
    public TrackedDataType DataType { get; }

    /// <summary>
    /// The definition for the tracked data type.
    /// </summary>
    public TrackedDataDefinition? Definition => _registry.GetDefinition(DataType);

    /// <summary>
    /// Gets the underlying helper for direct data access.
    /// </summary>
    public DataTrackerHelper Helper => _helper;

    private Configuration Config => _configService.Config;

    /// <summary>
    /// Gets settings for this specific data type.
    /// </summary>
    private DataTrackerSettings GetSettings()
    {
        if (!Config.DataTrackerSettings.TryGetValue(DataType, out var settings))
        {
            settings = new DataTrackerSettings();
            Config.DataTrackerSettings[DataType] = settings;
        }
        return settings;
    }

    public float GraphMinValue
    {
        get => _graphMinValue;
        set
        {
            _graphMinValue = value;
            _graphWidget.UpdateBounds(_graphMinValue, _graphMaxValue);
        }
    }

    public float GraphMaxValue
    {
        get => _graphMaxValue;
        set
        {
            _graphMaxValue = value;
            _graphWidget.UpdateBounds(_graphMinValue, _graphMaxValue);
        }
    }

    /// <summary>
    /// Initializes the DataTrackerComponent with shared database access from SamplerService.
    /// </summary>
    public DataTrackerComponent(
        TrackedDataType dataType,
        SamplerService samplerService,
        ConfigurationService configService,
        TrackedDataRegistry registry,
        InventoryChangeService? inventoryChangeService = null)
    {
        DataType = dataType;
        _samplerService = samplerService;
        _configService = configService;
        _registry = registry;
        _inventoryChangeService = inventoryChangeService;

        var definition = registry.GetDefinition(dataType);
        _graphMaxValue = definition?.MaxValue ?? 999_999_999;

        // Share the database service from SamplerService to avoid duplicate connections
        _helper = new DataTrackerHelper(
            dataType,
            samplerService.DbService,
            registry,
            ConfigStatic.GilTrackerMaxSamples,
            0f);

        _characterPicker = new CharacterPickerWidget(_helper);

        // Initialize graph widget using current graph bounds
        var plotId = $"dataplot_{dataType}";
        _graphWidget = new ImplotGraphWidget(new ImplotGraphWidget.GraphConfig
        {
            MinValue = _graphMinValue,
            MaxValue = _graphMaxValue,
            PlotId = plotId,
            NoDataText = "No data yet.",
            FloatEpsilon = ConfigStatic.FloatEpsilon
        });
        
        // Subscribe to auto-scroll settings changes from the controls drawer
        _graphWidget.OnAutoScrollSettingsChanged += OnAutoScrollSettingsChanged;

        // Subscribe to inventory change events for event-driven updates
        if (_inventoryChangeService != null)
        {
            _inventoryChangeService.OnValuesChanged += OnValuesChanged;
        }
    }
    
    private void OnAutoScrollSettingsChanged(bool enabled, int timeValue, AutoScrollTimeUnit timeUnit, float nowPosition)
    {
        var settings = GetSettings();
        settings.AutoScrollEnabled = enabled;
        settings.AutoScrollTimeValue = timeValue;
        settings.AutoScrollTimeUnit = timeUnit;
        settings.AutoScrollNowPosition = nowPosition;
        _configService.Save();
    }

    private void OnValuesChanged(IReadOnlyDictionary<TrackedDataType, long> values)
    {
        // Only flag update if our data type changed
        if (values.ContainsKey(DataType))
        {
            _pendingUpdate = true;
        }
    }

    public bool HasDb => _samplerService.HasDb;
    
    /// <summary>
    /// Gets the hidden series names from the graph widget.
    /// </summary>
    public IReadOnlyCollection<string> HiddenSeries => _graphWidget.HiddenSeries;
    
    /// <summary>
    /// Sets the hidden series names on the graph widget.
    /// </summary>
    public void SetHiddenSeries(IEnumerable<string>? seriesNames) => _graphWidget.SetHiddenSeries(seriesNames);

    public void ClearAllData()
    {
        try
        {
            _helper.ClearAllData();
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to clear {DataType} data", ex);
        }
    }

    public string? ExportCsv()
    {
        try
        {
            return _helper.ExportCsv();
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to export {DataType} CSV", ex);
            return null;
        }
    }

    public int CleanUnassociatedCharacters()
    {
        try
        {
            return _helper.CleanUnassociatedCharacterData();
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to clean unassociated {DataType} characters", ex);
            return 0;
        }
    }

    public void Draw()
    {
        var settings = GetSettings();

        // Sample when flagged by inventory change events (event-driven, not polling)
        if (_pendingUpdate)
        {
            try
            {
                // Delegate to helper for sampling and persistence
                using (ProfilerService.BeginStaticChildScope("SampleFromGame"))
                {
                    _helper.SampleFromGame();
                }
                _pendingUpdate = false;
            }
            catch (Exception ex)
            {
                LogService.Debug($"[DataTrackerComponent:{DataType}] Sampling error: {ex.Message}");
            }
        }

        // Draw the character picker widget (if not hidden)
        if (!settings.HideCharacterSelector)
        {
            _characterPicker.Draw();
        }

        // Update graph display options from settings
        _graphWidget.UpdateDisplayOptions(
            settings.ShowValueLabel,
            settings.ValueLabelOffsetX,
            settings.ValueLabelOffsetY,
            settings.LegendWidth,
            settings.ShowLegend,
            settings.GraphType,
            settings.ShowXAxisTimestamps,
            legendPosition: settings.LegendPosition,
            legendHeightPercent: settings.LegendHeightPercent,
            autoScrollEnabled: settings.AutoScrollEnabled,
            autoScrollTimeValue: settings.AutoScrollTimeValue,
            autoScrollTimeUnit: settings.AutoScrollTimeUnit,
            autoScrollNowPosition: settings.AutoScrollNowPosition,
            showControlsDrawer: settings.ShowControlsDrawer);

        // Calculate time cutoff if time range filtering is enabled
        DateTime? timeCutoff = null;
        if (settings.TimeRangeUnit != TimeRangeUnit.All)
        {
            timeCutoff = CalculateTimeCutoff(settings);
        }

        // Draw the sample graph widget
        if (settings.ShowMultipleLines && _helper.SelectedCharacterId == 0)
        {
            // Multi-line mode: show each character as a separate line
            IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series;
            using (ProfilerService.BeginStaticChildScope("GetAllCharacterSeries"))
            {
                series = _helper.GetAllCharacterSeries(timeCutoff);
            }
            _graphWidget.DrawMultipleSeries(series);
        }
        else
        {
            // Single line mode
            IReadOnlyList<float> samples;
            using (ProfilerService.BeginStaticChildScope("GetSamples"))
            {
                samples = timeCutoff.HasValue
                    ? _helper.GetFilteredSamples(timeCutoff.Value)
                    : _helper.Samples;
            }
            _graphWidget.Draw(samples);
        }

        // Debug: right-click the plot to open a popup listing all data points + timestamps
#if DEBUG
        try
        {
            if (StateService.IsEditModeStatic && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                ImGui.OpenPopup($"datatracker_{DataType}_popup");
                _pointsPopupOpen = true;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"Debug popup error: {ex.Message}");
        }

        if (ImGui.BeginPopupModal($"datatracker_{DataType}_popup", ref _pointsPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            try
            {
                var pts = _helper.GetPoints();
                if (pts.Count == 0)
                {
                    ImGui.TextUnformatted("No data points available.");
                }
                else
                {
                    var displayName = Definition?.DisplayName ?? DataType.ToString();
                    ImGui.TextUnformatted($"{displayName} data: " + (_helper.SelectedCharacterId == 0 ? "All Characters" : _helper.SelectedCharacterId.ToString()));
                    ImGui.Separator();
                    ImGui.BeginChild($"datatracker_{DataType}_child", ConfigStatic.GilTrackerPointsPopupSize, true);
                    for (var i = 0; i < pts.Count; i++)
                    {
                        var p = pts[i];
                        ImGui.TextUnformatted($"{i}: {p.ts:O}  {p.value:N0}");
                    }
                    ImGui.EndChild();
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"Debug points popup error: {ex.Message}");
            }

            if (ImGui.Button("Close"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
#endif
    }

    private static DateTime CalculateTimeCutoff(DataTrackerSettings settings)
    {
        var now = DateTime.UtcNow;
        var value = settings.TimeRangeValue;

        return settings.TimeRangeUnit switch
        {
            TimeRangeUnit.Minutes => now.AddMinutes(-value),
            TimeRangeUnit.Hours => now.AddHours(-value),
            TimeRangeUnit.Days => now.AddDays(-value),
            TimeRangeUnit.Weeks => now.AddDays(-value * 7),
            TimeRangeUnit.Months => now.AddMonths(-value),
            _ => DateTime.MinValue
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _graphWidget.OnAutoScrollSettingsChanged -= OnAutoScrollSettingsChanged;
        if (_inventoryChangeService != null)
        {
            _inventoryChangeService.OnValuesChanged -= OnValuesChanged;
        }
    }
}
