namespace Kaleidoscope.Gui.ConfigWindow;

/// <summary>
/// A selectable configuration category rendered in the config window sidebar.
/// </summary>
public interface IConfigCategory
{
    /// <summary>Sidebar label for this category.</summary>
    string Label { get; }

    /// <summary>Whether this category belongs to the developer-only section (CTRL+ALT / dev mode).</summary>
    bool IsDeveloper { get; }

    /// <summary>Draws the category content.</summary>
    void Draw();
}
