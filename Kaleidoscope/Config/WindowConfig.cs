namespace Kaleidoscope.Config;

/// <summary>
/// Window state and position configuration.
/// </summary>
public class WindowConfig
{
    public bool PinMainWindow { get; set; } = false;
    public bool PinConfigWindow { get; set; } = false;
    public Vector2 MainWindowPos { get; set; } = new(100, 100);
    public Vector2 MainWindowSize { get; set; } = new(600, 400);
    public Vector2 ConfigWindowPos { get; set; } = new(100, 100);
    public Vector2 ConfigWindowSize { get; set; } = new(600, 400);
}
