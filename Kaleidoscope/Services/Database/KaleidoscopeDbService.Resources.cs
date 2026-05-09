using Microsoft.Data.Sqlite;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{
    /// <summary>
    /// Schema DDL for the unified resources subsystem (schema version 2).
    /// Idempotent — safe to run repeatedly via CREATE IF NOT EXISTS.
    /// </summary>
    private const string ResourcesSchemaSql = @"
CREATE TABLE IF NOT EXISTS meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT OR IGNORE INTO meta(key, value) VALUES ('schema_version', '1');

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

    /// <summary>
    /// Read the current schema version from the meta table. Returns 1 if meta is missing
    /// or empty (treats unversioned databases as version 1).
    /// </summary>
    public int GetSchemaVersion()
    {
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return 1;
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? 1 : int.Parse((string)result);
            }
            catch
            {
                // meta table doesn't exist yet — pre-migration state
                return 1;
            }
        }
    }

    private void SetSchemaVersion(int version, SqliteTransaction? tx = null)
    {
        if (_connection == null) return;
        using var cmd = _connection.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', $v)";
        cmd.Parameters.AddWithValue("$v", version.ToString());
        cmd.ExecuteNonQuery();
    }
}
