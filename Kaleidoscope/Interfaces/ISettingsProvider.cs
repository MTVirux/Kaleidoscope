namespace Kaleidoscope.Interfaces;

/// <summary>
/// Interface for components/widgets that provide their own settings UI.
/// When a component implementing this interface is registered with a ToolComponent,
/// its settings will automatically be included in the tool's settings panel.
/// </summary>
public interface ISettingsProvider
{
    bool HasSettings { get; }
    
    /// <summary>
    /// Display name for this component's settings section header.
    /// </summary>
    string SettingsName { get; }
    
    /// <returns>True if any setting was changed (to trigger config save).</returns>
    bool DrawSettings();
}
