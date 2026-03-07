namespace Kaleidoscope.Models;

public sealed class TrackedDataDefinition
{
    public TrackedDataType Type { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string ShortName { get; init; } = string.Empty;

    public TrackedDataCategory Category { get; init; }

    /// <summary>
    /// The item ID in the game data, if applicable (e.g., for tomestones, scrips).
    /// </summary>
    public uint? ItemId { get; init; }

    /// <summary>
    /// Maximum possible value (for graph scaling).
    /// </summary>
    public long MaxValue { get; init; } = 999_999_999;

    public bool EnabledByDefault { get; init; } = false;

    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The variable name used in the database for this type.
    /// </summary>
    public string VariableName => Type.ToString();

    public uint? IconId { get; init; }

    /// <summary>
    /// Whether this data type is calculated from external data (e.g., Universalis prices) 
    /// rather than read directly from game memory. Calculated types are sampled on a timer
    /// by their respective services rather than by InventoryChangeService.
    /// </summary>
    public bool IsCalculated { get; init; } = false;
}
