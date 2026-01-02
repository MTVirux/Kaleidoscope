using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Help;

/// <summary>
/// A tool that displays graph control instructions for users.
/// </summary>
public class ImPlotReferenceTool : ToolComponent
{
    public override string ToolName => "Graph Controls";
    
    public ImPlotReferenceTool()
    {
        Title = "Graph Controls";
        Size = new Vector2(340, 320);
    }

    public override void RenderToolContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "Graph Controls");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Navigation:");
            ImGui.Spacing();
            ImGui.BulletText("Scroll wheel: Zoom in/out");
            ImGui.BulletText("Click + drag: Pan the view");
            ImGui.BulletText("Double-click: Reset zoom to fit all data");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Axis Controls:");
            ImGui.Spacing();
            ImGui.BulletText("Scroll on X-axis: Zoom X only");
            ImGui.BulletText("Scroll on Y-axis: Zoom Y only");
            ImGui.BulletText("Drag X-axis: Pan horizontally");
            ImGui.BulletText("Drag Y-axis: Pan vertically");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Selection:");
            ImGui.Spacing();
            ImGui.BulletText("Hover: View values at cursor position");
            ImGui.BulletText("Right-click + drag: Box zoom selection");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Legend:");
            ImGui.Spacing();
            ImGui.BulletText("Click legend item: Toggle series visibility");
            ImGui.Spacing();

            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Tip: Use the graph settings to change chart type,");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "legend position, and time range.");

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    public override bool HasSettings => false;
}
