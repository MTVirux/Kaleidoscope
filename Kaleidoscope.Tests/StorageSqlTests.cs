// Kaleidoscope.Tests/StorageSqlTests.cs
using Microsoft.Data.Sqlite;
using Kaleidoscope.Services.Database;
using Xunit;

namespace Kaleidoscope.Tests;

public sealed class StorageSqlTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public StorageSqlTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Exec(@"CREATE TABLE sale_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            item_id INTEGER NOT NULL, world_id INTEGER NOT NULL,
            price_per_unit INTEGER NOT NULL, quantity INTEGER NOT NULL DEFAULT 1,
            is_hq INTEGER NOT NULL DEFAULT 0, total INTEGER NOT NULL,
            timestamp INTEGER NOT NULL, buyer_name TEXT);");
        Exec("CREATE INDEX idx_sale_records_ring ON sale_records(item_id, world_id, is_hq, timestamp DESC);");
    }

    private void Exec(string sql, params (string, object)[] args)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private void InsertSale(int item, int world, bool hq, long ts, int price = 100) =>
        Exec("INSERT INTO sale_records (item_id, world_id, price_per_unit, quantity, is_hq, total, timestamp) VALUES ($i,$w,$p,1,$h,$p,$t)",
            ("$i", item), ("$w", world), ("$p", price), ("$h", hq ? 1 : 0), ("$t", ts));

    private long Count(string where = "1=1")
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sale_records WHERE {where}";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void TrimForKey_KeepsNewestN_PerHqFlag()
    {
        for (var i = 0; i < 15; i++) InsertSale(101, 40, hq: false, ts: 1000 + i);
        for (var i = 0; i < 15; i++) InsertSale(101, 40, hq: true, ts: 2000 + i);
        InsertSale(999, 40, hq: false, ts: 1); // other item untouched

        Exec(StorageSql.TrimSaleRingForKeySql, ("$iid", 101), ("$wid", 40), ("$hq", 0), ("$keep", StorageSql.SaleRingKeep));

        Assert.Equal(10, Count("item_id=101 AND is_hq=0"));
        Assert.Equal(15, Count("item_id=101 AND is_hq=1")); // hq=1 not trimmed by hq=0 call
        Assert.Equal(1, Count("item_id=999"));
        // survivors are the NEWEST 10
        Assert.Equal(0, Count("item_id=101 AND is_hq=0 AND timestamp < 1005"));
    }

    [Fact]
    public void TrimAll_KeepsNewestN_PerItemWorldHq()
    {
        for (var i = 0; i < 12; i++) InsertSale(1, 40, false, 100 + i);
        for (var i = 0; i < 12; i++) InsertSale(1, 41, false, 100 + i); // different world
        for (var i = 0; i < 3; i++) InsertSale(2, 40, true, 100 + i);   // under cap

        Exec(StorageSql.TrimSaleRingAllSql, ("$keep", StorageSql.SaleRingKeep));

        Assert.Equal(10, Count("item_id=1 AND world_id=40"));
        Assert.Equal(10, Count("item_id=1 AND world_id=41"));
        Assert.Equal(3, Count("item_id=2"));
    }

    [Fact]
    public void TrimForKey_TieBreaksOnId_WhenTimestampsEqual()
    {
        for (var i = 0; i < 12; i++) InsertSale(7, 40, false, ts: 5555); // same-tick batch
        Exec(StorageSql.TrimSaleRingForKeySql, ("$iid", 7), ("$wid", 40), ("$hq", 0), ("$keep", StorageSql.SaleRingKeep));
        Assert.Equal(10, Count("item_id=7"));
        // newest ids survive
        Assert.Equal(0, Count("item_id=7 AND id <= 2"));
    }

    public void Dispose() => _conn.Dispose();
}
