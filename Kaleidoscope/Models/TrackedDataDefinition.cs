namespace Kaleidoscope.Models;

/// <summary>
/// Defines metadata for a trackable data type.
/// </summary>
public sealed class TrackedDataDefinition
{
    /// <summary>
    /// The data type this definition describes.
    /// </summary>
    public TrackedDataType Type { get; init; }

    /// <summary>
    /// Display name shown in UI.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Short name for compact displays.
    /// </summary>
    public string ShortName { get; init; } = string.Empty;

    /// <summary>
    /// Category for grouping in UI.
    /// </summary>
    public TrackedDataCategory Category { get; init; }

    /// <summary>
    /// The item ID in the game data, if applicable (e.g., for tomestones, scrips).
    /// </summary>
    public uint? ItemId { get; init; }

    /// <summary>
    /// Maximum possible value (for graph scaling).
    /// </summary>
    public long MaxValue { get; init; } = 999_999_999;

    /// <summary>
    /// Whether this type is enabled by default for new installations.
    /// </summary>
    public bool EnabledByDefault { get; init; } = false;

    /// <summary>
    /// Description of what this data type tracks.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The variable name used in the database for this type.
    /// </summary>
    public string VariableName => Type.ToString();

    /// <summary>
    /// Icon ID from game data, if applicable.
    /// </summary>
    public uint? IconId { get; init; }
}
