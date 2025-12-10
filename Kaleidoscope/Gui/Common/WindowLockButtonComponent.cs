namespace CrystalTerror.Gui.Common;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

/// <summary>
/// Component for managing the window lock button functionality.
/// Handles toggling between locked/unlocked state and saving position/size.
/// </summary>
public class WindowLockButtonComponent
{
    private readonly CrystalTerrorPlugin plugin;
    private readonly bool isConfigWindow;
    private FontAwesomeIcon currentIcon;

    public FontAwesomeIcon CurrentIcon => currentIcon;

    public WindowLockButtonComponent(CrystalTerrorPlugin plugin, bool isConfigWindow = false)
    {
        this.plugin = plugin;
        this.isConfigWindow = isConfigWindow;

        // Initialize icon based on current state
        var isPinned = isConfigWindow ? plugin.Config.PinConfigWindow : plugin.Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }

    public void OnLockButtonClick() { }

    /// <summary>
    /// Updates the lock button icon state based on current configuration.
    /// </summary>
    public void UpdateState()
    {
        var isPinned = isConfigWindow ? this.plugin.Config.PinConfigWindow : this.plugin.Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }
}
