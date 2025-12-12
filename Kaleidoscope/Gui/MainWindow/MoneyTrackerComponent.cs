using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using System.Globalization;
using ECommons.DalamudServices;
using System.Collections.Generic;
using System.Linq;
using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using Kaleidoscope.Libs;

namespace Kaleidoscope.Gui.MainWindow
{
    public class MoneyTrackerComponent
    {
        private readonly MoneyTrackerHelper _helper;
        private readonly CharacterPicker _characterPicker;
        private bool _pointsPopupOpen = false;
        private bool _namesPopupOpen = false;
        private DateTime _lastSampleTime = DateTime.MinValue;
        private int _sampleIntervalMs = 1000; // sample every 1000 milliseconds (default 1s)

        private readonly string? _dbPath;
        private Func<bool>? _getSamplerEnabled;
        private Action<bool>? _setSamplerEnabled;
        private Func<int>? _getSamplerInterval;
        private Action<int>? _setSamplerInterval;
        private bool _clearDbOpen = false;

        public MoneyTrackerComponent(string? dbPath = null, Func<bool>? getSamplerEnabled = null, Action<bool>? setSamplerEnabled = null, Func<int>? getSamplerInterval = null, Action<int>? setSamplerInterval = null)
        {
            _dbPath = dbPath;
            _helper = new MoneyTrackerHelper(_dbPath, 200, 100000f);
            _characterPicker = new CharacterPicker(_helper);
            _getSamplerEnabled = getSamplerEnabled;
            _setSamplerEnabled = setSamplerEnabled;
            _getSamplerInterval = getSamplerInterval;
            _setSamplerInterval = setSamplerInterval;
        }

        public bool HasDb => !string.IsNullOrEmpty(_dbPath);

        public void ClearAllData()
        {
            try { _helper.ClearAllData(); } catch { }
        }

        public string? ExportCsv()
        {
            try { return _helper.ExportCsv(); } catch { return null; }
        }

        public int CleanUnassociatedCharacters()
        {
            try
            {
                return _helper.CleanUnassociatedCharacterData();
            }
            catch { return 0; }
        }



        public void Draw()
        {
            // Try to sample from the game's currency manager at most once per _sampleIntervalMs.
            try
            {
                // _getSamplerInterval() now returns milliseconds from the UI/plugin wrapper
                if (_getSamplerInterval != null) _sampleIntervalMs = Math.Max(1, _getSamplerInterval());
                var now = DateTime.UtcNow;
                if ((now - _lastSampleTime).TotalMilliseconds >= _sampleIntervalMs)
                {
                    // Delegate to helper for sampling and persistence, maintain lastSampleTime in UI
                    _helper.SampleFromGame();
                    _lastSampleTime = now;
                }
            }
            catch 
            {
                // ignore sampling errors
            }

            // DB buttons moved to Config Window (Data Management)

            _characterPicker.Draw();

            float min = float.MaxValue;
            float max = float.MinValue;
            for (var i = 0; i < _helper.Samples.Count; i++)
            {
                var v = _helper.Samples[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (min == float.MaxValue) min = 0;
            if (max == float.MinValue) max = 0;

            ImGui.TextUnformatted($"Current: {((long)_helper.LastValue).ToString("N0", CultureInfo.InvariantCulture)}  Min: {((long)min).ToString("N0", CultureInfo.InvariantCulture)}  Max: {((long)max).ToString("N0", CultureInfo.InvariantCulture)}");

            // Plot the samples
            if (_helper.Samples.Count > 0)
            {
                // ImGui.PlotLines expects a float array
                var arr = _helper.Samples.ToArray();
                // Expand the plot to fill the available content region by hosting it in a sized child
                try
                {
                    var avail = ImGui.GetContentRegionAvail();
                    var graphWidth = Math.Max(100f, avail.X);
                    var graphHeight = Math.Max(100f, avail.Y);

                    // Ensure we have a non-zero vertical range for plotting
                    if (Math.Abs(max - min) < 0.0001f)
                    {
                        max = min + 1f;
                    }

                    ImGui.BeginChild("moneytracker_plot_child", new System.Numerics.Vector2(graphWidth, graphHeight), false);
                    ImGui.PlotLines("##gilplot", arr, arr.Length);
                    ImGui.EndChild();
                }
                catch
                {
                    // Fall back to default small plot if anything goes wrong
                    ImGui.PlotLines("##gilplot", arr, arr.Length);
                }
                // Show a tooltip with a nicely formatted value when hovering the plot
                if (ImGui.IsItemHovered())
                {
                    try
                    {
                        var minRect = ImGui.GetItemRectMin();
                        var maxRect = ImGui.GetItemRectMax();
                        var mouse = ImGui.GetMousePos();
                        var width = maxRect.X - minRect.X;
                        if (width > 0 && arr.Length > 0)
                        {
                            var rel = (mouse.X - minRect.X) / width;
                            var idx = (int)Math.Floor(rel * arr.Length);
                            if (idx < 0) idx = 0;
                            if (idx >= arr.Length) idx = arr.Length - 1;
                            var val = arr[idx];
                            string FormatValue(float v)
                            {
                                if (Math.Abs(v - Math.Truncate(v)) < 0.0001f)
                                    return ((long)v).ToString("N0", CultureInfo.InvariantCulture);
                                return v.ToString("N2", CultureInfo.InvariantCulture);
                            }

                            var currentStr = $"{idx}:{FormatValue(val)}";
                            // If we have a previous value, show the percent change in parentheses (no plus sign for positive)
                            if (idx > 0)
                            {
                                var prev = arr[idx - 1];
                                if (Math.Abs(prev) < 0.00001f)
                                {
                                    ImGui.SetTooltip($"{currentStr} (N/A)");
                                }
                                else
                                {
                                    var percent = (((double)val - (double)prev) / Math.Abs((double)prev)) * 100.0;
                                    var sign = percent < 0 ? "-" : "";
                                    var percentAbs = Math.Abs(percent);
                                    var percentStr = percentAbs.ToString("0.##", CultureInfo.InvariantCulture);
                                    ImGui.SetTooltip($"{currentStr} ({sign}{percentStr}%)");
                                }
                            }
                            else
                            {
                                ImGui.SetTooltip(currentStr);
                            }
                        }
                    }
                    catch { }
                    // Debug: right-click the plot to open a popup listing all data points + timestamps
                    #if DEBUG
                    try
                    {
                        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                        {
                            ImGui.OpenPopup("moneytracker_points_popup");
                            _pointsPopupOpen = true;
                        }
                    }
                    catch { }
                    #endif
                }
            }
            else
            {
                ImGui.TextUnformatted("No data yet.");
            }

            // Popup showing all stored points with timestamps (debug-only)
            #if DEBUG
            if (ImGui.BeginPopupModal("moneytracker_points_popup", ref _pointsPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
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
                        ImGui.BeginChild("moneytracker_points_child", new Vector2(700, 300), true);
                        for (var i = 0; i < pts.Count; i++)
                        {
                            var p = pts[i];
                            ImGui.TextUnformatted($"{i}: {p.ts:O}  {p.value:N0}");
                        }
                        ImGui.EndChild();
                    }
                }
                catch { }

                if (ImGui.Button("Close"))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
            #endif

            

            ImGui.Separator();
            // we don't show an else here — the previous block already shows a 'No data' message if graph empty
            if (!string.IsNullOrEmpty(_helper.LastStatusMessage))
            {
                ImGui.TextUnformatted(_helper.LastStatusMessage);
            }
        }
    }
}
