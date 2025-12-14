namespace Kaleidoscope.Config;

/// <summary>
/// General plugin settings.
/// </summary>
public class GeneralConfig
{
    public bool ShowOnStart { get; set; } = true;
    public bool ExclusiveFullscreen { get; set; } = false;
    public float ContentGridCellWidthPercent { get; set; } = 25f;
    public float ContentGridCellHeightPercent { get; set; } = 25f;
    public bool EditMode { get; set; } = false;
}
