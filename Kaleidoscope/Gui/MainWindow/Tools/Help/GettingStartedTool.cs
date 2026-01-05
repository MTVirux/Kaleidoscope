using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Help;

/// <summary>
/// A tool that displays getting started instructions for new users.
/// This is the default tool shown when creating a new layout from scratch.
/// </summary>
public class GettingStartedTool : ToolComponent
{
    public override string ToolName => "Getting Started";
    
    public GettingStartedTool()
    {
        Title = "Getting Started";
        Size = new Vector2(400, 300);
    }

    public override void RenderToolContent()
    {
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "Welcome to Kaleidoscope!");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted("Kaleidoscope is a customizable HUD overlay plugin for FFXIV.");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Quick Start:");
            ImGui.Spacing();

            ImGui.BulletText("Click the Edit button (pencil icon) in the title bar to enter edit mode");
            ImGui.BulletText("Right-click anywhere in the window to open the context menu");
            ImGui.BulletText("Use 'Add tool' to add new widgets to your layout");
            ImGui.BulletText("Drag tools by their title bar to reposition them");
            ImGui.BulletText("Drag the bottom-right corner of a tool to resize it");
            ImGui.BulletText("Right-click a tool for options (background, settings, remove)");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Title Bar Buttons:");
            ImGui.Spacing();

            ImGui.BulletText("Cog: Open settings window");
            ImGui.BulletText("Arrows: Toggle fullscreen mode");
            ImGui.BulletText("Lock: Lock window position and size");
            ImGui.BulletText("Pencil: Toggle edit mode");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Tips:");
            ImGui.Spacing();

            ImGui.BulletText("Tools snap to the grid when you release them");
            ImGui.BulletText("Toggle 'Show header' to hide a tool's title bar");
            ImGui.BulletText("Your layout is saved automatically");
            ImGui.BulletText("Use 'Manage Layouts...' to create multiple layouts");
            ImGui.Spacing();

            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(UiColors.Info, "You can remove this tool once you're comfortable.");

            ImGui.PopTextWrapPos();
        }
        catch (Exception ex)
        {
            LogDebug($"Draw error: {ex.Message}");
        }
    }

    public override bool HasSettings => false;
}
