namespace Kaleidoscope.Gui.Common;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using OtterGui.Text;
using Kaleidoscope.Services;

public class WindowLockButton
{
    private readonly ConfigurationService _configService;
    private readonly bool isConfigWindow;
    private FontAwesomeIcon currentIcon;

    private Configuration Config => _configService.Config;
    public FontAwesomeIcon CurrentIcon => currentIcon;

    public WindowLockButton(ConfigurationService configService, bool isConfigWindow = false)
    {
        _configService = configService;
        this.isConfigWindow = isConfigWindow;
        var isPinned = isConfigWindow ? Config.PinConfigWindow : Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }

    public void OnLockButtonClick()
    {
        if (isConfigWindow)
        {
            Config.PinConfigWindow = !Config.PinConfigWindow;
        }
        else
        {
            Config.PinMainWindow = !Config.PinMainWindow;
        }

        UpdateState();

        _configService.Save();
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
        var isPinned = isConfigWindow ? Config.PinConfigWindow : Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }
}
