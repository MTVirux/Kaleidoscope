namespace Kaleidoscope.Services.Profiler;

/// <summary>
/// Immutable capture of profiler aggregate stats at a point in time. Produced by
/// ProfilerService.CaptureSnapshot and serialized by ProfilerCsvFormatter.
/// </summary>
public sealed record ProfilerSnapshot(
    DateTime TimestampUtc,
    IReadOnlyList<ProfilerSnapshot.Row> Rows,
    double MemMb,
    int Gc0,
    int Gc1,
    int Gc2)
{
    /// <summary>One profiled target. Kind is "window", "tool", or "child".</summary>
    public sealed record Row(
        string Target,
        string Kind,
        string? Parent,
        long Samples,
        double Avg,
        double P50,
        double P95,
        double P99,
        double Max,
        double Jitter);
}
