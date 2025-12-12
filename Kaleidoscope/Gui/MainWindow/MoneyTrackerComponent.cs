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
    internal class MoneyTrackerComponent
    {
        private readonly MoneyTrackerHelper _helper;
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
            _getSamplerEnabled = getSamplerEnabled;
            _setSamplerEnabled = setSamplerEnabled;
            _getSamplerInterval = getSamplerInterval;
            _setSamplerInterval = setSamplerInterval;
        }



        public void Draw()
        {
            if (_getSamplerEnabled != null && _setSamplerEnabled != null)
            {
                if (_getSamplerInterval != null && _setSamplerInterval != null)
                {
                    var interval = _getSamplerInterval();
                    if (ImGui.InputInt("Sample Interval (ms)", ref interval))
                    {
                        if (interval < 1) interval = 1;
                        _setSamplerInterval(interval);
                    }
                }
            }

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

            if (!string.IsNullOrEmpty(_dbPath))
            {
                if (ImGui.Button("Clear DB"))
            {
                ImGui.OpenPopup("moneytracker_clear_db_confirm");
                _clearDbOpen = true;
            }
            }
            if (ImGui.BeginPopupModal("moneytracker_clear_db_confirm", ref _clearDbOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("This will permanently delete all saved Money Tracker data from the DB for all characters. Proceed?");
                if (ImGui.Button("Yes"))
                {
                    _helper.ClearAllData();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("No"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            if (_helper.AvailableCharacters.Count > 0)
            {
                var idx = _helper.AvailableCharacters.IndexOf(_helper.SelectedCharacterId);
                if (idx < 0) idx = 0;
                var names = _helper.AvailableCharacters.Select(id => CharacterLib.GetCharacterName(id)).ToArray();
                if (ImGui.Combo("Character", ref idx, names, names.Length))
                {
                    var id = _helper.AvailableCharacters[idx];
                    _helper.LoadForCharacter(id);
                }
            }

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
            if (_helper.FirstSampleTime.HasValue && _helper.LastSampleTime.HasValue)
            {
                ImGui.TextUnformatted($"Range: {_helper.FirstSampleTime:O} -> {_helper.LastSampleTime:O}");
            }

            // Plot the samples
            if (_helper.Samples.Count > 0)
            {
                // ImGui.PlotLines expects a float array
                var arr = _helper.Samples.ToArray();
                // Use the simpler overload to avoid binding differences across ImGui builds
                ImGui.PlotLines("##gilplot", arr, arr.Length);
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
                }
            }
            else
            {
                ImGui.TextUnformatted("No data yet.");
            }

            ImGui.Separator();
            if (ImGui.Button("Export CSV") && !string.IsNullOrEmpty(_dbPath))
            {
                try
                {
                    var fileName = _helper.ExportCsv();
                    // last message is stored on helper
                }
                catch { }
            }
            // we don't show an else here — the previous block already shows a 'No data' message if graph empty
            if (!string.IsNullOrEmpty(_helper.LastStatusMessage))
            {
                ImGui.TextUnformatted(_helper.LastStatusMessage);
            }
        }
    }
}
