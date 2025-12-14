namespace Kaleidoscope.Config;

/// <summary>
/// Window state and position configuration.
/// </summary>
public class WindowConfig
{
    /// <summary>Whether the main window is pinned.</summary>
    public bool PinMainWindow { get; set; } = false;

    /// <summary>Whether the config window is pinned.</summary>
    public bool PinConfigWindow { get; set; } = false;

    /// <summary>Saved position for the main window.</summary>
    public Vector2 MainWindowPos { get; set; } = new Vector2(100, 100);

    /// <summary>Saved size for the main window.</summary>
    public Vector2 MainWindowSize { get; set; } = new Vector2(600, 400);

    /// <summary>Saved position for the config window.</summary>
    public Vector2 ConfigWindowPos { get; set; } = new Vector2(100, 100);

    /// <summary>Saved size for the config window.</summary>
    public Vector2 ConfigWindowSize { get; set; } = new Vector2(600, 400);
}
