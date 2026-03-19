namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Marks a <see cref="ToolComponent"/> subclass as a tool type that can be discovered
/// and instantiated by <see cref="ToolFactory"/>. Constructor dependencies are resolved
/// from the DI container via <c>ActivatorUtilities.CreateInstance</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ToolTypeAttribute : Attribute
{
    /// <summary>Unique tool identifier used for layout persistence and factory lookup.</summary>
    public string Id { get; }

    /// <summary>Display label shown in menus and tool headers.</summary>
    public string Label { get; }

    /// <summary>Category path for nested context menus (e.g. "Universalis", "AutoRetainer").</summary>
    public string Category { get; }

    /// <summary>Optional tooltip description for the tool.</summary>
    public string Description { get; }

    /// <summary>
    /// When set, specifies required service types that must be non-null for this tool
    /// to be available. Tools whose required services are missing will not appear in menus.
    /// </summary>
    public Type[] RequiredServices { get; set; } = [];

    /// <summary>
    /// Optional post-creation configuration key. When multiple tool definitions share the
    /// same class (e.g. DataTool with Graph vs Table view mode), this distinguishes them.
    /// The value is passed to <see cref="ToolFactory"/> which applies a registered post-create action.
    /// </summary>
    public string? Variant { get; set; }

    public ToolTypeAttribute(string id, string label, string category, string description = "")
    {
        Id = id;
        Label = label;
        Category = category;
        Description = description;
    }
}
