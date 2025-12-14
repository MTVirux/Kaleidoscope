using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Libs;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow
{
    public class GilTrackerComponent
    {
        private readonly GilTrackerHelper _helper;
        private readonly CharacterPickerWidget _characterPicker;
        private SampleGraphWidget _graphWidget;
        private readonly SamplerService _samplerService;

        // Expose DB path so callers can reuse the same DB file when creating multiple UI instances.
        public string? DbPath => _dbPath;
        private bool _pointsPopupOpen = false;
        private DateTime _lastSampleTime = DateTime.MinValue;
        private int _sampleIntervalMs = ConfigStatic.DefaultSamplerIntervalMs;

        private readonly string? _dbPath;

        /// <summary>
        /// DI constructor. Shares the database service from SamplerService.
        /// </summary>
        // Graph bounds (editable via settings)
        private float _graphMinValue = 0f;
        private float _graphMaxValue = ConfigStatic.GilTrackerMaxGil;

        public float GraphMinValue
        {
            get => _graphMinValue;
            set
            {
                _graphMinValue = value;
                UpdateGraphWidget();
            }
        }

        public float GraphMaxValue
        {
            get => _graphMaxValue;
            set
            {
                _graphMaxValue = value;
                UpdateGraphWidget();
            }
        }

        public GilTrackerComponent(FilenameService filenameService, SamplerService samplerService)
        {
            _samplerService = samplerService;
            _dbPath = filenameService.DatabasePath;
            // Share the database service from SamplerService to avoid duplicate connections
            _helper = new GilTrackerHelper(samplerService.DbService, ConfigStatic.GilTrackerMaxSamples, ConfigStatic.GilTrackerStartingValue);
            _characterPicker = new CharacterPickerWidget(_helper);
            // initialize graph widget using current graph bounds
            _graphWidget = new SampleGraphWidget(new SampleGraphWidget.GraphConfig
            {
                MinValue = _graphMinValue,
                MaxValue = _graphMaxValue,
                PlotId = "gilplot",
                NoDataText = "No data yet.",
                FloatEpsilon = ConfigStatic.FloatEpsilon
            });
        }

        private void UpdateGraphWidget()
        {
            try
            {
                _graphWidget = new SampleGraphWidget(new SampleGraphWidget.GraphConfig
                {
                    MinValue = _graphMinValue,
                    MaxValue = _graphMaxValue,
                    PlotId = "gilplot",
                    NoDataText = "No data yet.",
                    FloatEpsilon = ConfigStatic.FloatEpsilon
                });
            }
            catch (Exception ex)
            {
                LogService.Debug($"Failed to update graph widget: {ex.Message}");
            }
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

            // Draw the character picker widget
            _characterPicker.Draw();

            // Draw the sample graph widget
            _graphWidget.Draw(_helper.Samples);

            // Debug: right-click the plot to open a popup listing all data points + timestamps
            #if DEBUG
            try
            {
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
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
                        ImGui.TextUnformatted($"Points for character: {_helper.SelectedCharacterId}");
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
    }
}
