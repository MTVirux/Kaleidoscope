namespace Kaleidoscope;

/// <summary>
/// Compile-time constants for UI layout and configuration.
/// </summary>
public static class ConfigStatic
{
    // Window defaults
    public static readonly Vector2 DefaultWindowPosition = new(100, 100);
    public static readonly Vector2 DefaultWindowSize = new(800, 600);
    public static readonly Vector2 MinimumWindowSize = new(250, 180);

    // GilTracker defaults
    public const int GilTrackerMaxSamples = 200;
    public const float GilTrackerStartingValue = 100000f;
    public const float GilTrackerMaxGil = 999_999_999f;
    public static readonly Vector2 GilTrackerToolSize = new(360, 220);
    public static readonly Vector2 GilTrackerPointsPopupSize = new(700, 300);

    // Sampler defaults
    public const int DefaultSamplerIntervalMs = 1000;
    public const int MinSamplerIntervalMs = 1000;

    // UI constants
    public const float FloatEpsilon = 0.0001f;
    public const int TextInputBufferSize = 128;
    public const float MaxDragDelta = 2000f;
    public const int MaxGridLines = 1024;
    
    // Tool component size constraints
    public const float MinToolWidth = 50f;
    // MinToolHeight is calculated dynamically based on text line height
}
