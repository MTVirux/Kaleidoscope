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

        // Optional background for the tool. When enabled the tool child background
        // will be set to this color. Color is RGBA with components in [0,1].
        // Enabled by default so new tools show the background immediately.
        public bool BackgroundEnabled { get; set; } = true;
        // Default to Dalamud red (approx. #D23A3A) with 50% alpha
        public Vector4 BackgroundColor { get; set; } = new Vector4(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);

        // Called when the tool should render its UI into the provided ImGui child
        public abstract void DrawContent();
    }
}
