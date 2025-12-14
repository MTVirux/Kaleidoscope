using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.CharacterPicker
{
    using Kaleidoscope.Gui.MainWindow;

    /// <summary>
    /// A standalone character picker tool.
    /// For actual character selection functionality, use CharacterPickerWidget
    /// with an ICharacterDataSource implementation.
    /// </summary>
    /// <remarks>
    /// To add character selection to a tool:
    /// 1. Implement ICharacterDataSource or use an existing implementation like GilTrackerHelper
    /// 2. Create a CharacterPickerWidget with the data source
    /// 3. Call widget.Draw() in your DrawContent method
    /// </remarks>
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
