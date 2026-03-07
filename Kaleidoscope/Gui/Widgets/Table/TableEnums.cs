namespace Kaleidoscope.Gui.Widgets.Table;

/// <summary>
/// Horizontal alignment options for table cell content.
/// </summary>
public enum TableHorizontalAlignment
{
    /// <summary>Align content to the left of the cell.</summary>
    Left,
    /// <summary>Center content horizontally in the cell.</summary>
    Center,
    /// <summary>Align content to the right of the cell.</summary>
    Right
}

/// <summary>
/// Vertical alignment options for table cell content.
/// </summary>
public enum TableVerticalAlignment
{
    /// <summary>Align content to the top of the cell.</summary>
    Top,
    /// <summary>Center content vertically in the cell.</summary>
    Center,
    /// <summary>Align content to the bottom of the cell.</summary>
    Bottom
}

/// <summary>
/// Column resize actions available from the header right-click context menu.
/// </summary>
public enum MTColumnResizeAction
{
    /// <summary>No resize action pending.</summary>
    None,
    /// <summary>Resize column(s) to fit header text width.</summary>
    HeaderWidth,
    /// <summary>Resize column(s) to fit data content width.</summary>
    DataWidth,
    /// <summary>Resize column(s) to fill remaining table space equally.</summary>
    FillSpace
}
