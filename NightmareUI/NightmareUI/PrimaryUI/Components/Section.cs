using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using ECommons;
using ECommons.ImGuiMethods;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NightmareUI.PrimaryUI.Components;
internal unsafe class Section
{
    internal string Name = "";
    internal Vector4? Color;
    internal List<IWidget> Widgets = [];
    internal bool PrevSeparator = false;
    internal Func<bool>? Cond = null;
    internal bool CondComp;
    internal bool Collapsible;
    internal Action? RightFloat;

    public bool ShouldHighlight => Widgets.OfType<ImGuiWidget>().Any(z => z.ShouldHighlight);

    public static Vector2 CellPadding { get; } =  new(7f);


    internal void Draw(NuiBuilder builder, bool noCollapse)
    {
        var oldPadding = ImGui.GetStyle().CellPadding;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, CellPadding);
        if(ImGui.BeginTable(Name, 1, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn(Name, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var id = ImGui.GetID(Name + "NuiSection");
            Color ??= ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
            if(Collapsible && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(ImGui.GetCursorScreenPos() - ImGui.GetStyle().CellPadding,
                ImGui.GetCursorScreenPos() + ImGui.GetStyle().CellPadding + new Vector2(ImGui.GetContentRegionAvail().X, ImGui.CalcTextSize(Name).Y)
                ))
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                Color = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBgHovered];
                if(!noCollapse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    NuiTools.State.CollapsedHeaders.Toggle(id);
                }
            }
            if(Collapsible)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                ImGuiEx.Text((!NuiTools.State.CollapsedHeaders.Contains(id) ? FontAwesomeIcon.Minus : FontAwesomeIcon.Plus).ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();
            }

            var cur = ImGui.GetCursorPos();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(Color.Value));
            ImGuiEx.Text(Name);
            if(!Collapsible || !NuiTools.State.CollapsedHeaders.Contains(id))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                foreach(var x in Widgets)
                {
                    if(x is CondIf condIf)
                    {
                        CondComp = true;
                        Cond = condIf.Predicate;
                    }
                    else if(x is CondElse)
                    {
                        CondComp = false;
                    }
                    else if(x is CondEndIf)
                    {
                        Cond = null;
                    }
                    if(Cond != null && Cond.Invoke() != CondComp) continue;
                    if(x is ImGuiWidget imGuiWidget)
                    {
                        Vector4? col = (builder.Filter != "") ? (imGuiWidget.ShouldHighlight ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudGrey3) : null;
                        PrevSeparator = false;
                        if(col != null) ImGui.PushStyleColor(ImGuiCol.Text, col.Value);
                        try
                        {
                            if(imGuiWidget.Width != null)
                            {
                                ImGui.SetNextItemWidth(imGuiWidget.Width.Value);
                            }
                            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, oldPadding);
                            try
                            {
                                imGuiWidget.DrawAction(imGuiWidget.Label);
                            }
                            catch(Exception iex)
                            {
                                iex.Log();
                            }
                            ImGui.PopStyleVar();
                            if(imGuiWidget.Help != null)
                            {
                                ImGuiEx.HelpMarker(imGuiWidget.Help);
                            }
                        }
                        catch(Exception e)
                        {
                            e.Log();
                        }
                        if(col != null) ImGui.PopStyleColor();
                    }
                    else if(x is SeparatorWidget separatorWidget)
                    {
                        if(!PrevSeparator)
                        {
                            separatorWidget.DrawAction();
                            PrevSeparator = true;
                        }
                    }
                }
            }
            ImGui.EndTable();
            if(RightFloat != null)
            {
                var cur2 = ImGui.GetCursorPos();
                ImGui.SetCursorPos(cur);
                ImGuiEx.RightFloat(Name + "NuiSection", RightFloat);
                ImGui.SetCursorPos(cur2);
            }
        }
        ImGui.Dummy(new(5f));
        ImGui.PopStyleVar();
    }
}
