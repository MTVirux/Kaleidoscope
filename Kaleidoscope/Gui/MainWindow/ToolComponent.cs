using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
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

        // Optional: override to expose a settings UI for the tool. When
        // `HasSettings` is true the container will show a "Settings..."
        // context menu item which opens a modal that calls `DrawSettings`.
        public virtual bool HasSettings => false;

        // Draw the tool-specific settings UI. Override when `HasSettings`
        // returns true. The default implementation does nothing.
        public virtual void DrawSettings() { }

        // Helper for tools to show a tooltip for the last item if hovered.
        // `description` should explain the setting; `defaultText` shows the default value.
        protected void ShowSettingTooltip(string description, string defaultText)
        {
            try
            {
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    if (!string.IsNullOrEmpty(description)) ImGui.TextUnformatted(description);
                    if (!string.IsNullOrEmpty(defaultText))
                    {
                        ImGui.Separator();
                        ImGui.TextUnformatted($"Default: {defaultText}");
                    }
                    ImGui.EndTooltip();
                }
            }
            catch { }
        }
    }
}
