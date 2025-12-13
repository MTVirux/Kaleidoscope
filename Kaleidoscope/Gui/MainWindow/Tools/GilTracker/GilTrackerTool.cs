using System.Numerics;
namespace Kaleidoscope.Gui.MainWindow.Tools.GilTracker
{
    using Kaleidoscope.Gui.MainWindow;

    public class GilTrackerTool : ToolComponent
    {
        private readonly GilTrackerComponent _inner;

        public GilTrackerTool(GilTrackerComponent inner)
        {
            _inner = inner;
            Title = "Gil Tracker";
            // default size
            Size = new Vector2(360, 220);
        }

        public override void DrawContent()
        {
            _inner.Draw();
        }
    }
}
