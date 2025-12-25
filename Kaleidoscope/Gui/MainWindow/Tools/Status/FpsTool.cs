using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// A tool that displays the current frames per second.
/// </summary>
public class FpsTool : ToolComponent
{
    private static readonly Vector4 InfoColor = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 GoodColor = new(0.2f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 WarningColor = new(0.9f, 0.7f, 0.2f, 1f);
    private static readonly Vector4 BadColor = new(0.8f, 0.2f, 0.2f, 1f);

    // Smoothing to avoid jittery display
    private float _smoothedFps;
    private const float SmoothingFactor = 0.1f;

    /// <summary>
    /// Whether to show the frame time in milliseconds.
    /// </summary>
    public bool ShowFrameTime { get; set; } = true;

    /// <summary>
    /// FPS threshold below which the color turns to warning.
    /// </summary>
    public float WarningThreshold { get; set; } = 30f;

    /// <summary>
    /// FPS threshold below which the color turns to bad/red.
    /// </summary>
    public float BadThreshold { get; set; } = 15f;

    public FpsTool()
    {
        Title = "FPS";
        Size = new Vector2(120, 70);
    }

    public override void DrawContent()
    {
        try
        {
            var io = ImGui.GetIO();
            var currentFps = io.Framerate;

            // Apply smoothing
            if (_smoothedFps <= 0)
                _smoothedFps = currentFps;
            else
                _smoothedFps = _smoothedFps + SmoothingFactor * (currentFps - _smoothedFps);

            var fpsColor = GetFpsColor(_smoothedFps);

            // Display FPS
            ImGui.TextColored(InfoColor, "FPS:");
            ImGui.SameLine();
            ImGui.TextColored(fpsColor, $"{_smoothedFps:F1}");

            if (ShowFrameTime)
            {
                var frameTimeMs = 1000f / _smoothedFps;
                ImGui.TextColored(InfoColor, "Frame:");
                ImGui.SameLine();
                ImGui.TextColored(fpsColor, $"{frameTimeMs:F2} ms");
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[FpsTool] Draw error: {ex.Message}");
        }
    }

    private Vector4 GetFpsColor(float fps)
    {
        if (fps < BadThreshold)
            return BadColor;
        if (fps < WarningThreshold)
            return WarningColor;
        return GoodColor;
    }

    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        var showFrameTime = ShowFrameTime;
        if (ImGui.Checkbox("Show Frame Time", ref showFrameTime))
        {
            ShowFrameTime = showFrameTime;
            NotifyToolSettingsChanged();
        }

        ImGui.Spacing();

        var warningThreshold = WarningThreshold;
        if (ImGui.SliderFloat("Warning FPS", ref warningThreshold, 10f, 60f, "%.0f"))
        {
            WarningThreshold = warningThreshold;
            NotifyToolSettingsChanged();
        }

        var badThreshold = BadThreshold;
        if (ImGui.SliderFloat("Critical FPS", ref badThreshold, 5f, 30f, "%.0f"))
        {
            BadThreshold = badThreshold;
            NotifyToolSettingsChanged();
        }
    }
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["ShowFrameTime"] = ShowFrameTime,
            ["WarningThreshold"] = WarningThreshold,
            ["BadThreshold"] = BadThreshold
        };
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        ShowFrameTime = GetSetting(settings, "ShowFrameTime", ShowFrameTime);
        WarningThreshold = GetSetting(settings, "WarningThreshold", WarningThreshold);
        BadThreshold = GetSetting(settings, "BadThreshold", BadThreshold);
    }
}
