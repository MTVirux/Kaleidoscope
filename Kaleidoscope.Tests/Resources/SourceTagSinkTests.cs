using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class SourceTagSinkTests
{
    [Fact]
    public void Stamp_ThenConsume_ReturnsTagAndClears()
    {
        var sink = new SourceTagSink(now: () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        sink.Stamp(new SourceTag { Kind = SourceKind.DutyReward, Detail = "The Praetorium", StampedAt = sink.Now() }, ttl: TimeSpan.FromSeconds(5));

        var tag = sink.ConsumeIfFresh();

        Assert.NotNull(tag);
        Assert.Equal(SourceKind.DutyReward, tag!.Value.Kind);
        Assert.Equal("The Praetorium", tag.Value.Detail);
        Assert.Null(sink.ConsumeIfFresh());
    }

    [Fact]
    public void Stamp_ExpiredAtConsume_ReturnsNullAndClears()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = t;
        var sink = new SourceTagSink(now: () => clock);

        sink.Stamp(new SourceTag { Kind = SourceKind.DutyReward, StampedAt = t }, ttl: TimeSpan.FromSeconds(5));
        clock = t.AddSeconds(6);

        Assert.Null(sink.ConsumeIfFresh());
        Assert.Null(sink.ConsumeIfFresh());
    }

    [Fact]
    public void Stamp_OverwritesExistingTag()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sink = new SourceTagSink(now: () => t);

        sink.Stamp(new SourceTag { Kind = SourceKind.Trade, StampedAt = t },     ttl: TimeSpan.FromSeconds(5));
        sink.Stamp(new SourceTag { Kind = SourceKind.DutyReward, StampedAt = t }, ttl: TimeSpan.FromSeconds(5));

        var tag = sink.ConsumeIfFresh();
        Assert.Equal(SourceKind.DutyReward, tag!.Value.Kind);
    }
}
