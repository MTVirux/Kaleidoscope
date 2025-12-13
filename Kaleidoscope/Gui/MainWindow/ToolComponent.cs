using System.Numerics;
namespace Kaleidoscope.Gui.MainWindow
{
    public abstract class ToolComponent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "Tool";
        public Vector2 Position { get; set; } = new Vector2(50, 50);
        public Vector2 Size { get; set; } = new Vector2(300, 200);
        public bool Visible { get; set; } = true;

        // Called when the tool should render its UI into the provided ImGui child
        public abstract void DrawContent();
    }
}
