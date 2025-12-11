namespace Kaleidoscope.Gui.Common
{
    /// <summary>
    /// Minimal UI component interface used by the plugin UI.
    /// Provided so components can implement a standard Render method.
    /// </summary>
    public interface IUIComponent
    {
        void Render();
    }
}
