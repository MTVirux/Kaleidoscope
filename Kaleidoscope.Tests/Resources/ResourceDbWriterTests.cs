using Microsoft.Data.Sqlite;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class ResourceDbWriterTests
{
    public const string SchemaSql = @"
        CREATE TABLE resources (
            owner_id INTEGER NOT NULL, owner_kind INTEGER NOT NULL,
            parent_owner_id INTEGER NOT NULL DEFAULT 0, container INTEGER NOT NULL,
            item_id INTEGER NOT NULL, slot INTEGER NOT NULL,
            quantity INTEGER NOT NULL, flags INTEGER NOT NULL DEFAULT 0,
            spiritbond INTEGER NOT NULL DEFAULT 0, collectability INTEGER NOT NULL DEFAULT 0,
            condition INTEGER NOT NULL DEFAULT 0, glamour_id INTEGER NOT NULL DEFAULT 0,
            updated_at INTEGER NOT NULL,
            PRIMARY KEY (owner_id, owner_kind, container, slot));
        CREATE TABLE resource_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            owner_id INTEGER NOT NULL, owner_kind INTEGER NOT NULL,
            container INTEGER NOT NULL, item_id INTEGER NOT NULL,
            timestamp INTEGER NOT NULL, quantity INTEGER NOT NULL,
            change_amount INTEGER NOT NULL, source_kind INTEGER NOT NULL DEFAULT 0,
            source_detail TEXT);";

    private static SqliteConnection FreshConn()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaSql;
        cmd.ExecuteNonQuery();
        return conn;
    }

    private static ResourceWrite Write(long qty, long change, SourceKind src = SourceKind.Unknown)
        => new()
        {
            Resource = new Resource
            {
                Key = new ResourceKey { OwnerId = 1001, OwnerKind = OwnerKind.Player, Container = Container.Inventory1, ItemId = 5057, Slot = 0 },
                Quantity = qty,
                UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            ChangeAmount = change,
            SourceKind = src,
            SourceDetail = null,
        };

    [Fact]
    public void Flush_UpsertsResourceRow_AndAppendsHistory()
    {
        using var conn = FreshConn();
        var writer = new ResourceDbWriter(conn);

        writer.Enqueue(Write(qty: 10, change: 10));
        writer.FlushOnce();

        using var verify = conn.CreateCommand();
        verify.CommandText = "SELECT quantity FROM resources";
        Assert.Equal(10L, (long)verify.ExecuteScalar()!);

        verify.CommandText = "SELECT COUNT(*) FROM resource_history";
        Assert.Equal(1L, (long)verify.ExecuteScalar()!);
    }

    [Fact]
    public void Flush_ZeroChange_UpsertsButSkipsHistory()
    {
        using var conn = FreshConn();
        var writer = new ResourceDbWriter(conn);

        writer.Enqueue(Write(qty: 10, change: 10));
        writer.FlushOnce();
        writer.Enqueue(Write(qty: 10, change: 0));
        writer.FlushOnce();

        using var verify = conn.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM resources";
        Assert.Equal(1L, (long)verify.ExecuteScalar()!);

        verify.CommandText = "SELECT COUNT(*) FROM resource_history";
        Assert.Equal(1L, (long)verify.ExecuteScalar()!);
    }

    [Fact]
    public void Flush_BatchAtomic_AllRowsLand()
    {
        using var conn = FreshConn();
        var writer = new ResourceDbWriter(conn);

        writer.Enqueue(Write(qty: 10, change: 10));
        writer.Enqueue(Write(qty: 20, change: 10));
        writer.Enqueue(Write(qty: 30, change: 10));
        writer.FlushOnce();

        using var verify = conn.CreateCommand();
        verify.CommandText = "SELECT quantity FROM resources";
        Assert.Equal(30L, (long)verify.ExecuteScalar()!);

        verify.CommandText = "SELECT COUNT(*) FROM resource_history";
        Assert.Equal(3L, (long)verify.ExecuteScalar()!);
    }
}
