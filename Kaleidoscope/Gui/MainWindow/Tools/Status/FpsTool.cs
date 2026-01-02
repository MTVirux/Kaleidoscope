using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models.Settings;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// Settings class for FpsTool with all configurable properties.
/// </summary>
public class FpsToolSettings
{
    public bool ShowFrameTime { get; set; } = true;
    public float WarningThreshold { get; set; } = 30f;
    public float BadThreshold { get; set; } = 15f;
}

/// <summary>
/// A tool that displays the current frames per second.
/// </summary>
public class FpsTool : ToolComponent
{
    public override string ToolName => "FPS";

    // Smoothing to avoid jittery display
    private float _smoothedFps;
    private const float SmoothingFactor = 0.1f;
    
    // Settings instance and schema
    private readonly FpsToolSettings _settings = new();
    
    private static readonly SettingsSchema<FpsToolSettings> Schema = SettingsSchema.For<FpsToolSettings>()
        .Checkbox(s => s.ShowFrameTime, "Show Frame Time", "Display milliseconds per frame alongside FPS", defaultValue: true)
        .Spacing()
        .SliderFloat(s => s.WarningThreshold, "Warning FPS", 10f, 60f, "FPS below this value shows warning color", "%.0f", 30f)
        .SliderFloat(s => s.BadThreshold, "Critical FPS", 5f, 30f, "FPS below this value shows critical/red color", "%.0f", 15f);

    /// <summary>
    /// Whether to show the frame time in milliseconds.
    /// </summary>
    public bool ShowFrameTime
    {
        get => _settings.ShowFrameTime;
        set => _settings.ShowFrameTime = value;
    }

    /// <summary>
    /// FPS threshold below which the color turns to warning.
    /// </summary>
    public float WarningThreshold
    {
        get => _settings.WarningThreshold;
        set => _settings.WarningThreshold = value;
    }

    /// <summary>
    /// FPS threshold below which the color turns to bad/red.
    /// </summary>
    public float BadThreshold
    {
        get => _settings.BadThreshold;
        set => _settings.BadThreshold = value;
    }

    public FpsTool()
    {
        Title = "FPS";
        Size = new Vector2(120, 70);
    }

    public override void RenderToolContent()
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
            ImGui.TextColored(UiColors.Info, "FPS:");
            ImGui.SameLine();
            ImGui.TextColored(fpsColor, $"{_smoothedFps:F1}");

            if (ShowFrameTime)
            {
                var frameTimeMs = 1000f / _smoothedFps;
                ImGui.TextColored(UiColors.Info, "Frame:");
                ImGui.SameLine();
                ImGui.TextColored(fpsColor, $"{frameTimeMs:F2} ms");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    private Vector4 GetFpsColor(float fps)
    {
        if (fps < BadThreshold)
            return UiColors.Bad;
        if (fps < WarningThreshold)
            return UiColors.Warning;
        return UiColors.Good;
    }

    protected override bool HasToolSettings => true;
    
    protected override object? GetToolSettingsSchema() => Schema;
    
    protected override object? GetToolSettingsObject() => _settings;
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return Schema.ToDictionary(_settings)!;
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        Schema.FromDictionary(_settings, settings);
    }
}
