using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;

using NightmareUI.PrimaryUI.Components;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

namespace NightmareUI.PrimaryUI;
public class NuiBuilder
{
    private Section? CurrentSection;
    private List<Section> Sections = [];
    public string Filter = "";

    public bool ShouldHighlight => Sections.Any(z => z.ShouldHighlight);

    public NuiBuilder() { }

    public NuiBuilder Section(string name, Vector4? color = null, bool collapsible = false, Action? rightFloat = null)
    {
        CurrentSection = new() { Name = name, Collapsible = collapsible, Color = color, RightFloat = rightFloat };
        Sections.Add(CurrentSection);
        return this;
    }

    [MemberNotNull(nameof(CurrentSection))]
    private void EnsureSectionNotNull()
    {
        if(CurrentSection == null) throw new NullReferenceException("CurrentSection is null");
    }

    public NuiBuilder Widget(string name, Action<string> drawAction, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, drawAction, help));
        return this;
    }

    public NuiBuilder Widget(Action drawAction)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, "", (x) => drawAction(), null));
        return this;
    }

    public NuiBuilder If(Func<bool> cond)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new CondIf(cond));
        return this;
    }

    public NuiBuilder Else()
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new CondElse());
        return this;
    }

    public NuiBuilder EndIf()
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new CondEndIf());
        return this;
    }

    public NuiBuilder Indent()
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, "", (x) => ImGui.Indent()));
        return this;
    }

    public NuiBuilder Unindent()
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, "", (x) => ImGui.Unindent()));
        return this;
    }

    public delegate ref bool RefBoolDelegate();
    public delegate ref int RefIntDelegate();
    public delegate ref float RefFloatDelegate();
    public delegate ref string RefStringDelegate();
    public delegate ref T RefEnumDelegate<T>() where T : Enum, IConvertible;

    public NuiBuilder TextWrapped(string text)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, "", (x) => ImGuiEx.TextWrapped(text), null));
        return this;
    }

    public NuiBuilder TextWrapped(Vector4? col, string text)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, "", (x) => ImGuiEx.TextWrapped(col, text), null));
        return this;
    }

    public NuiBuilder EnumCombo<T>(float width, string name, RefEnumDelegate<T> value, IDictionary<T, string>? names = null, string? help = null) where T : Enum, IConvertible
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) => ImGuiEx.EnumCombo<T>(name, ref value(), names), help));
        return this;
    }

    public NuiBuilder EnumComboFullWidth<T>(float? width, string name, RefEnumDelegate<T> value, Func<T, bool> filter = null, IDictionary<T, string>? names = null, string? help = null) where T : Enum, IConvertible
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGuiEx.TextWrapped(name);
            if(help != null)
            {
                ImGui.SameLine();
                ImGuiEx.HelpMarker(help);
            }
            ImGui.Indent();
            if(width == null)
            {
                ImGuiEx.SetNextItemFullWidth();
            }
            else
            {
                ImGui.SetNextItemWidth(width.Value);
            }
            ImGuiEx.EnumCombo<T>($"##{name}", ref value(), filter, names);
            ImGui.Unindent();
        }, null));
        return this;
    }

    public NuiBuilder Checkbox(string name, RefBoolDelegate value, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) => ImGui.Checkbox(x, ref value()), help));
        return this;
    }

    public NuiBuilder CheckboxInverted(string name, RefBoolDelegate value, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) => ImGuiEx.CheckboxInverted(x, ref value()), help));
        return this;
    }

    public NuiBuilder SliderIntAsFloat(float width, string name, RefIntDelegate value, int min, int max, float divider = 1000, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGuiEx.SliderIntAsFloat(name, ref value(), min, max, divider);
        }, help));
        return this;
    }

    public NuiBuilder InputInt(float width, string name, RefIntDelegate value, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGui.InputInt(name, ref value());
        }, help));
        return this;
    }

    public NuiBuilder InputInt(float width, string name, RefIntDelegate value, int step, int step_fast, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGui.InputInt(name, ref value(), step, step_fast);
        }, help));
        return this;
    }

    public NuiBuilder DragInt(float width, string name, RefIntDelegate value, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGui.DragInt(name, ref value());
        }, help));
        return this;
    }

    public NuiBuilder DragInt(float width, string name, RefIntDelegate value, float v_speed, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGui.DragInt(name, ref value(), v_speed);
        }, help));
        return this;
    }

    public NuiBuilder DragInt(float width, string name, RefIntDelegate value, float v_speed, int v_min, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGui.DragInt(name, ref value(), v_speed, v_min);
        }, help));
        return this;
    }

    public NuiBuilder DragInt(float width, string name, RefIntDelegate value, float v_speed, int v_min, int v_max, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGui.DragInt(name, ref value(), v_speed, v_min, v_max);
        }, help));
        return this;
    }

    public NuiBuilder SliderInt(float width, string name, RefIntDelegate value, int min, int max, string? help = null, ImGuiSliderFlags flags = ImGuiSliderFlags.None)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGuiEx.SliderInt(name, ref value(), min, max, "%d", flags);
        }, help));
        return this;
    }

    public NuiBuilder SliderFloat(float width, string name, RefFloatDelegate value, float min, float max, string? help = null, ImGuiSliderFlags flags = ImGuiSliderFlags.None)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, name, (x) =>
        {
            ImGui.SetNextItemWidth(width);
            ImGuiEx.SliderFloat(name, ref value(), min, max, "%.3f", flags);
        }, help));
        return this;
    }

    public NuiBuilder Widget(float width, string name, Action<string> drawAction, string? help = null)
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new ImGuiWidget(this, width, name, drawAction, help));
        return this;
    }

    public NuiBuilder Separator()
    {
        EnsureSectionNotNull();
        CurrentSection.Widgets.Add(new SeparatorWidget(() =>
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
        }));
        return this;
    }

    public NuiBuilder Draw(bool noCollapse = false)
    {
        foreach(var x in Sections)
        {
            x.Draw(this, noCollapse);
        }
        return this;
    }
}
