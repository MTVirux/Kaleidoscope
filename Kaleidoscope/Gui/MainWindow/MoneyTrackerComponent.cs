using ImGui = Dalamud.Bindings.ImGui.ImGui;
using System.Numerics;
using System.Collections.Generic;

namespace Kaleidoscope.Gui.MainWindow
{
    internal class MoneyTrackerComponent
    {
        private readonly List<float> _samples = new();
        private readonly int _maxSamples = 200;
        private static readonly System.Random _rnd = new();
        private float _lastValue = 100000f; // simulated starting gil

        public MoneyTrackerComponent()
        {
            // seed with a few values so the plot isn't empty
            for (var i = 0; i < 40; i++)
                PushSample(SimulateNext());
        }

        private void PushSample(float v)
        {
            _samples.Add(v);
            if (_samples.Count > _maxSamples)
                _samples.RemoveAt(0);
            _lastValue = v;
        }

        private float SimulateNext()
        {
            // small random walk to emulate gil changes
            var delta = (float)(_rnd.NextDouble() * 15000.0 - 5000.0);
            var next = _lastValue + delta;
            if (next < 0) next = 0;
            return next;
        }

        public void Draw()
        {
            ImGui.TextUnformatted("Gil (simulated)");

            if (ImGui.Button("Sample Now"))
            {
                PushSample(SimulateNext());
            }
            ImGui.SameLine();
            if (ImGui.Button("Add Random"))
            {
                PushSample((float)(_rnd.Next(0, 1000000)));
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                _samples.Clear();
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            for (var i = 0; i < _samples.Count; i++)
            {
                var v = _samples[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (min == float.MaxValue) min = 0;
            if (max == float.MinValue) max = 0;

            ImGui.TextUnformatted($"Current: {(long)_lastValue}  Min: {(long)min}  Max: {(long)max}");

            // Plot the samples
            if (_samples.Count > 0)
            {
                // ImGui.PlotLines expects a float array
                var arr = _samples.ToArray();
                // Use the simpler overload to avoid binding differences across ImGui builds
                ImGui.PlotLines("##gilplot", arr, arr.Length);
            }
            else
            {
                ImGui.TextUnformatted("No data yet. Click 'Sample Now' to add a point.");
            }
        }
    }
}
