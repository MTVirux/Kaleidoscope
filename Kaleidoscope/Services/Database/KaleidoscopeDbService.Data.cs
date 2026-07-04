using Kaleidoscope.Services.Resources.Adapters;
using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{

    public bool ClearCharacterData(string variable, ulong characterId)
    {
        var mapping = LegacyVariableTranslator.Translate(variable, characterId);
        if (mapping == null)
        {
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] ClearCharacterData: unknown variable '{variable}', nothing to clear");
            return false;
        }

        return ExecuteWrite("ClearCharacterData", false, conn =>
        {
            return RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"DELETE FROM resource_history
                        WHERE item_id = $iid AND container = $cont AND owner_id = $oid";
                cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
                cmd.ExecuteNonQuery();
                return true;
            });
        });
    }

    public bool ClearAllData(string variable)
    {
        // characterId=0 is intentional — ParseLegacyVariableName only needs it for
        // character-scoped retainer variables; for global clears the owner is irrelevant
        // because we delete by (item_id, container) without an owner filter.
        var mapping = LegacyVariableTranslator.Translate(variable, 0);
        if (mapping == null)
        {
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] ClearAllData: unknown variable '{variable}', nothing to clear");
            return false;
        }

        return ExecuteWrite("ClearAllData", false, conn =>
        {
            return RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM resource_history WHERE item_id = $iid AND container = $cont";
                cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
                cmd.ExecuteNonQuery();

                LogService.Info(LogCategory.Database, $"[KaleidoscopeDb] Cleared all data for variable '{variable}'");
                return true;
            });
        });
    }

    /// <summary>
    /// Clears all data from all tables to simulate a fresh install.
    /// </summary>
    public bool ClearAllTables()
    {
        return ExecuteWrite("ClearAllTables", false, conn =>
        {
            return RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                // Unified resources tables (Phase 1+)
                cmd.CommandText = "DELETE FROM resource_history";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM resources";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM owner_names";
                cmd.ExecuteNonQuery();

                // Character/identity registry
                cmd.CommandText = "DELETE FROM character_names";
                cmd.ExecuteNonQuery();

                // Price tracking
                cmd.CommandText = "DELETE FROM item_prices";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM price_history";
                cmd.ExecuteNonQuery();

                // Inventory value history
                cmd.CommandText = "DELETE FROM inventory_value_items";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM inventory_value_history";
                cmd.ExecuteNonQuery();

                // Sales tracking
                cmd.CommandText = "DELETE FROM sale_records";
                cmd.ExecuteNonQuery();

                LogService.Info(LogCategory.Database, "[KaleidoscopeDb] Cleared all data from all tables");
                return true;
            });
        });
    }

    /// <summary>
    /// Gets history rows within a date range for a variable, optionally filtered by character.
    /// After Phase 3 this queries resource_history rather than the dropped points/series tables.
    /// </summary>
    /// <param name="variable">The legacy variable name (e.g., "Gil", "Item_12345").</param>
    /// <param name="characterId">Character ID, or null for all characters.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>List of rows with owner ID, timestamp, and quantity.</returns>
    public List<(ulong characterId, DateTime timestamp, long value)> GetPointsInRange(
        string variable, ulong? characterId, DateTime start, DateTime end)
    {
        var result = new List<(ulong, DateTime, long)>();

        var resolvedOwnerId = characterId.HasValue && characterId.Value != 0 ? characterId.Value : (ulong)0;
        var mapping = LegacyVariableTranslator.Translate(variable, resolvedOwnerId);
        if (mapping == null) return result;

        return ExecuteRead("GetPointsInRange", result, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
            cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
            cmd.Parameters.AddWithValue("$start", start.Ticks);
            cmd.Parameters.AddWithValue("$end", end.Ticks);

            if (characterId.HasValue && characterId.Value != 0)
            {
                cmd.CommandText = @"SELECT owner_id, timestamp, quantity FROM resource_history
                        WHERE item_id = $iid AND container = $cont AND owner_id = $oid
                          AND timestamp >= $start AND timestamp <= $end
                        ORDER BY timestamp DESC";
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
            }
            else
            {
                cmd.CommandText = @"SELECT owner_id, timestamp, quantity FROM resource_history
                        WHERE item_id = $iid AND container = $cont
                          AND timestamp >= $start AND timestamp <= $end
                        ORDER BY timestamp DESC";
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var cid = (ulong)reader.GetInt64(0);
                var ticks = reader.GetInt64(1);
                var value = reader.GetInt64(2);
                result.Add((cid, new DateTime(ticks, DateTimeKind.Utc), value));
            }

            return result;
        }, debugLog: true);
    }

    /// <summary>
    /// Counts history rows within a date range and estimates storage size.
    /// After Phase 3 this queries resource_history rather than the dropped points/series tables.
    /// </summary>
    /// <param name="variable">The legacy variable name.</param>
    /// <param name="characterId">Character ID, or null for all characters.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>Tuple of (count, estimated bytes).</returns>
    public (int count, long estimatedBytes) CountPointsInRange(
        string variable, ulong? characterId, DateTime start, DateTime end)
    {
        const int BytesPerRow = 32; // item_id(8)+owner_id(8)+container(4)+timestamp(8)+quantity(8)

        var resolvedOwnerId = characterId.HasValue && characterId.Value != 0 ? characterId.Value : (ulong)0;
        var mapping = LegacyVariableTranslator.Translate(variable, resolvedOwnerId);
        if (mapping == null) return (0, 0);

        return ExecuteRead<(int count, long estimatedBytes)>("CountPointsInRange", (0, 0), conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
            cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
            cmd.Parameters.AddWithValue("$start", start.Ticks);
            cmd.Parameters.AddWithValue("$end", end.Ticks);

            if (characterId.HasValue && characterId.Value != 0)
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM resource_history
                        WHERE item_id = $iid AND container = $cont AND owner_id = $oid
                          AND timestamp >= $start AND timestamp <= $end";
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
            }
            else
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM resource_history
                        WHERE item_id = $iid AND container = $cont
                          AND timestamp >= $start AND timestamp <= $end";
            }

            var count = Convert.ToInt32(cmd.ExecuteScalar());
            return (count, count * BytesPerRow);
        }, debugLog: true);
    }

    /// <summary>
    /// Deletes history rows within a date range for a variable.
    /// After Phase 3 this deletes from resource_history rather than the dropped points/series tables.
    /// </summary>
    /// <param name="variable">The legacy variable name.</param>
    /// <param name="characterId">Character ID, or null for all characters.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>Number of rows deleted.</returns>
    public int DeletePointsInRange(string variable, ulong? characterId, DateTime start, DateTime end)
    {
        var resolvedOwnerId = characterId.HasValue && characterId.Value != 0 ? characterId.Value : (ulong)0;
        var mapping = LegacyVariableTranslator.Translate(variable, resolvedOwnerId);
        if (mapping == null) return 0;

        return ExecuteWrite("DeletePointsInRange", 0, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
            cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
            cmd.Parameters.AddWithValue("$start", start.Ticks);
            cmd.Parameters.AddWithValue("$end", end.Ticks);

            if (characterId.HasValue && characterId.Value != 0)
            {
                cmd.CommandText = @"DELETE FROM resource_history
                        WHERE item_id = $iid AND container = $cont AND owner_id = $oid
                          AND timestamp >= $start AND timestamp <= $end";
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
            }
            else
            {
                cmd.CommandText = @"DELETE FROM resource_history
                        WHERE item_id = $iid AND container = $cont
                          AND timestamp >= $start AND timestamp <= $end";
            }

            var deleted = cmd.ExecuteNonQuery();
            LogService.Info(LogCategory.Database, $"[KaleidoscopeDb] Deleted {deleted} history rows for '{variable}' between {start:O} and {end:O}");
            return deleted;
        });
    }

    /// <summary>
    /// Exports history rows within a date range to a CSV string.
    /// After Phase 3 this queries resource_history rather than the dropped points/series tables.
    /// </summary>
    /// <param name="variable">The legacy variable name.</param>
    /// <param name="characterId">Character ID, or null for all characters.</param>
    /// <param name="start">Start of range (inclusive).</param>
    /// <param name="end">End of range (inclusive).</param>
    /// <returns>CSV content as string.</returns>
    public string ExportPointsInRangeToCsv(string variable, ulong? characterId, DateTime start, DateTime end)
    {
        var sb = new StringBuilder();

        var resolvedOwnerId = characterId.HasValue && characterId.Value != 0 ? characterId.Value : (ulong)0;
        var mapping = LegacyVariableTranslator.Translate(variable, resolvedOwnerId);
        if (mapping == null) return sb.ToString();

        return ExecuteRead("ExportPointsInRangeToCsv", sb.ToString(), conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
            cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
            cmd.Parameters.AddWithValue("$start", start.Ticks);
            cmd.Parameters.AddWithValue("$end", end.Ticks);

            if (characterId.HasValue && characterId.Value != 0)
            {
                sb.AppendLine("timestamp_utc,quantity");
                cmd.CommandText = @"SELECT timestamp, quantity FROM resource_history
                        WHERE item_id = $iid AND container = $cont AND owner_id = $oid
                          AND timestamp >= $start AND timestamp <= $end
                        ORDER BY timestamp ASC";
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);
            }
            else
            {
                sb.AppendLine("timestamp_utc,quantity,owner_id");
                cmd.CommandText = @"SELECT timestamp, quantity, owner_id FROM resource_history
                        WHERE item_id = $iid AND container = $cont
                          AND timestamp >= $start AND timestamp <= $end
                        ORDER BY timestamp ASC";
            }

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

            return sb.ToString();
        });
    }

    /// <summary>
    /// Runs VACUUM to reclaim disk space after deletions.
    /// Warning: This can be slow on large databases.
    /// </summary>
    /// <returns>True if successful.</returns>
    public bool Vacuum()
    {
        return ExecuteWrite("VACUUM", false, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM";
            cmd.ExecuteNonQuery();
            LogService.Info(LogCategory.Database, "[KaleidoscopeDb] VACUUM completed successfully");
            return true;
        });
    }

    /// <summary>
    /// Removes resource_history rows for owners that have no entry in character_names
    /// and no entry in owner_names (i.e., completely unrecognised characters).
    /// The <paramref name="variable"/> parameter is retained for API compatibility but
    /// the cleanup is now owner-scoped rather than variable-scoped.
    /// Returns the number of distinct owners whose data was removed.
    /// </summary>
    public int CleanUnassociatedCharacters(string variable)
    {
        var mapping = LegacyVariableTranslator.Translate(variable, 0);
        if (mapping == null) return 0;

        return ExecuteWrite("CleanUnassociatedCharacters", 0, conn =>
        {
            // Find owner_ids that appear in resource_history for this (item, container) but
            // have no matching entry in either character_names or owner_names.
            var idsToRemove = new List<long>();
            using (var selectCmd = conn.CreateCommand())
            {
                selectCmd.CommandText = @"
                        SELECT DISTINCT rh.owner_id FROM resource_history rh
                        WHERE rh.item_id = $iid AND rh.container = $cont
                          AND rh.owner_id != 0
                          AND rh.owner_id NOT IN (SELECT character_id FROM character_names)
                          AND rh.owner_id NOT IN (SELECT owner_id FROM owner_names)";
                selectCmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                selectCmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);

                using var reader = selectCmd.ExecuteReader();
                while (reader.Read())
                {
                    var oid = reader.GetInt64(0);
                    if (oid != 0) idsToRemove.Add(oid);
                }
            }

            if (idsToRemove.Count == 0) return 0;

            RunInTransaction(tx =>
            {
                foreach (var oid in idsToRemove)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"DELETE FROM resource_history
                            WHERE item_id = $iid AND container = $cont AND owner_id = $oid";
                    cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
                    cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);
                    cmd.Parameters.AddWithValue("$oid", oid);
                    cmd.ExecuteNonQuery();
                }
            });

            LogService.Info(LogCategory.Database, $"[KaleidoscopeDb] Cleaned {idsToRemove.Count} unassociated character/owner entries for '{variable}'");
            return idsToRemove.Count;
        });
    }

    /// <summary>
    /// Migrates stored names to clean format (removes "You (Name)" wrappers, etc.).
    /// </summary>
    public void MigrateStoredNames()
    {
        ExecuteWrite("MigrateStoredNames", conn =>
        {
            using var cmd = conn.CreateCommand();
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

            if (updates.Count == 0 && deletes.Count == 0) return;

            RunInTransaction(migrationTx =>
            {
                foreach (var (cid, newName) in updates)
                {
                    try
                    {
                        using var updateCmd = conn.CreateCommand();
                        updateCmd.Transaction = migrationTx;
                        updateCmd.CommandText = "UPDATE character_names SET name = $n WHERE character_id = $c";
                        updateCmd.Parameters.AddWithValue("$n", newName);
                        updateCmd.Parameters.AddWithValue("$c", cid);
                        updateCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        NotifyIfCorruption(ex);
                        LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Name update failed for CID {cid}: {ex.Message}");
                    }
                }

                foreach (var cid in deletes)
                {
                    try
                    {
                        using var deleteCmd = conn.CreateCommand();
                        deleteCmd.Transaction = migrationTx;
                        deleteCmd.CommandText = "DELETE FROM character_names WHERE character_id = $c";
                        deleteCmd.Parameters.AddWithValue("$c", cid);
                        deleteCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        NotifyIfCorruption(ex);
                        LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Name delete failed for CID {cid}: {ex.Message}");
                    }
                }
            });
        }, debugLog: true);
    }

}
