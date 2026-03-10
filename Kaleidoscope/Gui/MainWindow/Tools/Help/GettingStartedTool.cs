using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Help;

/// <summary>
/// A tool that displays getting started instructions for new users.
/// This is the default tool shown when creating a new layout from scratch.
/// </summary>
public sealed class GettingStartedTool : ToolComponent
{
    public override string ToolName => "Getting Started";
    
    public GettingStartedTool()
    {
        Title = "Getting Started";
        Size = new Vector2(420, 520);
    }

    public override void RenderToolContent()
    {
        try
        {
            var avail = ImGui.GetContentRegionAvail();
            ImGui.BeginChild("##GettingStartedScroll", avail, false, ImGuiWindowFlags.None);
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);

            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), "Welcome to Kaleidoscope!");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextUnformatted("Kaleidoscope is a customizable HUD overlay plugin for FFXIV that lets you track game data across multiple characters.");
            ImGui.Spacing();

            // --- Quick Start ---
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Quick Start:");
            ImGui.Spacing();

            ImGui.BulletText("Click the Edit button (pencil icon) in the title bar to enter edit mode");
            ImGui.BulletText("Right-click the background in edit mode to open the context menu");
            ImGui.BulletText("Use 'Add tool' to add widgets to your layout");
            ImGui.BulletText("Drag tools by their title bar to reposition them");
            ImGui.BulletText("Drag the bottom-right corner of a tool to resize it");
            ImGui.BulletText("Hold CTRL+SHIFT for temporary edit mode without toggling");
            ImGui.Spacing();

            // --- Title Bar Buttons ---
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Title Bar Buttons:");
            ImGui.Spacing();

            ImGui.BulletText("Save: Save layout changes (shown when layout is modified)");
            ImGui.BulletText("Cog: Open settings window");
            ImGui.BulletText("Arrows: Toggle fullscreen mode");
            ImGui.BulletText("Lock: Lock window position and size");
            ImGui.BulletText("Pencil: Toggle edit mode");
            ImGui.Spacing();

            // --- Tool Context Menu ---
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Tool Options (right-click a tool header):");
            ImGui.Spacing();

            ImGui.BulletText("Rename: Change the tool's display name");
            ImGui.BulletText("Duplicate: Create a copy of the tool");
            ImGui.BulletText("Appearance: Toggle background, header, outline; pick colors");
            ImGui.BulletText("Settings: Open the tool's settings panel");
            ImGui.BulletText("Remove: Delete the tool from the layout");
            ImGui.Spacing();

            // --- Layout Management ---
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Layout Management:");
            ImGui.Spacing();

            ImGui.BulletText("Create, save, and load multiple layouts");
            ImGui.BulletText("Use 'Save Layout' or the save button to persist changes");
            ImGui.BulletText("'Discard Changes' reverts to the last saved state");
            ImGui.BulletText("'Edit grid resolution' adjusts the snap grid");
            ImGui.Spacing();

            // --- Tips ---
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "Tips:");
            ImGui.Spacing();

            ImGui.BulletText("Tools snap to the grid when you release them");
            ImGui.BulletText("Toggle 'Show header' in Appearance to hide a tool's title bar");
            ImGui.BulletText("Use 'Manage Layouts...' to organize your layouts");
            ImGui.Spacing();

            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(UiColors.Info, "You can remove this tool once you're comfortable.");

            ImGui.PopTextWrapPos();
            ImGui.EndChild();
        }
        catch (Exception ex)
        {
            LogDebug($"Draw error: {ex.Message}");
        }
    }
}
