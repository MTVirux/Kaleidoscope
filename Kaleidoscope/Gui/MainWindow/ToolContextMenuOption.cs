namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Represents a custom option that can appear in a tool's right-click context menu.
/// </summary>
public class ToolContextMenuOption
{
    /// <summary>
    /// The text label displayed in the menu item.
    /// </summary>
    public required string Label { get; init; }
    
    /// <summary>
    /// The action to execute when the menu item is clicked.
    /// </summary>
    public required Action OnClick { get; init; }
    
    /// <summary>
    /// Optional tooltip shown when hovering over the menu item.
    /// </summary>
    public string? Tooltip { get; init; }
    
    /// <summary>
    /// Optional icon (emoji or text) displayed before the label.
    /// </summary>
    public string? Icon { get; init; }
    
    /// <summary>
    /// Whether the menu item is currently enabled.
    /// Disabled items appear grayed out and cannot be clicked.
    /// </summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>
    /// For toggle-style options, indicates the current state.
    /// When set, displays a checkmark next to the label if true.
    /// </summary>
    public bool? IsChecked { get; init; }
    
    /// <summary>
    /// Whether a separator should be drawn before this menu item.
    /// </summary>
    public bool SeparatorBefore { get; init; }
    
    /// <summary>
    /// Whether a separator should be drawn after this menu item.
    /// </summary>
    public bool SeparatorAfter { get; init; }
    
    /// <summary>
    /// Optional keyboard shortcut hint displayed on the right side of the menu item.
    /// </summary>
    public string? Shortcut { get; init; }
}
