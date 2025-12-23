namespace Kaleidoscope.Interfaces;

/// <summary>
/// Interface for components/widgets that provide their own settings UI.
/// When a component implementing this interface is registered with a ToolComponent,
/// its settings will automatically be included in the tool's settings panel.
/// </summary>
public interface ISettingsProvider
{
    /// <summary>
    /// Gets whether this component has settings to display.
    /// </summary>
    bool HasSettings { get; }
    
    /// <summary>
    /// Gets the display name for this component's settings section.
    /// Used as the header when rendering settings in the tool's settings panel.
    /// </summary>
    string SettingsName { get; }
    
    /// <summary>
    /// Draws the settings UI for this component.
    /// Called automatically by the parent ToolComponent when rendering settings.
    /// </summary>
    /// <returns>True if any setting was changed (to trigger config save).</returns>
    bool DrawSettings();
}
