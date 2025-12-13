namespace Kaleidoscope.Gui.ConfigWindow;

/// <summary>
/// Container component for the config window content.
/// Minimal stub to avoid external dependency on NightmareUI's ConfigFileSystem.
/// </summary>
public class ConfigWindowContainerComponent
{
    private readonly object fileSystem;

    public ConfigWindowContainerComponent(object fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public void Render() { }
}
