namespace Kaleidoscope.Gui.Common;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

/// <summary>
/// Component for managing the window lock button functionality.
/// Handles toggling between locked/unlocked state and saving position/size.
/// </summary>
public class WindowLockButtonComponent
{
    private readonly ConfigurationService _configService;
    private readonly bool isConfigWindow;
    private FontAwesomeIcon currentIcon;

    private Configuration Config => _configService.Config;
    public FontAwesomeIcon CurrentIcon => currentIcon;

    public WindowLockButtonComponent(ConfigurationService configService, bool isConfigWindow = false)
    {
        _configService = configService;
        this.isConfigWindow = isConfigWindow;

        // Initialize icon based on current state
        var isPinned = isConfigWindow ? Config.PinConfigWindow : Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }

    public void OnLockButtonClick(ImGuiMouseButton button)
    {
        if (button == ImGuiMouseButton.Left)
        {
            if (isConfigWindow)
            {
                Config.PinConfigWindow = !Config.PinConfigWindow;
                this.currentIcon = Config.PinConfigWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;

                if (Config.PinConfigWindow)
                {
                    Config.ConfigWindowPos = ImGui.GetWindowPos();
                    Config.ConfigWindowSize = ImGui.GetWindowSize();
                }
            }
            else
            {
                Config.PinMainWindow = !Config.PinMainWindow;
                this.currentIcon = Config.PinMainWindow ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;

                if (Config.PinMainWindow)
                {
                    Config.MainWindowPos = ImGui.GetWindowPos();
                    Config.MainWindowSize = ImGui.GetWindowSize();
                }
            }

            _configService.Save();
        }
    }

    /// <summary>
    /// Updates the lock button icon state based on current configuration.
    /// </summary>
    public void UpdateState()
    {
        var isPinned = isConfigWindow ? Config.PinConfigWindow : Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }
}
