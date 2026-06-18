using System.Globalization;

namespace Kaleidoscope.Services.Profiler;

/// <summary>
/// Serializes a ProfilerSnapshot to CSV lines (one per row plus a trailing runtime row).
/// Pure and Dalamud-free; unit tested.
/// </summary>
public static class ProfilerCsvFormatter
{
    public const string Header =
        "timestamp,target,kind,parent,samples,avg_ms,p50_ms,p95_ms,p99_ms,max_ms,jitter_ms,mem_mb,gc0,gc1,gc2";

    public static IEnumerable<string> Format(ProfilerSnapshot snapshot)
    {
        var ts = snapshot.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        foreach (var r in snapshot.Rows)
        {
            yield return string.Join(',',
                ts,
                Escape(r.Target),
                r.Kind,
                Escape(r.Parent ?? string.Empty),
                r.Samples.ToString(CultureInfo.InvariantCulture),
                Num(r.Avg), Num(r.P50), Num(r.P95), Num(r.P99), Num(r.Max), Num(r.Jitter),
                "", "", "", "");
        }

        yield return string.Join(',',
            ts, "Process", "runtime", "",
            "", "", "", "", "", "", "",
            snapshot.MemMb.ToString("F2", CultureInfo.InvariantCulture),
            snapshot.Gc0.ToString(CultureInfo.InvariantCulture),
            snapshot.Gc1.ToString(CultureInfo.InvariantCulture),
            snapshot.Gc2.ToString(CultureInfo.InvariantCulture));
    }

    private static string Num(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static string Escape(string field)
    {
        if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
