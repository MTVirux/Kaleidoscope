namespace Kaleidoscope.Gui.Common;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

/// <summary>
/// Component for managing the window lock button functionality.
/// Handles toggling between locked/unlocked state and saving position/size.
/// </summary>
public class WindowLockButtonComponent
{
    private readonly Kaleidoscope.KaleidoscopePlugin plugin;
    private readonly bool isConfigWindow;
    private FontAwesomeIcon currentIcon;

    public FontAwesomeIcon CurrentIcon => currentIcon;

    public WindowLockButtonComponent(Kaleidoscope.KaleidoscopePlugin plugin, bool isConfigWindow = false)
    {
        this.plugin = plugin;
        this.isConfigWindow = isConfigWindow;

        // Initialize icon based on current state
        var isPinned = isConfigWindow ? plugin.Config.PinConfigWindow : plugin.Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }

    public void OnLockButtonClick(ImGuiMouseButton button)
    {
        if (button == ImGuiMouseButton.Left)
        {
            if (isConfigWindow)
            {
                this.plugin.Config.PinConfigWindow = !this.plugin.Config.PinConfigWindow;
                this.currentIcon = this.plugin.Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;

                if (this.plugin.Config.PinConfigWindow)
                {
                    this.plugin.Config.ConfigWindowPos = ImGui.GetWindowPos();
                    this.plugin.Config.ConfigWindowSize = ImGui.GetWindowSize();
                }
            }
            else
            {
                this.plugin.Config.PinMainWindow = !this.plugin.Config.PinMainWindow;
                this.currentIcon = this.plugin.Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;

                if (this.plugin.Config.PinMainWindow)
                {
                    this.plugin.Config.MainWindowPos = ImGui.GetWindowPos();
                    this.plugin.Config.MainWindowSize = ImGui.GetWindowSize();
                }
            }

            try { ECommons.DalamudServices.Svc.PluginInterface.SavePluginConfig(this.plugin.Config); } catch { }
        }
    }

    /// <summary>
    /// Updates the lock button icon state based on current configuration.
    /// </summary>
    public void UpdateState()
    {
        var isPinned = isConfigWindow ? this.plugin.Config.PinConfigWindow : this.plugin.Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }
}
