using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Clears all data for a specific character and variable.
    /// </summary>
    public bool ClearCharacterData(string variable, ulong characterId)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable = $v AND character_id = $c)";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM series WHERE variable = $v AND character_id = $c";
                cmd.ExecuteNonQuery();

                return true;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] ClearCharacterData failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Clears all data for a variable across all characters.
    /// </summary>
    public bool ClearAllData(string variable)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable = $v)";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM series WHERE variable = $v";
                cmd.ExecuteNonQuery();

                LogService.Info(LogCategory.Database, $"[KaleidoscopeDb] Cleared all data for variable '{variable}'");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] ClearAllData failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Clears all data from all tables to simulate a fresh install.
    /// </summary>
    public bool ClearAllTables()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                
                // Time-series data
                cmd.CommandText = "DELETE FROM points";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM series";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM character_names";
                cmd.ExecuteNonQuery();

                // Inventory data
                cmd.CommandText = "DELETE FROM inventory_items";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM inventory_cache";
                cmd.ExecuteNonQuery();

                // Price data
                cmd.CommandText = "DELETE FROM item_prices";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM price_history";
                cmd.ExecuteNonQuery();

                // Inventory value history
                cmd.CommandText = "DELETE FROM inventory_value_items";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM inventory_value_history";
                cmd.ExecuteNonQuery();

                // Sale records
                cmd.CommandText = "DELETE FROM sale_records";
                cmd.ExecuteNonQuery();

                LogService.Info(LogCategory.Database, "[KaleidoscopeDb] Cleared all data from all tables");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] ClearAllTables failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Gets points within a date range for a variable, optionally filtered by character.
    /// </summary>
    /// <param name="variable">The variable name (e.g., "Gil").</param>
    /// <param name="characterId">Character ID, or null for all characters.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>List of points with character ID, timestamp, and value.</returns>
    public List<(ulong characterId, DateTime timestamp, long value)> GetPointsInRange(
        string variable, ulong? characterId, DateTime start, DateTime end)
    {
        var result = new List<(ulong, DateTime, long)>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();

                if (characterId.HasValue && characterId.Value != 0)
                {
                    cmd.CommandText = @"SELECT s.character_id, p.timestamp, p.value FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable = $v AND s.character_id = $c
                          AND p.timestamp >= $start AND p.timestamp <= $end
                        ORDER BY p.timestamp DESC";
                    cmd.Parameters.AddWithValue("$c", (long)characterId.Value);
                }
                else
                {
                    cmd.CommandText = @"SELECT s.character_id, p.timestamp, p.value FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable = $v
                          AND p.timestamp >= $start AND p.timestamp <= $end
                        ORDER BY p.timestamp DESC";
                }

                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$start", start.Ticks);
                cmd.Parameters.AddWithValue("$end", end.Ticks);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cid = (ulong)reader.GetInt64(0);
                    var ticks = reader.GetInt64(1);
                    var value = reader.GetInt64(2);
                    result.Add((cid, new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetPointsInRange failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Counts points within a date range and estimates storage size.
    /// </summary>
    /// <param name="variable">The variable name.</param>
    /// <param name="characterId">Character ID, or null for all characters.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>Tuple of (count, estimated bytes).</returns>
    public (int count, long estimatedBytes) CountPointsInRange(
        string variable, ulong? characterId, DateTime start, DateTime end)
    {
        const int BytesPerPoint = 24; // series_id (8) + timestamp (8) + value (8)

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return (0, 0);

            try
            {
                using var cmd = conn.CreateCommand();

                if (characterId.HasValue && characterId.Value != 0)
                {
                    cmd.CommandText = @"SELECT COUNT(*) FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable = $v AND s.character_id = $c
                          AND p.timestamp >= $start AND p.timestamp <= $end";
                    cmd.Parameters.AddWithValue("$c", (long)characterId.Value);
                }
                else
                {
                    cmd.CommandText = @"SELECT COUNT(*) FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable = $v
                          AND p.timestamp >= $start AND p.timestamp <= $end";
                }

                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$start", start.Ticks);
                cmd.Parameters.AddWithValue("$end", end.Ticks);

                var count = Convert.ToInt32(cmd.ExecuteScalar());
                return (count, count * BytesPerPoint);
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] CountPointsInRange failed: {ex.Message}");
                return (0, 0);
            }
        }
    }

    /// <summary>
    /// Deletes points within a date range for a variable.
    /// </summary>
    /// <param name="variable">The variable name.</param>
    /// <param name="characterId">Character ID, or null for all characters.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>Number of points deleted.</returns>
    public int DeletePointsInRange(string variable, ulong? characterId, DateTime start, DateTime end)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();

                if (characterId.HasValue && characterId.Value != 0)
                {
                    cmd.CommandText = @"DELETE FROM points 
                        WHERE series_id IN (SELECT id FROM series WHERE variable = $v AND character_id = $c)
                          AND timestamp >= $start AND timestamp <= $end";
                    cmd.Parameters.AddWithValue("$c", (long)characterId.Value);
                }
                else
                {
                    cmd.CommandText = @"DELETE FROM points 
                        WHERE series_id IN (SELECT id FROM series WHERE variable = $v)
                          AND timestamp >= $start AND timestamp <= $end";
                }

                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$start", start.Ticks);
                cmd.Parameters.AddWithValue("$end", end.Ticks);

                var deleted = cmd.ExecuteNonQuery();
                LogService.Info(LogCategory.Database, $"[KaleidoscopeDb] Deleted {deleted} points for '{variable}' between {start:O} and {end:O}");
                return deleted;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] DeletePointsInRange failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Exports points within a date range to a CSV string.
    /// </summary>
    /// <param name="variable">The variable name.</param>
    /// <param name="characterId">Character ID, or null for all characters.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>CSV content as string.</returns>
    public string ExportPointsInRangeToCsv(string variable, ulong? characterId, DateTime start, DateTime end)
    {
        var sb = new StringBuilder();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return sb.ToString();

            try
            {
                using var cmd = conn.CreateCommand();

                if (characterId.HasValue && characterId.Value != 0)
                {
                    sb.AppendLine("timestamp_utc,value");
                    cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable = $v AND s.character_id = $c
                          AND p.timestamp >= $start AND p.timestamp <= $end
                        ORDER BY p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$c", (long)characterId.Value);
                }
                else
                {
                    sb.AppendLine("timestamp_utc,value,character_id");
                    cmd.CommandText = @"SELECT p.timestamp, p.value, s.character_id FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable = $v
                          AND p.timestamp >= $start AND p.timestamp <= $end
                        ORDER BY p.timestamp ASC";
                }

                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$start", start.Ticks);
                cmd.Parameters.AddWithValue("$end", end.Ticks);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ticks = reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    var ts = new DateTime(ticks, DateTimeKind.Utc);

                    if (characterId.HasValue && characterId.Value != 0)
                    {
                        sb.AppendLine($"{ts:O},{value}");
                    }
                    else
                    {
                        var cid = reader.GetInt64(2);
                        sb.AppendLine($"{ts:O},{value},{cid}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] ExportPointsInRangeToCsv failed: {ex.Message}", ex);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Runs VACUUM to reclaim disk space after deletions.
    /// Warning: This can be slow on large databases.
    /// </summary>
    /// <returns>True if successful.</returns>
    public bool Vacuum()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "VACUUM";
                cmd.ExecuteNonQuery();
                LogService.Info(LogCategory.Database, "[KaleidoscopeDb] VACUUM completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] VACUUM failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Removes data for characters that don't have a name association.
    /// Returns the number of characters removed.
    /// </summary>
    public int CleanUnassociatedCharacters(string variable)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                // Find character IDs with data but no name
                var idsToRemove = new List<long>();
                using (var selectCmd = _connection.CreateCommand())
                {
                    selectCmd.CommandText = @"SELECT DISTINCT character_id FROM series 
                        WHERE variable = $v 
                        AND character_id NOT IN (SELECT character_id FROM character_names)";
                    selectCmd.Parameters.AddWithValue("$v", variable);

                    using var reader = selectCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var cid = reader.GetInt64(0);
                        if (cid != 0) idsToRemove.Add(cid);
                    }
                }

                if (idsToRemove.Count == 0) return 0;

                using var tx = _connection.BeginTransaction();
                try
                {
                    foreach (var cid in idsToRemove)
                    {
                        using var cmd = _connection.CreateCommand();
                        cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable = $v AND character_id = $c)";
                        cmd.Parameters.AddWithValue("$v", variable);
                        cmd.Parameters.AddWithValue("$c", cid);
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "DELETE FROM series WHERE variable = $v AND character_id = $c";
                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                    LogService.Info(LogCategory.Database, $"[KaleidoscopeDb] Cleaned {idsToRemove.Count} unassociated characters");
                    return idsToRemove.Count;
                }
                catch (Exception ex)
                {
                    LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Transaction failed: {ex.Message}", ex);
                    try { tx.Rollback(); } 
                    catch (Exception rollbackEx) { LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Rollback also failed: {rollbackEx.Message}"); }
                    return 0;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] CleanUnassociatedCharacters failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Migrates stored names to clean format (removes "You (Name)" wrappers, etc.).
    /// </summary>
    public void MigrateStoredNames()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT character_id, name FROM character_names";
                var updates = new List<(long cid, string newName)>();
                var deletes = new List<long>();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var cid = reader.GetInt64(0);
                        var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        var sanitized = NameSanitizer.Sanitize(name);

                        // If the stored name contains any digit, treat it as invalid
                        if (!string.IsNullOrEmpty(sanitized) && sanitized.Any(char.IsDigit))
                        {
                            deletes.Add(cid);
                            continue;
                        }

                        // If the stored name sanitizes to just "You", treat it as a placeholder
                        if (!string.IsNullOrEmpty(sanitized) && string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
                        {
                            deletes.Add(cid);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(sanitized) && !string.Equals(sanitized, name, StringComparison.Ordinal))
                        {
                            updates.Add((cid, sanitized));
                        }
                    }
                }

                foreach (var (cid, newName) in updates)
                {
                    try
                    {
                        using var updateCmd = _connection.CreateCommand();
                        updateCmd.CommandText = "UPDATE character_names SET name = $n WHERE character_id = $c";
                        updateCmd.Parameters.AddWithValue("$n", newName);
                        updateCmd.Parameters.AddWithValue("$c", cid);
                        updateCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Name update failed for CID {cid}: {ex.Message}");
                    }
                }

                foreach (var cid in deletes)
                {
                    try
                    {
                        using var deleteCmd = _connection.CreateCommand();
                        deleteCmd.CommandText = "DELETE FROM character_names WHERE character_id = $c";
                        deleteCmd.Parameters.AddWithValue("$c", cid);
                        deleteCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Name delete failed for CID {cid}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] MigrateStoredNames failed: {ex.Message}");
            }
        }
    }

}
