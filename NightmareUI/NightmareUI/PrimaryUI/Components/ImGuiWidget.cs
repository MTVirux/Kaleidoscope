using System;

namespace NightmareUI.PrimaryUI.Components;
internal class ImGuiWidget : IWidget
{
    internal string Label;
    internal Action<string> DrawAction;
    internal string? Help;
    internal float? Width;
    internal NuiBuilder NuiBuilder;

    internal bool ShouldHighlight => NuiBuilder.Filter == "" || Label.Contains(NuiBuilder.Filter, StringComparison.OrdinalIgnoreCase);

    public ImGuiWidget(NuiBuilder builder, string label, Action<string> drawAction, string? help = null)
    {
        NuiBuilder = builder;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        DrawAction = drawAction ?? throw new ArgumentNullException(nameof(drawAction));
        Help = help;
    }

    public ImGuiWidget(NuiBuilder builder, float width, string label, Action<string> drawAction, string? help = null)
    {
        NuiBuilder = builder;
        Width = width;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        DrawAction = drawAction ?? throw new ArgumentNullException(nameof(drawAction));
        Help = help;
    }
}
