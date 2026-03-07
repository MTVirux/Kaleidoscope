namespace Kaleidoscope.Gui.Widgets.Common;

/// <summary>
/// Interface for settings classes that contain number formatting configuration.
/// Implement this interface to enable automatic number format settings binding with widgets.
/// </summary>
public interface INumberFormatSettings
{
    /// <summary>
    /// Number format configuration for the widget.
    /// </summary>
    NumberFormatConfig NumberFormat { get; set; }
}
