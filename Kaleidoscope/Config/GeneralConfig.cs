namespace Kaleidoscope.Config;

/// <summary>
/// General plugin settings.
/// </summary>
public class GeneralConfig
{
    /// <summary>Whether the UI opens on plugin start.</summary>
    public bool ShowOnStart { get; set; } = true;

    /// <summary>Whether exclusive fullscreen mode is enabled.</summary>
    public bool ExclusiveFullscreen { get; set; } = false;

    /// <summary>Grid cell width percentage for content container.</summary>
    public float ContentGridCellWidthPercent { get; set; } = 25f;

    /// <summary>Grid cell height percentage for content container.</summary>
    public float ContentGridCellHeightPercent { get; set; } = 25f;

    /// <summary>Whether edit mode is enabled by default.</summary>
    public bool EditMode { get; set; } = false;
}
