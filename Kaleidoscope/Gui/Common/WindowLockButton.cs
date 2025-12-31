using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using OtterGui.Text;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.Common;

/// <summary>
/// Manages the window lock/pin button functionality for windows.
/// </summary>
public class WindowLockButton
{
    private readonly ConfigurationService _configService;
    private readonly StateService? _stateService;
    private readonly bool isConfigWindow;
    private FontAwesomeIcon currentIcon;

    private Configuration Config => _configService.Config;
    public FontAwesomeIcon CurrentIcon => currentIcon;

    public WindowLockButton(ConfigurationService configService, StateService? stateService = null, bool isConfigWindow = false)
    {
        _configService = configService;
        _stateService = stateService;
        this.isConfigWindow = isConfigWindow;
        var isPinned = isConfigWindow ? Config.PinConfigWindow : (_stateService?.IsLocked ?? Config.PinMainWindow);
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }

    public void OnLockButtonClick()
    {
        if (isConfigWindow)
        {
            Config.PinConfigWindow = !Config.PinConfigWindow;
            _configService.MarkDirty();
        }
        else
        {
            // Use StateService if available, otherwise fall back to Config
            if (_stateService != null)
            {
                if (!_stateService.IsLocked)
                {
                    Config.MainWindowPos = ImGui.GetWindowPos();
                    Config.MainWindowSize = ImGui.GetWindowSize();
                }
                _stateService.ToggleLocked();
            }
            else
            {
                Config.PinMainWindow = !Config.PinMainWindow;
                _configService.MarkDirty();
            }
        }

        UpdateState();
    }

    public void RenderInTitleBar()
    {
        var wndPos = ImGui.GetWindowPos();
        var wndSize = ImGui.GetWindowSize();
        var style = ImGui.GetStyle();
        var btnSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
        var x = wndPos.X + wndSize.X - style.WindowPadding.X - btnSize.X - 4.0f;
        var y = wndPos.Y + style.WindowPadding.Y;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var tooltip = isConfigWindow ? "Pin config window" : "Pin main window";
        if (ImUtf8.IconButton(this.currentIcon, tooltip, btnSize))
        {
            OnLockButtonClick();
        }
        ImGui.SetCursorPosY(ImGui.GetCursorPosY());
    }

    public void UpdateState()
    {
        var isPinned = isConfigWindow ? Config.PinConfigWindow : (_stateService?.IsLocked ?? Config.PinMainWindow);
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }
}
