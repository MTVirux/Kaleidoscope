using Microsoft.Data.Sqlite;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class ResourceObservationServiceTests
{
    private static SqliteConnection Conn()
    {
        var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = ResourceDbWriterTests.SchemaSql;
        cmd.ExecuteNonQuery();
        return c;
    }

    private static ResourceObservation Obs(long qty, OwnerKind kind = OwnerKind.Player) => new()
    {
        Key = new ResourceKey { OwnerId = 1001, OwnerKind = kind, Container = Container.Inventory1, ItemId = 5057, Slot = 0 },
        Quantity = qty,
        UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void RecordObservation_NewKey_UpdatesStoreAndQueuesWrite()
    {
        using var conn = Conn();
        var store  = new ResourceStore();
        var writer = new ResourceDbWriter(conn);
        var sink   = new SourceTagSink();
        var svc    = new ResourceObservationService(store, writer, sink);

        svc.RecordObservation(Obs(10));

        Assert.Equal(10, store.Get(Obs(10).ToResource().Key)!.Value.Quantity);
        Assert.Equal(1, writer.PendingCount);
    }

    [Fact]
    public void RecordObservation_IdempotentValue_DoesNotQueueDb()
    {
        using var conn = Conn();
        var store  = new ResourceStore();
        var writer = new ResourceDbWriter(conn);
        var sink   = new SourceTagSink();
        var svc    = new ResourceObservationService(store, writer, sink);

        svc.RecordObservation(Obs(10));
        writer.FlushOnce();

        svc.RecordObservation(Obs(10));
        Assert.Equal(0, writer.PendingCount);
    }

    [Fact]
    public void RecordObservation_ConsumesPendingSourceTag()
    {
        using var conn = Conn();
        var store  = new ResourceStore();
        var writer = new ResourceDbWriter(conn);
        var clock  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sink   = new SourceTagSink(now: () => clock);
        var svc    = new ResourceObservationService(store, writer, sink);

        sink.Stamp(new SourceTag { Kind = SourceKind.DutyReward, Detail = "TestDuty", StampedAt = clock }, ttl: TimeSpan.FromSeconds(5));
        svc.RecordObservation(Obs(50));
        writer.FlushOnce();

        using var verify = conn.CreateCommand();
        verify.CommandText = "SELECT source_kind, source_detail FROM resource_history";
        using var r = verify.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal((int)SourceKind.DutyReward, r.GetInt64(0));
        Assert.Equal("TestDuty", r.GetString(1));

        Assert.Null(sink.ConsumeIfFresh());
    }
}
