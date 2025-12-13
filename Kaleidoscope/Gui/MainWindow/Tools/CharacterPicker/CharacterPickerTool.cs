using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.CharacterPicker
{
    using Kaleidoscope.Gui.MainWindow;

    public class CharacterPickerTool : ToolComponent
    {
        public CharacterPickerTool()
        {
            Title = "Character Picker";
            Size = new Vector2(260, 80);
        }

        public override void DrawContent()
        {
            ImGui.TextUnformatted("Character Picker (placeholder)");
            ImGui.Separator();
            ImGui.TextUnformatted("Integrate with GilTrackerHelper or game actor list.");
        }
    }
}
