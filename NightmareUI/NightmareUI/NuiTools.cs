using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using ECommons;
using ECommons.ExcelServices.TerritoryEnumeration;
using ECommons.ImGuiMethods;
using ECommons.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NightmareUI;
public static class NuiTools
{
    public static NightmareUIState State { get; private set; } = new();

    public static void SetState(NightmareUIState state)
    {
        State = state;
    }

    public static void ButtonTabs(ButtonInfo[][] buttons2d, int maxButtons = int.MaxValue) => ButtonTabs(GenericHelpers.GetCallStackID(), buttons2d, maxButtons);
    public static void ButtonTabs(string id, ButtonInfo[][] buttons2d, int maxButtons = int.MaxValue, bool child = true)
    {
        ImGui.PushID(id);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 1));
        for(var q = 0; q < buttons2d.Length; q++)
        {
            var buttons = buttons2d[q];
            buttons = buttons.Where(x => x != null).ToArray();
            var curMaxButtons = Math.Clamp(maxButtons, 1, buttons.Length);
            if(!State.ActiveTab.ContainsKey(id)) State.ActiveTab[id] = buttons[0].InternalName;
            var width = ImGui.GetContentRegionAvail().X / curMaxButtons;
            for(var i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                var act = State.ActiveTab[id] == b.InternalName;
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
                if(act)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
                }
                var w = width;
                if((i + 1) % curMaxButtons == 0)
                {
                    w = ImGui.GetContentRegionAvail().X;
                }
                if(ImGui.Button(b.Name, new(w, ImGui.GetFrameHeight())))
                {
                    State.ActiveTab[id] = buttons[i].InternalName;
                }
                if((i + 1) % curMaxButtons != 0 && i + 1 != buttons.Length)
                {
                    ImGui.SameLine(0, 0);
                }
                if(act)
                {
                    ImGui.PopStyleColor(2);
                }
                ImGui.PopStyleVar();
            }
        }
        ImGui.PopStyleVar();
        if(!child || ImGui.BeginChild($"NuiTabs{id}"))
        {
            if(State.ActiveTab.TryGetValue(id, out var value))
            {
                try
                {
                    foreach(var a in buttons2d)
                    {
                        foreach(var b in a)
                        {
                            if(b != null && b.InternalName == value)
                            {
                                b.Action();
                            }
                        }
                    }
                }
                catch(Exception e)
                {
                    PluginLog.Error("Error!");
                    e.Log();
                }
            }
        }
        if(child) ImGui.EndChild();
        ImGui.PopID();
    }

    public record class ButtonInfo
    {
        public readonly string Name;
        public readonly string InternalName;
        public readonly Action Action;

        public ButtonInfo(string name, Action action)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InternalName = Name;
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public ButtonInfo(string name, string internalName, Action action)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InternalName = internalName;
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }
    }


    public static bool RenderResidentialIcon(this uint residentialAetheryte, float? size = null)
    {
        var id = residentialAetheryte switch
        {
            ResidentalAreas.Mist => (new Vector2(0.3382f, 0.0000f), new Vector2(0.4118f, 0.1408f)),
            ResidentalAreas.The_Lavender_Beds => (new Vector2(0.4118f, 0.0000f), new Vector2(0.4853f, 0.1408f)),
            ResidentalAreas.The_Goblet => (new Vector2(0.3382f, 0.1408f), new Vector2(0.4118f, 0.2817f)),
            ResidentalAreas.Empyreum => (new Vector2(0.4853f, 0.0000f), new Vector2(0.5588f, 0.1408f)),
            ResidentalAreas.Shirogane => (new Vector2(0.7059f, 0.0000f), new Vector2(0.7794f, 0.1408f)),
            _ => (new Vector2(0.4853f, 0.1408f), new Vector2(0.5588f, 0.2817f))
        };
        if(ThreadLoadImageHandler.TryGetTextureWrap("ui/uld/Teleport_hr1.tex", out var tex))
        {
            size ??= ImGuiHelpers.GetButtonSize("A").Y;
            ImGui.Image(tex.Handle, new(size.Value), id.Item1, id.Item2);
            return true;
        }
        return false;
    }
}
