using System.Globalization;
using Kaleidoscope.Services.Profiler;
using Xunit;

namespace Kaleidoscope.Tests.Profiler;

public class ProfilerCsvFormatterTests
{
    private static ProfilerSnapshot Snap(params ProfilerSnapshot.Row[] rows)
        => new(new DateTime(2026, 6, 18, 12, 0, 0, DateTimeKind.Utc), rows, 182.4, 42, 7, 1);

    [Fact]
    public void Header_HasFifteenColumns()
    {
        Assert.Equal(
            "timestamp,target,kind,parent,samples,avg_ms,p50_ms,p95_ms,p99_ms,max_ms,jitter_ms,mem_mb,gc0,gc1,gc2",
            ProfilerCsvFormatter.Header);
        Assert.Equal(15, ProfilerCsvFormatter.Header.Split(',').Length);
    }

    [Fact]
    public void Format_ToolRow_MapsFieldsAndBlankMemoryColumns()
    {
        var snap = Snap(new ProfilerSnapshot.Row("DataTool", "tool", null, 420, 2.1, 1.8, 4.2, 6.0, 9.1, 7.3));
        var lines = ProfilerCsvFormatter.Format(snap).ToList();

        Assert.Equal(
            "2026-06-18T12:00:00.000Z,DataTool,tool,,420,2.100,1.800,4.200,6.000,9.100,7.300,,,,",
            lines[0]);
    }

    [Fact]
    public void Format_ChildRow_SetsParent()
    {
        var snap = Snap(new ProfilerSnapshot.Row("LoadSeries", "child", "DataTool", 420, 1.3, 1.1, 2.8, 4.0, 5.5, 4.4));
        var lines = ProfilerCsvFormatter.Format(snap).ToList();

        Assert.StartsWith("2026-06-18T12:00:00.000Z,LoadSeries,child,DataTool,420,", lines[0]);
    }

    [Fact]
    public void Format_AlwaysAppendsRuntimeRowLast()
    {
        var snap = Snap(new ProfilerSnapshot.Row("Main Window", "window", null, 600, 0.842, 0.7, 1.9, 3.1, 5.2, 4.5));
        var lines = ProfilerCsvFormatter.Format(snap).ToList();

        Assert.Equal(2, lines.Count);
        Assert.Equal(
            "2026-06-18T12:00:00.000Z,Process,runtime,,,,,,,,,182.40,42,7,1",
            lines[^1]);
    }

    [Fact]
    public void Format_EscapesTargetContainingComma()
    {
        var snap = Snap(new ProfilerSnapshot.Row("Item Sales, History", "tool", null, 1, 1.0, 1.0, 1.0, 1.0, 1.0, 0.0));
        var lines = ProfilerCsvFormatter.Format(snap).ToList();

        Assert.Contains("\"Item Sales, History\"", lines[0]);
    }

    [Fact]
    public void Format_UsesInvariantCulture_UnderCommaDecimalCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // comma decimal separator
            var snap = Snap(new ProfilerSnapshot.Row("T", "tool", null, 1, 2.5, 0, 0, 0, 0, 0));
            var line = ProfilerCsvFormatter.Format(snap).First();
            Assert.Contains(",2.500,", line); // dot decimal, not comma
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
