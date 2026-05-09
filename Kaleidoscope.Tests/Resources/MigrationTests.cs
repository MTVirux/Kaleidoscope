using Microsoft.Data.Sqlite;
using Xunit;

namespace Kaleidoscope.Tests.Resources;

public class MigrationTests
{
    /// <summary>
    /// Build a fresh in-memory connection that simulates an old (v1, unversioned) database
    /// with the legacy inventory_cache / inventory_items / series / points schema populated.
    /// Used as the input to schema migrations under test.
    /// </summary>
    internal static SqliteConnection NewLegacyDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE inventory_cache (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                character_id INTEGER NOT NULL,
                source_type INTEGER NOT NULL,
                retainer_id INTEGER NOT NULL DEFAULT 0,
                name TEXT, world TEXT,
                gil INTEGER NOT NULL DEFAULT 0,
                updated_at INTEGER NOT NULL,
                UNIQUE (character_id, source_type, retainer_id)
            );
            CREATE TABLE inventory_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                cache_id INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL,
                is_hq INTEGER NOT NULL DEFAULT 0,
                is_collectable INTEGER NOT NULL DEFAULT 0,
                slot INTEGER NOT NULL,
                container_type INTEGER NOT NULL,
                spiritbond INTEGER NOT NULL DEFAULT 0,
                condition INTEGER NOT NULL DEFAULT 0,
                glamour_id INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE series (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                variable TEXT NOT NULL,
                character_id INTEGER NOT NULL
            );
            CREATE TABLE points (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                series_id INTEGER NOT NULL,
                timestamp INTEGER NOT NULL,
                value INTEGER NOT NULL
            );
            CREATE TABLE resources (
                owner_id INTEGER NOT NULL, owner_kind INTEGER NOT NULL,
                parent_owner_id INTEGER NOT NULL DEFAULT 0, container INTEGER NOT NULL,
                item_id INTEGER NOT NULL, slot INTEGER NOT NULL,
                quantity INTEGER NOT NULL, flags INTEGER NOT NULL DEFAULT 0,
                spiritbond INTEGER NOT NULL DEFAULT 0, collectability INTEGER NOT NULL DEFAULT 0,
                condition INTEGER NOT NULL DEFAULT 0, glamour_id INTEGER NOT NULL DEFAULT 0,
                updated_at INTEGER NOT NULL,
                PRIMARY KEY (owner_id, owner_kind, container, slot)
            );";
        cmd.ExecuteNonQuery();
        return conn;
    }

    [Fact]
    public void BackfillResourcesFromInventoryItems_DemuxesCollectableAndComputesFlags()
    {
        using var conn = NewLegacyDb();

        using (var seed = conn.CreateCommand())
        {
            seed.CommandText = @"
                INSERT INTO inventory_cache (character_id, source_type, retainer_id, name, world, gil, updated_at)
                    VALUES (1001, 0, 0, 'Player1', 'Cerberus', 999, 638000000000000000);
                INSERT INTO inventory_items (cache_id, item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id)
                    VALUES (1, 5057, 42, 1, 0, 7, 0, 0, 30000, 0);
                INSERT INTO inventory_items (cache_id, item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id)
                    VALUES (1, 36256, 1, 0, 1, 0, 0, 1500, 30000, 0);";
            seed.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = MigrationSqlExposed.BackfillResourcesFromInventoryItemsSql;
            cmd.ExecuteNonQuery();
        }

        using var verify = conn.CreateCommand();
        verify.CommandText = @"
            SELECT owner_id, owner_kind, container, item_id, slot, quantity, flags, spiritbond, collectability, condition
            FROM resources ORDER BY item_id";
        using var r = verify.ExecuteReader();

        Assert.True(r.Read());
        Assert.Equal(1001L, r.GetInt64(0));
        Assert.Equal(0L,    r.GetInt64(1));
        Assert.Equal(0L,    r.GetInt64(2));
        Assert.Equal(5057L, r.GetInt64(3));
        Assert.Equal(7L,    r.GetInt64(4));
        Assert.Equal(42L,   r.GetInt64(5));
        Assert.Equal(1L,    r.GetInt64(6));    // flags = HQ
        Assert.Equal(0L,    r.GetInt64(7));    // spiritbond (HQ, not collectable)
        Assert.Equal(0L,    r.GetInt64(8));    // collectability

        Assert.True(r.Read());
        Assert.Equal(36256L, r.GetInt64(3));
        Assert.Equal(2L,     r.GetInt64(6));    // flags = Collectable
        Assert.Equal(0L,     r.GetInt64(7));    // spiritbond (collectable, demuxed)
        Assert.Equal(1500L,  r.GetInt64(8));    // collectability (demuxed)

        Assert.False(r.Read());
    }
}
