using Kaleidoscope.Services.Profiler;
using Xunit;

namespace Kaleidoscope.Tests.Profiler;

public class RotatingFileWriterTests
{
    private static string FreshDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kaleido_rfw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void WriteLine_AppendsLines()
    {
        var dir = FreshDir();
        var path = Path.Combine(dir, "events.log");
        using (var w = new RotatingFileWriter(path, maxSizeMB: 1))
        {
            w.WriteLine("alpha");
            w.WriteLine("beta");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(new[] { "alpha", "beta" }, lines);
    }

    [Fact]
    public void NewFile_WithHeader_StartsWithHeader()
    {
        var dir = FreshDir();
        var path = Path.Combine(dir, "data.csv");
        using (var w = new RotatingFileWriter(path, maxSizeMB: 1, headerLine: "h1,h2"))
        {
            w.WriteLine("1,2");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal("h1,h2", lines[0]);
        Assert.Equal("1,2", lines[1]);
    }

    [Fact]
    public void ExceedingMaxSize_RotatesAndReopensWithHeader()
    {
        var dir = FreshDir();
        var path = Path.Combine(dir, "data.csv");
        var bigLine = new string('x', 1024);

        using (var w = new RotatingFileWriter(path, maxSizeMB: 1, headerLine: "header"))
        {
            // ~1.1MB of data forces at least one rotation (max is 1MB).
            for (var i = 0; i < 1100; i++)
                w.WriteLine(bigLine);
        }

        var rotated = Directory.GetFiles(dir, "data_*.csv");
        Assert.NotEmpty(rotated);                            // a rotated file was produced
        Assert.Equal("header", File.ReadAllLines(path)[0]);  // live file restarted with header
    }
}
