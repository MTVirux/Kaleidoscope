using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Libs;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.GilTracker;

/// <summary>
/// Core component for gil tracking functionality.
/// Manages sampling, persistence, and display of gil data.
/// </summary>
public class GilTrackerComponent
{
        private readonly GilTrackerHelper _helper;
        private readonly CharacterPickerWidget _characterPicker;
        private readonly SampleGraphWidget _graphWidget;
        private readonly SamplerService _samplerService;
        private readonly ConfigurationService _configService;

        // Expose DB path so callers can reuse the same DB file when creating multiple UI instances.
        public string? DbPath => _dbPath;
        private bool _pointsPopupOpen = false;
        private DateTime _lastSampleTime = DateTime.MinValue;
        private int _sampleIntervalMs = ConfigStatic.DefaultSamplerIntervalMs;

        private readonly string? _dbPath;

        // Graph bounds (editable via settings)
        private float _graphMinValue = 0f;
        private float _graphMaxValue = ConfigStatic.GilTrackerMaxGil;

        private Configuration Config => _configService.Config;

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
        /// Initializes the GilTrackerComponent with shared database access from SamplerService.
        /// </summary>
        public GilTrackerComponent(FilenameService filenameService, SamplerService samplerService, ConfigurationService configService)
        {
            _samplerService = samplerService;
            _configService = configService;
            _dbPath = filenameService.DatabasePath;
            // Share the database service from SamplerService to avoid duplicate connections
            _helper = new GilTrackerHelper(samplerService.DbService, ConfigStatic.GilTrackerMaxSamples, ConfigStatic.GilTrackerStartingValue);
            _characterPicker = new CharacterPickerWidget(_helper);
            // Initialize graph widget using current graph bounds
            _graphWidget = new SampleGraphWidget(new SampleGraphWidget.GraphConfig
            {
                MinValue = _graphMinValue,
                MaxValue = _graphMaxValue,
                PlotId = "gilplot",
                NoDataText = "No data yet.",
                FloatEpsilon = ConfigStatic.FloatEpsilon
            });
        }

        public bool HasDb => !string.IsNullOrEmpty(_dbPath);

        public void ClearAllData()
        {
            try
            {
                _helper.ClearAllData();
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to clear GilTracker data", ex);
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
                LogService.Error("Failed to export CSV", ex);
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
                LogService.Error("Failed to clean unassociated characters", ex);
                return 0;
            }
        }



        public void Draw()
        {
            // Try to sample from the game's currency manager at most once per _sampleIntervalMs.
            try
            {
                _sampleIntervalMs = Math.Max(1, _samplerService.IntervalMs);

                var now = DateTime.UtcNow;
                if ((now - _lastSampleTime).TotalMilliseconds >= _sampleIntervalMs)
                {
                    // Delegate to helper for sampling and persistence, maintain lastSampleTime in UI
                    _helper.SampleFromGame();
                    _lastSampleTime = now;
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[GilTrackerComponent] Sampling error: {ex.Message}");
            }

            // DB buttons moved to Config Window (Data Management)

            // Draw the character picker widget (if not hidden)
            if (!Config.GilTrackerHideCharacterSelector)
            {
                _characterPicker.Draw();
            }

            // Update graph display options from config
            _graphWidget.UpdateDisplayOptions(
                Config.GilTrackerShowEndGap,
                Config.GilTrackerEndGapPercent,
                Config.GilTrackerShowValueLabel,
                Config.GilTrackerValueLabelOffsetX,
                Config.GilTrackerValueLabelOffsetY,
                Config.GilTrackerAutoScaleGraph);

            // Calculate time cutoff if time range filtering is enabled
            DateTime? timeCutoff = null;
            if (Config.GilTrackerTimeRangeUnit != TimeRangeUnit.All)
            {
                timeCutoff = CalculateTimeCutoff();
            }

            // Draw the sample graph widget
            if (Config.GilTrackerShowMultipleLines && _helper.SelectedCharacterId == 0)
            {
                // Multi-line mode: show each character as a separate line
                var series = _helper.GetAllCharacterSeries(timeCutoff);
                _graphWidget.DrawMultipleSeries(series);
            }
            else
            {
                // Single line mode
                var samples = timeCutoff.HasValue
                    ? _helper.GetFilteredSamples(timeCutoff.Value)
                    : _helper.Samples;
                _graphWidget.Draw(samples);
            }

            // Debug: right-click the plot to open a popup listing all data points + timestamps
            // Only available in edit mode to avoid accidental activation during normal use
            #if DEBUG
            try
            {
                if (StateService.IsEditModeStatic && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    ImGui.OpenPopup("giltracker_points_popup");
                    _pointsPopupOpen = true;
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"Debug popup error: {ex.Message}");
            }
            #endif

            // Popup showing all stored points with timestamps (debug-only)
            #if DEBUG
            if (ImGui.BeginPopupModal("giltracker_points_popup", ref _pointsPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
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
                        ImGui.TextUnformatted($"Gil data: " + (_helper.SelectedCharacterId == 0 ? "All Characters" : _helper.SelectedCharacterId.ToString()));
                        ImGui.Separator();
                        ImGui.BeginChild("giltracker_points_child", ConfigStatic.GilTrackerPointsPopupSize, true);
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

    private DateTime CalculateTimeCutoff()
    {
        var now = DateTime.UtcNow;
        var value = Config.GilTrackerTimeRangeValue;

        return Config.GilTrackerTimeRangeUnit switch
        {
            TimeRangeUnit.Minutes => now.AddMinutes(-value),
            TimeRangeUnit.Hours => now.AddHours(-value),
            TimeRangeUnit.Days => now.AddDays(-value),
            TimeRangeUnit.Weeks => now.AddDays(-value * 7),
            TimeRangeUnit.Months => now.AddMonths(-value),
            _ => DateTime.MinValue
        };
    }
}
