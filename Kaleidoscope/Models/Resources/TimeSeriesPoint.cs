namespace Kaleidoscope.Models.Resources;

/// <summary>
/// One historical observation of a resource quantity. Returned by ResourceStore.GetHistory.
/// </summary>
public readonly record struct TimeSeriesPoint
{
    public DateTime Timestamp { get; init; }

    public long Quantity { get; init; }

    public long ChangeAmount { get; init; }

    public SourceKind Source { get; init; }
}
