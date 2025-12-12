namespace Kaleidoscope.Gui.Common;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using OtterGui.Text;
using System.Numerics;

public class WindowLockButton
{
    private readonly Kaleidoscope.KaleidoscopePlugin plugin;
    private readonly bool isConfigWindow;
    private FontAwesomeIcon currentIcon;

    public FontAwesomeIcon CurrentIcon => currentIcon;

    public WindowLockButton(Kaleidoscope.KaleidoscopePlugin plugin, bool isConfigWindow = false)
    {
        this.plugin = plugin;
        this.isConfigWindow = isConfigWindow;
        var isPinned = isConfigWindow ? plugin.Config.PinConfigWindow : plugin.Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }

    public void OnLockButtonClick()
    {
        if (isConfigWindow)
        {
            this.plugin.Config.PinConfigWindow = !this.plugin.Config.PinConfigWindow;
        }
        else
        {
            this.plugin.Config.PinMainWindow = !this.plugin.Config.PinMainWindow;
        }

        UpdateState();

        try { this.plugin.SaveConfig(); } catch { }
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
        var isPinned = isConfigWindow ? this.plugin.Config.PinConfigWindow : this.plugin.Config.PinMainWindow;
        this.currentIcon = isPinned ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen;
    }
}
