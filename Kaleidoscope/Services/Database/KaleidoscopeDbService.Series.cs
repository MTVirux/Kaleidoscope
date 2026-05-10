using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Kaleidoscope.Services.Resources.Adapters;
using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

/// <summary>
/// Time-series query methods, rewritten for Phase 3 to operate against
/// resource_history / resources / owner_names rather than the dropped
/// legacy series / points tables.
///
/// Public API is preserved so callers (CurrencyTrackerService,
/// TimeSeriesCacheService, dev-UI) continue to compile unchanged.
/// Legacy variable names are translated via LegacyVariableTranslator.
/// </summary>
public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Ensures a resource_history entry can be stored for the given variable/character pair.
    /// After Phase 3 there is no separate "series" concept — this is a no-op that returns a
    /// synthetic non-null sentinel (1L) so callers that check for null still work.
    /// </summary>
    [Obsolete("Series table removed in Phase 3. Use SaveSampleIfChanged directly.")]
    public long? GetOrCreateSeries(string variable, ulong characterId)
    {
        // No series table exists; return a non-null sentinel so callers can proceed.
        // The actual insert is handled by SaveSampleIfChanged / SaveSamplesIfChangedBatched.
        return 1L;
    }

    /// <summary>
    /// Not meaningful after Phase 3 (no series-id concept). Returns null.
    /// </summary>
    [Obsolete("Series table removed in Phase 3. Use GetLatestHistoryValue instead.")]
    public long? GetLastValue(long seriesId)
    {
        // The seriesId concept is gone. Callers that used this in combination with
        // GetOrCreateSeries should migrate to GetLatestHistoryValue(itemId, ownerId, container).
        return null;
    }

    /// <summary>
    /// Returns the most recent quantity for the given variable and character.
    /// Translates the legacy variable name to resource_history coordinates.
    /// </summary>
    public long? GetLastValueForCharacter(string variable, ulong characterId)
    {
        var mapping = LegacyVariableTranslator.Translate(variable, characterId);
        if (mapping == null) return null;

        return GetLatestHistoryValue(mapping.Value.ItemId, mapping.Value.OwnerId, (int)mapping.Value.Container);
    }

    /// <summary>
    /// Returns the most recent (timestamp, quantity) for the given variable and character.
    /// Used by cache pre-population on startup.
    /// </summary>
    public (DateTime timestamp, long value)? GetLastPointForCharacter(string variable, ulong characterId)
    {
        var mapping = LegacyVariableTranslator.Translate(variable, characterId);
        if (mapping == null) return null;

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return null;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT timestamp, quantity FROM resource_history
                    WHERE item_id = $iid AND owner_id = $oid AND container = $cont
                    ORDER BY timestamp DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
                cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var ticks = reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    return (new DateTime(ticks, DateTimeKind.Utc), value);
                }

                return null;
            }
            catch (Exception ex)
            {
                LogDbDebug("GetLastPointForCharacter", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Returns the latest recorded quantity per owner for the given variable.
    /// Used by the table-view UI to display current values.
    /// </summary>
    public Dictionary<ulong, long> GetLatestValuesForVariable(string variable)
    {
        var result = new Dictionary<ulong, long>();

        // characterId=0 — we want all owners, mapping only needs the type part.
        var mapping = LegacyVariableTranslator.Translate(variable, 0);
        if (mapping == null) return result;

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT owner_id, quantity FROM (
                        SELECT owner_id, quantity,
                               ROW_NUMBER() OVER (PARTITION BY owner_id ORDER BY timestamp DESC) AS rn
                        FROM resource_history
                        WHERE item_id = $iid AND container = $cont AND owner_id != 0
                    ) ranked
                    WHERE rn = 1";
                cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ownerId = (ulong)reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    if (ownerId == 0) continue;
                    result[ownerId] = value;
                }
            }
            catch (Exception ex)
            {
                LogDbDebug("GetLatestValuesForVariable", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Not used after Phase 3 (no series-id concept). Returns false.
    /// Callers should use SaveSampleIfChanged instead.
    /// </summary>
    [Obsolete("Series table removed in Phase 3. Use SaveSampleIfChanged instead.")]
    public bool InsertPoint(long seriesId, long value, DateTime? timestamp = null)
    {
        return false;
    }

    /// <summary>
    /// Saves a sample value for a variable/character pair, only inserting if different
    /// from the last recorded value. Returns true if a new row was inserted.
    /// After Phase 3 writes to resource_history instead of the dropped points table.
    /// </summary>
    public bool SaveSampleIfChanged(string variable, ulong characterId, long value)
    {
        var mapping = LegacyVariableTranslator.Translate(variable, characterId);
        if (mapping == null) return false;

        var lastValue = GetLatestHistoryValue(mapping.Value.ItemId, mapping.Value.OwnerId, (int)mapping.Value.Container);
        if (lastValue.HasValue && lastValue.Value == value)
            return false;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO resource_history (owner_id, owner_kind, container, item_id, timestamp, quantity, change_amount, source_kind, source_detail)
                    VALUES ($oid, $okind, $cont, $iid, $ts, $qty, 0, 0, NULL)";
                cmd.Parameters.AddWithValue("$oid",   (long)mapping.Value.OwnerId);
                cmd.Parameters.AddWithValue("$okind", (int)mapping.Value.OwnerKind);
                cmd.Parameters.AddWithValue("$cont",  (int)mapping.Value.Container);
                cmd.Parameters.AddWithValue("$iid",   (long)mapping.Value.ItemId);
                cmd.Parameters.AddWithValue("$ts",    DateTime.UtcNow.Ticks);
                cmd.Parameters.AddWithValue("$qty",   value);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                LogDbError("SaveSampleIfChanged", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Saves multiple sample values in a single transaction, only inserting those that
    /// differ from their last recorded value.
    /// After Phase 3 writes to resource_history instead of the dropped points table.
    /// Returns the number of rows actually inserted.
    /// </summary>
    public int SaveSamplesIfChangedBatched(List<(string Variable, ulong CharacterId, long Value)> samples)
    {
        if (samples == null || samples.Count == 0) return 0;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                // Phase 1: resolve mappings and check last values under the write lock.
                var rowsToInsert = new List<(long ItemId, long OwnerId, int OwnerKind, int Container, long Value)>();

                foreach (var (variable, characterId, value) in samples)
                {
                    var mapping = LegacyVariableTranslator.Translate(variable, characterId);
                    if (mapping == null) continue;

                    // Inline last-value check using the write connection (avoids _readLock
                    // while holding _writeLock — see lock-ordering invariant).
                    using var lastCmd = _connection.CreateCommand();
                    lastCmd.CommandText = @"
                        SELECT quantity FROM resource_history
                        WHERE item_id = $iid AND owner_id = $oid AND container = $cont
                        ORDER BY timestamp DESC LIMIT 1";
                    lastCmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                    lastCmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
                    lastCmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
                    var lastResult = lastCmd.ExecuteScalar();
                    if (lastResult != null && lastResult != DBNull.Value && (long)lastResult == value)
                        continue;

                    rowsToInsert.Add(((long)mapping.Value.ItemId, (long)mapping.Value.OwnerId, (int)mapping.Value.OwnerKind, (int)mapping.Value.Container, value));
                }

                if (rowsToInsert.Count == 0) return 0;

                // Phase 2: batch-insert all changed rows in a single transaction.
                return RunInTransaction(tx =>
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO resource_history(owner_id, owner_kind, container, item_id, timestamp, quantity, change_amount, source_kind, source_detail)
                        VALUES($oid, $okind, $cont, $iid, $ts, $qty, 0, 0, NULL)";

                    var oidParam   = cmd.Parameters.Add("$oid",   SqliteType.Integer);
                    var okindParam = cmd.Parameters.Add("$okind", SqliteType.Integer);
                    var contParam  = cmd.Parameters.Add("$cont",  SqliteType.Integer);
                    var iidParam   = cmd.Parameters.Add("$iid",   SqliteType.Integer);
                    var tsParam    = cmd.Parameters.Add("$ts",    SqliteType.Integer);
                    var qtyParam   = cmd.Parameters.Add("$qty",   SqliteType.Integer);

                    var now = DateTime.UtcNow.Ticks;

                    foreach (var (iid, oid, okind, cont, qty) in rowsToInsert)
                    {
                        oidParam.Value   = oid;
                        okindParam.Value = okind;
                        contParam.Value  = cont;
                        iidParam.Value   = iid;
                        tsParam.Value    = now;
                        qtyParam.Value   = qty;
                        cmd.ExecuteNonQuery();
                    }

                    return rowsToInsert.Count;
                });
            }
            catch (Exception ex)
            {
                LogDbError("SaveSamplesIfChangedBatched", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Returns history points for a variable/character in ascending timestamp order.
    /// After Phase 3 queries resource_history instead of the dropped points table.
    /// </summary>
    public List<(DateTime timestamp, long value)> GetPoints(string variable, ulong characterId, int? limit = null)
    {
        var result = new List<(DateTime, long)>();

        var mapping = LegacyVariableTranslator.Translate(variable, characterId);
        if (mapping == null) return result;

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                var sql = @"SELECT timestamp, quantity FROM resource_history
                    WHERE item_id = $iid AND owner_id = $oid AND container = $cont
                    ORDER BY timestamp ASC";

                if (limit.HasValue)
                {
                    sql += " LIMIT $lim";
                    cmd.Parameters.AddWithValue("$lim", limit.Value);
                }

                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
                cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ticks = reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    result.Add((new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogDbDebug("GetPoints", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns history points for a variable/character after a cutoff time.
    /// Used for cache population on startup.
    /// After Phase 3 queries resource_history instead of the dropped points table.
    /// </summary>
    public List<(DateTime timestamp, long value)> GetPointsSince(string variable, ulong characterId, DateTime since)
    {
        var result = new List<(DateTime, long)>();

        var mapping = LegacyVariableTranslator.Translate(variable, characterId);
        if (mapping == null) return result;

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT timestamp, quantity FROM resource_history
                    WHERE item_id = $iid AND owner_id = $oid AND container = $cont
                      AND timestamp >= $since
                    ORDER BY timestamp ASC";
                cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
                cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
                cmd.Parameters.AddWithValue("$since", since.Ticks);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ticks = reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    result.Add((new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogDbDebug("GetPointsSince", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all history points for a variable across all owners.
    /// After Phase 3 queries resource_history instead of the dropped points table.
    /// </summary>
    public List<(ulong characterId, DateTime timestamp, long value)> GetAllPoints(string variable)
    {
        var result = new List<(ulong, DateTime, long)>();

        var mapping = LegacyVariableTranslator.Translate(variable, 0);
        if (mapping == null) return result;

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT owner_id, timestamp, quantity FROM resource_history
                    WHERE item_id = $iid AND container = $cont AND owner_id != 0
                    ORDER BY timestamp ASC";
                cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ownerId = (ulong)reader.GetInt64(0);
                    var ticks = reader.GetInt64(1);
                    var value = reader.GetInt64(2);
                    if (ownerId != 0)
                        result.Add((ownerId, new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogDbDebug("GetAllPoints", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all history points for variables matching a prefix, optionally filtered
    /// by a start time.  After Phase 3 queries resource_history using reverse-mapped
    /// (item_id, container) tuples derived from the prefix.
    /// </summary>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetAllPointsBatch(
        string variablePrefix, DateTime? since = null)
    {
        var result = new Dictionary<string, List<(ulong, DateTime, long)>>();

        // Resolve all (item_id, container) pairs that match the prefix by querying
        // resource_history directly, then reverse-map to legacy variable names.
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();

                // For legacy compatibility we need to return results keyed by variable name.
                // We determine the variable name from the (item_id, container, owner_id) triple
                // using the same reverse logic as GetAllVariablesWithPrefix.
                // First, find all candidate (item_id, container, owner_id) rows.
                if (since.HasValue)
                {
                    cmd.CommandText = @"
                        WITH series_max AS (
                            SELECT item_id, owner_id, container, MAX(timestamp) AS max_ts
                            FROM resource_history WHERE owner_id != 0
                            GROUP BY item_id, owner_id, container
                        )
                        SELECT rh.item_id, rh.owner_id, rh.container, rh.timestamp, rh.quantity
                        FROM series_max sm
                        JOIN resource_history rh
                            ON rh.item_id = sm.item_id AND rh.owner_id = sm.owner_id AND rh.container = sm.container
                        WHERE rh.timestamp >= $since OR rh.timestamp = sm.max_ts
                        ORDER BY rh.item_id, rh.owner_id, rh.timestamp";
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }
                else
                {
                    cmd.CommandText = @"SELECT item_id, owner_id, container, timestamp, quantity
                        FROM resource_history WHERE owner_id != 0
                        ORDER BY item_id, owner_id, timestamp ASC";
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var itemId  = (uint)reader.GetInt64(0);
                    var ownerId = (ulong)reader.GetInt64(1);
                    var cont    = (Container)reader.GetInt32(2);
                    var ticks   = reader.GetInt64(3);
                    var qty     = reader.GetInt64(4);

                    if (ownerId == 0) continue;

                    var varName = ReverseMapToVariableName(itemId, cont, ownerId);
                    if (varName == null || !varName.StartsWith(variablePrefix, StringComparison.Ordinal))
                        continue;

                    if (!result.TryGetValue(varName, out var list))
                    {
                        list = new List<(ulong, DateTime, long)>();
                        result[varName] = list;
                    }
                    list.Add((ownerId, new DateTime(ticks, DateTimeKind.Utc), qty));
                }
            }
            catch (Exception ex)
            {
                LogDbDebug("GetAllPointsBatch", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all history points for variables matching both a prefix and suffix.
    /// After Phase 3 queries resource_history using reverse-mapped variable names.
    /// </summary>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPointsBatchWithSuffix(
        string variablePrefix, string variableSuffix, DateTime? since = null)
    {
        var all = GetAllPointsBatch(variablePrefix, since);

        if (string.IsNullOrEmpty(variableSuffix))
            return all;

        var result = new Dictionary<string, List<(ulong, DateTime, long)>>();
        foreach (var kvp in all)
        {
            if (kvp.Key.EndsWith(variableSuffix, StringComparison.Ordinal))
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    /// <summary>
    /// Gets history points within a visible time window for variables matching a prefix,
    /// also including the latest point before the window for graph-line continuity.
    /// After Phase 3 queries resource_history.
    /// </summary>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPointsInWindow(
        string variablePrefix, DateTime windowStart, DateTime windowEnd)
    {
        var result = new Dictionary<string, List<(ulong, DateTime, long)>>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    WITH series_ids AS (
                        SELECT DISTINCT item_id, owner_id, container
                        FROM resource_history WHERE owner_id != 0
                    ),
                    last_before AS (
                        SELECT variable_row.item_id, variable_row.owner_id, variable_row.container,
                               rh.timestamp, rh.quantity
                        FROM series_ids variable_row
                        JOIN (
                            SELECT item_id, owner_id, container, timestamp, quantity,
                                   ROW_NUMBER() OVER (PARTITION BY item_id, owner_id, container ORDER BY timestamp DESC) AS rn
                            FROM resource_history
                            WHERE timestamp < $windowStart
                        ) rh ON rh.item_id = variable_row.item_id
                             AND rh.owner_id = variable_row.owner_id
                             AND rh.container = variable_row.container
                             AND rh.rn = 1
                    ),
                    in_window AS (
                        SELECT item_id, owner_id, container, timestamp, quantity
                        FROM resource_history
                        WHERE owner_id != 0
                          AND timestamp >= $windowStart AND timestamp <= $windowEnd
                    )
                    SELECT item_id, owner_id, container, timestamp, quantity FROM last_before
                    UNION ALL
                    SELECT item_id, owner_id, container, timestamp, quantity FROM in_window
                    ORDER BY item_id, owner_id, timestamp ASC";

                cmd.Parameters.AddWithValue("$windowStart", windowStart.Ticks);
                cmd.Parameters.AddWithValue("$windowEnd", windowEnd.Ticks);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var itemId  = (uint)reader.GetInt64(0);
                    var ownerId = (ulong)reader.GetInt64(1);
                    var cont    = (Container)reader.GetInt32(2);
                    var ticks   = reader.GetInt64(3);
                    var qty     = reader.GetInt64(4);

                    if (ownerId == 0) continue;

                    var varName = ReverseMapToVariableName(itemId, cont, ownerId);
                    if (varName == null || !varName.StartsWith(variablePrefix, StringComparison.Ordinal))
                        continue;

                    if (!result.TryGetValue(varName, out var list))
                    {
                        list = new List<(ulong, DateTime, long)>();
                        result[varName] = list;
                    }
                    list.Add((ownerId, new DateTime(ticks, DateTimeKind.Utc), qty));
                }
            }
            catch (Exception ex)
            {
                LogDbDebug("GetPointsInWindow", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the time range of available data for a variable prefix.
    /// After Phase 3 queries resource_history.
    /// </summary>
    public (DateTime earliest, DateTime latest)? GetDataTimeRange(string variablePrefix)
    {
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return null;

            try
            {
                // We need to find rows whose reverse-mapped variable name starts with the prefix.
                // For efficiency, handle the common case where the prefix is a TrackedDataType name
                // (e.g. "Gil") or an item prefix (e.g. "Item_", "ItemRetainer_").
                // Fall back to full scan if the prefix is short/ambiguous.
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT MIN(timestamp), MAX(timestamp) FROM resource_history
                    WHERE owner_id != 0";

                // Apply container filter for known prefix patterns to avoid scanning all rows.
                if (variablePrefix.StartsWith("Item_", StringComparison.Ordinal))
                {
                    cmd.CommandText = @"
                        SELECT MIN(timestamp), MAX(timestamp) FROM resource_history
                        WHERE owner_id != 0 AND container = $cont AND item_id < 1000000";
                    cmd.Parameters.AddWithValue("$cont", (int)Container.PlayerAggregate);
                }
                else if (variablePrefix.StartsWith("ItemRetainerX_", StringComparison.Ordinal))
                {
                    cmd.CommandText = @"
                        SELECT MIN(timestamp), MAX(timestamp) FROM resource_history
                        WHERE owner_id != 0 AND container = $cont AND item_id < 1000000";
                    cmd.Parameters.AddWithValue("$cont", (int)Container.RetainerPage1);
                }
                else if (variablePrefix.StartsWith("ItemRetainer_", StringComparison.Ordinal))
                {
                    cmd.CommandText = @"
                        SELECT MIN(timestamp), MAX(timestamp) FROM resource_history
                        WHERE owner_id != 0 AND container = $cont AND item_id < 1000000";
                    cmd.Parameters.AddWithValue("$cont", (int)Container.RetainerAggregate);
                }
                // else: full-table min/max is already set above.

                using var reader = cmd.ExecuteReader();
                if (reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1))
                {
                    var earliest = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
                    var latest   = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);
                    return (earliest, latest);
                }
            }
            catch (Exception ex)
            {
                LogDbDebug("GetDataTimeRange", ex);
            }
        }

        return null;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reverse-maps a (item_id, container, owner_id) triple to a legacy variable name.
    /// Returns null for unrecognised combinations (synthetic IDs handled via TrackedDataLegacyMap).
    /// </summary>
    private static string? ReverseMapToVariableName(uint itemId, Container container, ulong ownerId)
    {
        // TrackedDataType variables (synthetic item IDs ≥ 1_000_000)
        if (itemId >= 1_000_000)
        {
            // Reverse the TrackedDataLegacyMap: find enum name by (container, itemId).
            return ResourceCatalog.GetLegacyVariableName(itemId, container);
        }

        // Real item variables
        return container switch
        {
            Container.PlayerAggregate   => $"Item_{itemId}",
            Container.RetainerAggregate => $"ItemRetainer_{itemId}",
            Container.RetainerPage1 or
            Container.RetainerPage2 or
            Container.RetainerPage3 or
            Container.RetainerPage4 or
            Container.RetainerPage5 or
            Container.RetainerPage6 or
            Container.RetainerPage7     => $"ItemRetainerX_{ownerId}_{itemId}",
            _ => null,
        };
    }

}
