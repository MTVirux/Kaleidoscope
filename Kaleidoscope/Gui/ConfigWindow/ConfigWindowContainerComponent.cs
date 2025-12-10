namespace CrystalTerror.Gui.ConfigWindow;

using CrystalTerror.Gui.Common;
using NightmareUI.OtterGuiWrapper.FileSystems.Configuration;

/// <summary>
/// Container component for the config window content.
/// Handles rendering of configuration tabs and entries via the file system.
/// </summary>
public class ConfigWindowContainerComponent : IUIComponent
{
    private readonly ConfigFileSystem fileSystem;

    public ConfigWindowContainerComponent(ConfigFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public void Render() { }
}
