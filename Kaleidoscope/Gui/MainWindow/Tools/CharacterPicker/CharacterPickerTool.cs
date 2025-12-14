/*
 * CharacterPickerTool removed.
 * The standalone Character Picker tool has been removed from the UI.
 * The file is retained (wrapped out of compilation) for history; it is not compiled.
 */

#if false
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
            Size = new Vector2(300, 120);
        }

        public override void DrawContent()
        {
            ImGui.TextUnformatted("Character Selection Widget");
            ImGui.Separator();
            ImGui.TextWrapped("Use CharacterPickerWidget with an ICharacterDataSource for character selection in your tools.");
            ImGui.Spacing();
            ImGui.TextDisabled("See GilTrackerComponent for example usage.");
        }
    }
}
#endif
