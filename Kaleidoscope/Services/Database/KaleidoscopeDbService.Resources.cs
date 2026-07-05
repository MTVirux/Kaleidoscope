using Kaleidoscope.Services.Resources;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{
    /// <summary>
    /// Schema DDL for the unified resources subsystem (schema version 2).
    /// Idempotent — safe to run repeatedly via CREATE IF NOT EXISTS.
    /// </summary>
    private const string ResourcesSchemaSql = @"
CREATE TABLE IF NOT EXISTS resources (
    owner_id        INTEGER NOT NULL,
    owner_kind      INTEGER NOT NULL,
    parent_owner_id INTEGER NOT NULL DEFAULT 0,
    container       INTEGER NOT NULL,
    item_id         INTEGER NOT NULL,
    slot            INTEGER NOT NULL,
    quantity        INTEGER NOT NULL,
    flags           INTEGER NOT NULL DEFAULT 0,
    spiritbond      INTEGER NOT NULL DEFAULT 0,
    collectability  INTEGER NOT NULL DEFAULT 0,
    condition       INTEGER NOT NULL DEFAULT 0,
    glamour_id      INTEGER NOT NULL DEFAULT 0,
    updated_at      INTEGER NOT NULL,
    PRIMARY KEY (owner_id, owner_kind, container, slot)
);

CREATE INDEX IF NOT EXISTS idx_resources_item       ON resources(item_id);
CREATE INDEX IF NOT EXISTS idx_resources_owner_item ON resources(owner_id, item_id);
CREATE INDEX IF NOT EXISTS idx_resources_parent     ON resources(parent_owner_id, owner_kind);

CREATE TABLE IF NOT EXISTS resource_history (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_id        INTEGER NOT NULL,
    owner_kind      INTEGER NOT NULL,
    container       INTEGER NOT NULL,
    item_id         INTEGER NOT NULL,
    timestamp       INTEGER NOT NULL,
    quantity        INTEGER NOT NULL,
    change_amount   INTEGER NOT NULL,
    source_kind     INTEGER NOT NULL DEFAULT 0,
    source_detail   TEXT
);

CREATE INDEX IF NOT EXISTS idx_history_item_time  ON resource_history(item_id, owner_id, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_history_owner_time ON resource_history(owner_id, timestamp DESC);

CREATE TABLE IF NOT EXISTS owner_names (
    owner_id    INTEGER NOT NULL,
    owner_kind  INTEGER NOT NULL,
    name        TEXT NOT NULL,
    world       TEXT,
    updated_at  INTEGER NOT NULL,
    PRIMARY KEY (owner_id, owner_kind)
);
";

    /// <summary>
    /// Apply schema DDL for the resources subsystem. Idempotent (CREATE IF NOT EXISTS).
    /// Called from EnsureSchema during DB initialization.
    /// </summary>
    private void ApplyResourcesSchema()
    {
        if (_connection == null) return;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = ResourcesSchemaSql;
        cmd.ExecuteNonQuery();
    }

    private bool TableExists(string name)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() != null;
    }

    private void BackfillResourcesFromInventoryItems()
    {
        if (_connection == null) return;
        if (!TableExists("inventory_items")) return;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = MigrationSql.BackfillResourcesFromInventoryItemsSql;
        var rows = cmd.ExecuteNonQuery();
        LogService.Debug(LogCategory.Database, $"[Migration v6] Backfilled {rows} resources rows from inventory_items");
    }

    private void BackfillGilRowsFromInventoryCache()
    {
        if (_connection == null) return;
        if (!TableExists("inventory_cache")) return;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = MigrationSql.BackfillGilRowsSql;
        var rows = cmd.ExecuteNonQuery();
        LogService.Debug(LogCategory.Database, $"[Migration v6] Backfilled {rows} gil rows");
    }

    private void BackfillResourceHistoryFromSeries()
    {
        if (_connection == null) return;
        if (!TableExists("series") || !TableExists("points")) return;
        var (written, skipped) = MigrationSql.BackfillResourceHistoryFromSeries(_connection, null);
        LogService.Info(LogCategory.Database, $"[Migration v6] resource_history: wrote {written} rows, skipped {skipped} unrecognized series");
    }

    /// <summary>
    /// Construct the ResourceDbWriter for production use against this DB service's
    /// writer connection. Returns null if the connection isn't open yet (caller must defer).
    /// </summary>
    public ResourceDbWriter? CreateResourceDbWriter()
    {
        if (_connection == null) return null;
        return new ResourceDbWriter(_connection);
    }

    /// <summary>
    /// Migration v7: drop the legacy inventory_cache, inventory_items, series, and points
    /// tables. Phase 2 disabled all writers to these tables; Phase 3 removes them entirely.
    /// VACUUMs after the drop to reclaim disk space.
    /// </summary>
    private void MigrateDropLegacyTables()
    {
        if (_connection == null) return;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                // Drop dependent tables before parents (FK cascade is enabled per existing schema).
                ExecuteDropDdl("DROP TABLE IF EXISTS inventory_items;");
                ExecuteDropDdl("DROP TABLE IF EXISTS inventory_cache;");
                ExecuteDropDdl("DROP TABLE IF EXISTS points;");
                ExecuteDropDdl("DROP TABLE IF EXISTS series;");

                // VACUUM forbids running inside a transaction. The existing RunMigrations
                // chain runs each migration as a direct ExecuteNonQuery (not wrapped in a
                // transaction), so VACUUM is safe here.
                ExecuteDropDdl("VACUUM;");

                LogService.Info(LogCategory.Database, "[Migration v7] Dropped inventory_cache/inventory_items/series/points and VACUUMed");
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[Migration v7] Failed: {ex.Message}", ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Migration v8: storage optimization. Drops dead/retired tables (price_history was never
    /// written; series/points/inventory_items/inventory_cache are v7 leftovers EnsureSchema used
    /// to recreate; inventory_value_items breakdown is no longer persisted), trims sale_records
    /// to the recent-N ring, and swaps sale indexes for the ring index. No VACUUM here — the
    /// startup maintenance pass that follows reclaims space when the freelist is large.
    /// </summary>
    private void MigrateStorageOptimization()
    {
        if (_connection == null) return;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            ExecuteDropDdl("DROP TABLE IF EXISTS price_history;");
            ExecuteDropDdl("DROP TABLE IF EXISTS inventory_value_items;");
            ExecuteDropDdl("DROP TABLE IF EXISTS inventory_items;");
            ExecuteDropDdl("DROP TABLE IF EXISTS inventory_cache;");
            ExecuteDropDdl("DROP TABLE IF EXISTS points;");
            ExecuteDropDdl("DROP TABLE IF EXISTS series;");
            ExecuteDropDdl("DROP INDEX IF EXISTS idx_sale_records_item;");
            ExecuteDropDdl("DROP INDEX IF EXISTS idx_sale_records_item_world;");
            ExecuteDropDdl("CREATE INDEX IF NOT EXISTS idx_sale_records_ring ON sale_records(item_id, world_id, is_hq, timestamp DESC);");

            using (var trimCmd = _connection.CreateCommand())
            {
                trimCmd.CommandText = StorageSql.TrimSaleRingAllSql;
                trimCmd.Parameters.AddWithValue("$keep", StorageSql.SaleRingKeep);
                var trimmed = trimCmd.ExecuteNonQuery();
                LogService.Info(LogCategory.Database, $"[Migration v8] Trimmed {trimmed} sale_records rows to recent-{StorageSql.SaleRingKeep} ring");
            }

            LogService.Info(LogCategory.Database, "[Migration v8] Storage optimization migration complete");
        }
    }

    private void ExecuteDropDdl(string sql)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

}
