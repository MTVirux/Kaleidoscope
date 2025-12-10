using Dalamud.Bindings.ImGui;
using ECommons;
using ECommons.ImGuiMethods;

using System;

namespace NightmareUI.PrimaryUI;
public static class Nui
{
    public static void Checkbox(string label, ref bool value, string? help = null, Action? callback = null)
    {
        var ret = ImGui.Checkbox(label, ref value);
        if(ret) callback?.Invoke();
        if(help != null) ImGuiEx.HelpMarker(help);
    }

    public static void Widget(string label, Action<string> drawAction, string? help = null)
    {
        try
        {
            drawAction(label);
            if(help != null) ImGuiEx.HelpMarker(help);
        }
        catch(Exception e)
        {
            e.Log();
        }
    }
}
