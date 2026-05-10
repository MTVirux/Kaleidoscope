using System.Collections.Generic;
using Kaleidoscope.Models.Inventory;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{
    /// <summary>
    /// Read all inventory data from the new resources table, grouped into legacy
    /// InventoryCacheEntry shape. Used by InventoryCacheService when UseUnifiedResources
    /// is enabled. Excludes synthetic rows (Container ≥ 40000) — legacy entry shape
    /// doesn't carry them — except the SpecialPlayer/Gil row which becomes the Gil field.
    /// </summary>
    public List<InventoryCacheEntry> GetAllInventoryCachesFromResources()
    {
        var result = new Dictionary<(ulong OwnerId, OwnerKind Kind), InventoryCacheEntry>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return new List<InventoryCacheEntry>();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT owner_id, owner_kind, parent_owner_id, container, item_id, slot,
                           quantity, flags, spiritbond, collectability, condition, glamour_id, updated_at
                    FROM resources
                    WHERE container < 40000 OR (container = 90000 AND item_id = $gilId)
                    ORDER BY owner_kind, owner_id, container, slot";
                cmd.Parameters.AddWithValue("$gilId", (long)Kaleidoscope.Services.Resources.ResourceCatalog.GilItemId);
                ReadResourcesIntoEntries(cmd, result);
            }
            catch (Exception ex)
            {
                LogDbError("GetAllInventoryCachesFromResources", ex);
            }
        }

        return new List<InventoryCacheEntry>(result.Values);
    }

    /// <summary>
    /// Same as GetAllInventoryCachesFromResources but filtered to the given character
    /// and their retainers (via parent_owner_id).
    /// </summary>
    public List<InventoryCacheEntry> GetAllInventoryCachesFromResources(ulong characterId)
    {
        var result = new Dictionary<(ulong OwnerId, OwnerKind Kind), InventoryCacheEntry>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return new List<InventoryCacheEntry>();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT owner_id, owner_kind, parent_owner_id, container, item_id, slot,
                           quantity, flags, spiritbond, collectability, condition, glamour_id, updated_at
                    FROM resources
                    WHERE (container < 40000 OR (container = 90000 AND item_id = $gilId))
                      AND (owner_id = $cid OR parent_owner_id = $cid)
                    ORDER BY owner_kind, owner_id, container, slot";
                cmd.Parameters.AddWithValue("$gilId", (long)Kaleidoscope.Services.Resources.ResourceCatalog.GilItemId);
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                ReadResourcesIntoEntries(cmd, result);
            }
            catch (Exception ex)
            {
                LogDbError("GetAllInventoryCachesFromResources(characterId)", ex);
            }
        }

        return new List<InventoryCacheEntry>(result.Values);
    }

    /// <summary>
    /// Shared row-reader for both GetAllInventoryCachesFromResources overloads. Mutates
    /// the provided dictionary in place: groups rows into entries by (OwnerId, OwnerKind),
    /// promotes the SpecialPlayer/Gil row into the entry's Gil field, and converts
    /// non-synthetic rows into InventoryItemSnapshot entries.
    /// </summary>
    private static void ReadResourcesIntoEntries(
        Microsoft.Data.Sqlite.SqliteCommand cmd,
        Dictionary<(ulong OwnerId, OwnerKind Kind), InventoryCacheEntry> result)
    {
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var ownerId = (ulong)r.GetInt64(0);
            var ownerKind = (OwnerKind)r.GetInt32(1);
            var parentId = (ulong)r.GetInt64(2);
            var container = r.GetInt32(3);
            var itemId = (uint)r.GetInt64(4);
            var slot = (short)r.GetInt32(5);
            var qty = r.GetInt64(6);
            var flags = (ResourceFlags)r.GetInt32(7);
            var sb = (ushort)r.GetInt32(8);
            var col = (ushort)r.GetInt32(9);
            var cond = (ushort)r.GetInt32(10);
            var glam = (uint)r.GetInt64(11);
            var ts = new DateTime(r.GetInt64(12), DateTimeKind.Utc);

            var key = (ownerId, ownerKind);
            if (!result.TryGetValue(key, out var entry))
            {
                entry = new InventoryCacheEntry
                {
                    CharacterId = ownerKind == OwnerKind.Player ? ownerId : parentId,
                    RetainerId = ownerKind == OwnerKind.Retainer ? ownerId : 0,
                    SourceType = ownerKind == OwnerKind.Player ? InventorySourceType.Player : InventorySourceType.Retainer,
                    UpdatedAt = ts,
                };
                result[key] = entry;
            }

            if (container == 90000 && itemId == Kaleidoscope.Services.Resources.ResourceCatalog.GilItemId)
            {
                entry.Gil = qty;
                continue;
            }

            entry.Items.Add(new InventoryItemSnapshot
            {
                ItemId = itemId,
                Quantity = (int)qty,
                IsHq = (flags & ResourceFlags.HQ) != 0,
                IsCollectable = (flags & ResourceFlags.Collectable) != 0,
                Slot = slot,
                ContainerType = (uint)container,
                SpiritbondOrCollectability = (flags & ResourceFlags.Collectable) != 0 ? col : sb,
                Condition = cond,
                GlamourId = glam,
            });
        }
    }

    /// <summary>
    /// Sum quantity for an item across all retainers belonging to a character.
    /// Mirrors legacy GetRetainerItemCount.
    /// </summary>
    public long GetTotalRetainerItemFromResources(ulong characterId, uint itemId)
    {
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return 0;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(quantity), 0)
                    FROM resources
                    WHERE owner_kind = 1 AND parent_owner_id = $cid AND item_id = $iid";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.Parameters.AddWithValue("$iid", (long)itemId);
                var v = cmd.ExecuteScalar();
                return v != null && v != DBNull.Value ? (long)v : 0;
            }
            catch (Exception ex)
            {
                LogDbError("GetTotalRetainerItemFromResources", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Per-item totals across all owners (or filtered to one character/item).
    /// Mirrors legacy GetItemCountSummary.
    /// </summary>
    public Dictionary<uint, long> GetItemCountSummaryFromResources(ulong? characterId = null, uint? itemId = null)
    {
        var result = new Dictionary<uint, long>();
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;
            try
            {
                using var cmd = conn.CreateCommand();
                var sql = "SELECT item_id, SUM(quantity) FROM resources WHERE container < 40000";
                if (characterId.HasValue)
                {
                    sql += " AND (owner_id = $cid OR parent_owner_id = $cid)";
                    cmd.Parameters.AddWithValue("$cid", (long)characterId.Value);
                }
                if (itemId.HasValue)
                {
                    sql += " AND item_id = $iid";
                    cmd.Parameters.AddWithValue("$iid", (long)itemId.Value);
                }
                sql += " GROUP BY item_id";
                cmd.CommandText = sql;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    result[(uint)r.GetInt64(0)] = r.GetInt64(1);
            }
            catch (Exception ex)
            {
                LogDbError("GetItemCountSummaryFromResources", ex);
            }
        }
        return result;
    }

    /// <summary>
    /// All historical points for a given (item, owner, container) tuple, optionally filtered
    /// to points after a timestamp. Returns (timestamp, quantity) tuples ordered by timestamp.
    /// Used by TimeSeriesCacheService when UseUnifiedResources is enabled.
    /// </summary>
    public List<(long Timestamp, long Value)> GetHistoryPoints(uint itemId, ulong ownerId, int container, DateTime? since = null)
    {
        var result = new List<(long, long)>();
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;
            try
            {
                using var cmd = conn.CreateCommand();
                var sql = @"
                    SELECT timestamp, quantity FROM resource_history
                    WHERE item_id = $iid AND owner_id = $oid AND container = $cont";
                cmd.Parameters.AddWithValue("$iid", (long)itemId);
                cmd.Parameters.AddWithValue("$oid", (long)ownerId);
                cmd.Parameters.AddWithValue("$cont", container);
                if (since.HasValue)
                {
                    sql += " AND timestamp >= $since";
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }
                sql += " ORDER BY timestamp";
                cmd.CommandText = sql;
                using var r = cmd.ExecuteReader();
                while (r.Read()) result.Add((r.GetInt64(0), r.GetInt64(1)));
            }
            catch (Exception ex)
            {
                LogDbError("GetHistoryPoints", ex);
            }
        }
        return result;
    }

    /// <summary>
    /// Most recent quantity for a (item, owner, container) tuple, or null if no data.
    /// Used by callers that only care about the current value.
    /// </summary>
    public long? GetLatestHistoryValue(uint itemId, ulong ownerId, int container)
    {
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return null;
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT quantity FROM resource_history
                    WHERE item_id = $iid AND owner_id = $oid AND container = $cont
                    ORDER BY timestamp DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$iid", (long)itemId);
                cmd.Parameters.AddWithValue("$oid", (long)ownerId);
                cmd.Parameters.AddWithValue("$cont", container);
                var v = cmd.ExecuteScalar();
                return v != null && v != DBNull.Value ? (long?)(long)v : null;
            }
            catch (Exception ex)
            {
                LogDbError("GetLatestHistoryValue", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// One-shot cleanup: the Phase 1 migration backfilled retainer gil into Container.SpecialPlayer
    /// (synthetic = 90000) rows owned by retainers. The new live pipeline writes retainer gil under
    /// Container.RetainerGil (12000). Without this purge, both rows coexist and aggregate-by-owner-
    /// kind double-counts the retainer gil.
    /// </summary>
    public int PurgeStaleRetainerGilRows()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"DELETE FROM resources WHERE owner_kind = 1 AND container = 90000 AND item_id = $gilId";
                cmd.Parameters.AddWithValue("$gilId", (long)Resources.ResourceCatalog.GilItemId);
                return cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogDbError("PurgeStaleRetainerGilRows", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Read every row from the resources table and apply it to the given ResourceStore.
    /// Used at startup to pre-populate the in-memory store with offline character data
    /// (since the new capture pipeline only fires for the active character + open retainers).
    /// </summary>
    public int LoadAllResourcesInto(Kaleidoscope.Services.Resources.ResourceStore store)
    {
        var loaded = 0;
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return 0;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT owner_id, owner_kind, container, item_id, slot,
                           quantity, flags, spiritbond, collectability, condition, glamour_id, updated_at
                    FROM resources";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var resource = new Kaleidoscope.Models.Resources.Resource
                    {
                        Key = new Kaleidoscope.Models.Resources.ResourceKey
                        {
                            OwnerId   = (ulong)r.GetInt64(0),
                            OwnerKind = (Kaleidoscope.Models.Resources.OwnerKind)r.GetInt32(1),
                            Container = (Kaleidoscope.Models.Resources.Container)r.GetInt32(2),
                            ItemId    = (uint)r.GetInt64(3),
                            Slot      = (short)r.GetInt32(4),
                        },
                        Quantity = r.GetInt64(5),
                        Flags = (Kaleidoscope.Models.Resources.ResourceFlags)r.GetInt32(6),
                        Spiritbond = (ushort)r.GetInt32(7),
                        Collectability = (ushort)r.GetInt32(8),
                        Condition = (ushort)r.GetInt32(9),
                        GlamourId = (uint)r.GetInt64(10),
                        UpdatedAt = new DateTime(r.GetInt64(11), DateTimeKind.Utc),
                    };
                    store.ApplyWithAggregate(resource);
                    loaded++;
                }
            }
            catch (Exception ex)
            {
                LogDbError("LoadAllResourcesInto", ex);
            }
        }
        return loaded;
    }

    /// <summary>
    /// Returns all (variable, character_id) pairs from the legacy <c>series</c> table
    /// whose variable name starts with <paramref name="prefix"/> and, when
    /// <paramref name="suffix"/> is non-null/empty, also ends with that suffix.
    /// Used by <see cref="TimeSeriesCacheService"/> to discover which characters have
    /// history for a given variable pattern when routing through resource_history.
    /// </summary>
    public List<(string Variable, ulong CharacterId)> GetSeriesByVariablePrefixSuffix(string prefix, string? suffix)
    {
        var result = new List<(string, ulong)>();
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;
            try
            {
                using var cmd = conn.CreateCommand();
                var sql = "SELECT variable, character_id FROM series WHERE variable LIKE $prefix || '%'";
                cmd.Parameters.AddWithValue("$prefix", prefix);
                if (!string.IsNullOrEmpty(suffix))
                {
                    sql += " AND variable LIKE '%' || $suffix";
                    cmd.Parameters.AddWithValue("$suffix", suffix);
                }
                cmd.CommandText = sql;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    result.Add((r.GetString(0), (ulong)r.GetInt64(1)));
            }
            catch (Exception ex)
            {
                LogDbError("GetSeriesByVariablePrefixSuffix", ex);
            }
        }
        return result;
    }
}
