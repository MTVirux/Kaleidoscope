using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.GilTicker;

/// <summary>
/// Component that displays a scrolling ticker of character gil values.
/// </summary>
public class GilTickerComponent
{
    private readonly GilTrackerHelper _helper;
    private readonly ConfigurationService _configService;

    // Ticker animation state
    private float _tickerOffset = 0f;
    private DateTime _lastTickerUpdate = DateTime.MinValue;

    private Configuration Config => _configService.Config;

    public GilTickerComponent(GilTrackerHelper helper, ConfigurationService configService)
    {
        _helper = helper;
        _configService = configService;
    }

    public void Draw()
    {
        var series = _helper.GetAllCharacterSeries(null);
        if (series == null || series.Count == 0)
        {
            ImGui.TextUnformatted("No character data available.");
            return;
        }

        // Filter out disabled characters
        var disabledChars = Config.GilTickerDisabledCharacters;
        var filteredSeries = series.Where((s, idx) =>
        {
            var charId = GetCharacterIdFromName(s.name);
            return !disabledChars.Contains(charId);
        }).ToList();

        if (filteredSeries.Count == 0)
        {
            ImGui.TextUnformatted("All characters disabled.");
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        var width = Math.Max(1f, avail.X);
        var height = Math.Max(20f, avail.Y);

        DrawTickerLegend(filteredSeries, width, height);
    }

    private ulong GetCharacterIdFromName(string name)
    {
        // Try to find character ID from available characters list
        foreach (var charId in _helper.AvailableCharacters)
        {
            var charName = Kaleidoscope.Libs.CharacterLib.GetCharacterName(charId);
            if (charName == name)
                return charId;
        }
        return 0;
    }

    /// <summary>
    /// Draws a scrolling ticker-style legend showing character names and values.
    /// </summary>
    private void DrawTickerLegend(IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> series, float width, float height)
    {
        var legendColors = GetSeriesColors(series.Count);

        // Build the full ticker text
        var tickerParts = new List<(string text, System.Numerics.Vector4 color)>();
        var totalTextWidth = 0f;
        var separator = "  •  ";
        var separatorWidth = ImGui.CalcTextSize(separator).X;

        for (var i = 0; i < series.Count; i++)
        {
            var (name, samples) = series[i];
            var color = legendColors[i];
            var latestVal = samples != null && samples.Count > 0 ? FormatValue(samples[^1].value) : "N/A";
            var text = $"{name}: {latestVal}";
            var textWidth = ImGui.CalcTextSize(text).X;

            tickerParts.Add((text, new System.Numerics.Vector4(color.X, color.Y, color.Z, 1f)));
            totalTextWidth += textWidth;

            if (i < series.Count - 1)
            {
                tickerParts.Add((separator, new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f)));
                totalTextWidth += separatorWidth;
            }
        }

        // If all content fits, just draw it centered (no scrolling needed)
        if (totalTextWidth <= width)
        {
            var startX = (width - totalTextWidth) / 2f;
            var cursorPos = ImGui.GetCursorPos();
            ImGui.SetCursorPosX(cursorPos.X + startX);

            for (var i = 0; i < tickerParts.Count; i++)
            {
                var (text, color) = tickerParts[i];
                ImGui.TextColored(color, text);
                if (i < tickerParts.Count - 1) ImGui.SameLine(0, 0);
            }
            //ImGui.NewLine();
            return;
        }

        // Update ticker animation
        var speed = Config.GilTickerScrollSpeed;
        var now = DateTime.Now;
        if (_lastTickerUpdate != DateTime.MinValue)
        {
            var delta = (float)(now - _lastTickerUpdate).TotalSeconds;
            _tickerOffset += delta * speed;

            // Add separator at the end for seamless loop
            var loopWidth = totalTextWidth + separatorWidth + ImGui.CalcTextSize(separator).X;
            if (_tickerOffset >= loopWidth)
            {
                _tickerOffset -= loopWidth;
            }
        }
        _lastTickerUpdate = now;

        // Draw clipped ticker using DrawList
        var drawList = ImGui.GetWindowDrawList();
        var clipPos = ImGui.GetCursorScreenPos();
        var clipMax = new System.Numerics.Vector2(clipPos.X + width, clipPos.Y + height);

        drawList.PushClipRect(clipPos, clipMax, true);

        // Draw text twice for seamless looping
        var currentX = clipPos.X - _tickerOffset;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            for (var i = 0; i < tickerParts.Count; i++)
            {
                var (text, color) = tickerParts[i];
                var textWidth = ImGui.CalcTextSize(text).X;

                // Only draw if visible
                if (currentX + textWidth >= clipPos.X && currentX <= clipMax.X)
                {
                    drawList.AddText(new System.Numerics.Vector2(currentX, clipPos.Y), ImGui.GetColorU32(color), text);
                }

                currentX += textWidth;
            }

            // Add separator between loops
            currentX += separatorWidth;
        }

        drawList.PopClipRect();

        // Advance cursor
        ImGui.Dummy(new System.Numerics.Vector2(width, height));
    }

    private static System.Numerics.Vector3[] GetSeriesColors(int count)
    {
        // Predefined distinct colors for up to 8 series
        var colors = new System.Numerics.Vector3[]
        {
            new(0.4f, 0.8f, 0.4f),  // Green
            new(0.4f, 0.6f, 1.0f),  // Blue
            new(1.0f, 0.6f, 0.4f),  // Orange
            new(0.9f, 0.4f, 0.9f),  // Magenta
            new(1.0f, 1.0f, 0.4f),  // Yellow
            new(0.4f, 1.0f, 1.0f),  // Cyan
            new(1.0f, 0.4f, 0.4f),  // Red
            new(0.8f, 0.8f, 0.8f),  // Gray
        };

        var result = new System.Numerics.Vector3[count];
        for (var i = 0; i < count; i++)
            result[i] = colors[i % colors.Length];
        return result;
    }

    private static string FormatValue(float v)
    {
        const float epsilon = 0.0001f;
        if (Math.Abs(v - Math.Truncate(v)) < epsilon)
            return ((long)v).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        return v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    }
}
