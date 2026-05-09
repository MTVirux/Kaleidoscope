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

    private void BackfillResourcesFromInventoryItems()
    {
        if (_connection == null) return;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = MigrationSql.BackfillResourcesFromInventoryItemsSql;
        var rows = cmd.ExecuteNonQuery();
        LogService.Debug(LogCategory.Database, $"[Migration v6] Backfilled {rows} resources rows from inventory_items");
    }

    private void BackfillGilRowsFromInventoryCache()
    {
        if (_connection == null) return;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = MigrationSql.BackfillGilRowsSql;
        var rows = cmd.ExecuteNonQuery();
        LogService.Debug(LogCategory.Database, $"[Migration v6] Backfilled {rows} gil rows");
    }

}
