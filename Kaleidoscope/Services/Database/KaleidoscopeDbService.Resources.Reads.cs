using System.Collections.Generic;
using Kaleidoscope.Models.Inventory;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{
    /// <summary>
    /// Insert or update the human-readable name for an owner (player character or retainer).
    /// Called from capture sources when they observe an owner whose name is known.
    /// </summary>
    public void UpsertOwnerName(ulong ownerId, Kaleidoscope.Models.Resources.OwnerKind ownerKind, string name, string? world = null)
    {
        if (ownerId == 0 || string.IsNullOrEmpty(name)) return;
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO owner_names (owner_id, owner_kind, name, world, updated_at)
                    VALUES ($oid, $okind, $n, $w, $ts)
                    ON CONFLICT(owner_id, owner_kind) DO UPDATE SET
                        name       = excluded.name,
                        world      = COALESCE(excluded.world, owner_names.world),
                        updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("$oid", (long)ownerId);
                cmd.Parameters.AddWithValue("$okind", (int)ownerKind);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$w", (object?)world ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.Ticks);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogDbError("UpsertOwnerName", ex);
            }
        }
    }

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
                    SELECT r.owner_id, r.owner_kind, r.parent_owner_id, r.container, r.item_id, r.slot,
                           r.quantity, r.flags, r.spiritbond, r.collectability, r.condition, r.glamour_id, r.updated_at,
                           n.name, n.world
                    FROM resources r
                    LEFT JOIN owner_names n ON n.owner_id = r.owner_id AND n.owner_kind = r.owner_kind
                    WHERE r.container < 40000 OR (r.container = 90000 AND r.item_id = $gilId)
                    ORDER BY r.owner_kind, r.owner_id, r.container, r.slot";
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
                    SELECT r.owner_id, r.owner_kind, r.parent_owner_id, r.container, r.item_id, r.slot,
                           r.quantity, r.flags, r.spiritbond, r.collectability, r.condition, r.glamour_id, r.updated_at,
                           n.name, n.world
                    FROM resources r
                    LEFT JOIN owner_names n ON n.owner_id = r.owner_id AND n.owner_kind = r.owner_kind
                    WHERE (r.container < 40000 OR (r.container = 90000 AND r.item_id = $gilId))
                      AND (r.owner_id = $cid OR r.parent_owner_id = $cid)
                    ORDER BY r.owner_kind, r.owner_id, r.container, r.slot";
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
                    Name = r.IsDBNull(13) ? null : r.GetString(13),
                    World = r.IsDBNull(14) ? null : r.GetString(14),
                };
                result[key] = entry;
            }

            // Gil rows: player gil lives in Container.SpecialPlayer (90000); retainer gil lives in
            // Container.RetainerGil (12000). Both populate the entry's Gil field, not Items.
            if ((container == 90000 || container == 12000) && itemId == Kaleidoscope.Services.Resources.ResourceCatalog.GilItemId)
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
    /// Per-character sum of their retainers' gil. Used by the Retainer Gil currency column
    /// in the data table. Requires parent_owner_id to be correctly populated on retainer rows
    /// (Phase 3.5 fix ensures this for live captures; AR seed paths must also set it).
    /// </summary>
    public Dictionary<ulong, long> GetRetainerGilPerCharacter()
    {
        var result = new Dictionary<ulong, long>();
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT parent_owner_id, COALESCE(SUM(quantity), 0)
                    FROM resources
                    WHERE owner_kind = 1
                      AND container = 12000
                      AND item_id = $gilId
                      AND parent_owner_id != 0
                    GROUP BY parent_owner_id";
                cmd.Parameters.AddWithValue("$gilId", (long)Resources.ResourceCatalog.GilItemId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result[(ulong)r.GetInt64(0)] = r.GetInt64(1);
                }
            }
            catch (Exception ex)
            {
                LogDbError("GetRetainerGilPerCharacter", ex);
            }
        }
        return result;
    }

    /// <summary>
    /// Per-character sum of an item across the player's containers AND their retainers'
    /// containers. Used for item-bearing TrackedDataTypes like Ventures and Crystals where
    /// the value spans multiple owners + multiple item IDs.
    /// </summary>
    public Dictionary<ulong, long> GetItemSumPerCharacterIncludingRetainers(IEnumerable<uint> itemIds)
    {
        var ids = itemIds.Select(id => (long)id).ToList();
        if (ids.Count == 0) return new Dictionary<ulong, long>();

        var result = new Dictionary<ulong, long>();
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;
            try
            {
                // Build IN clause
                var inClause = string.Join(",", ids);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT
                        CASE owner_kind WHEN 0 THEN owner_id ELSE parent_owner_id END AS character_id,
                        COALESCE(SUM(quantity), 0) AS total
                    FROM resources
                    WHERE item_id IN ({inClause})
                      AND (owner_kind = 0 OR (owner_kind = 1 AND parent_owner_id != 0))
                    GROUP BY character_id
                    HAVING character_id != 0";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result[(ulong)r.GetInt64(0)] = r.GetInt64(1);
                }
            }
            catch (Exception ex)
            {
                LogDbError("GetItemSumPerCharacterIncludingRetainers", ex);
            }
        }
        return result;
    }

    /// <summary>
    /// Per-character latest value for a single (item, container) — scoped to OwnerKind.Player.
    /// Used for player-only currencies (Gil, MGP, Wolf Marks, Allied Seals, all Currency-container
    /// items like Tomestones/Scrips/GC Seals/etc.).
    /// </summary>
    public Dictionary<ulong, long> GetItemSumPerCharacterPlayerOnly(uint itemId, int container)
    {
        var result = new Dictionary<ulong, long>();
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT owner_id, COALESCE(SUM(quantity), 0)
                    FROM resources
                    WHERE owner_kind = 0 AND item_id = $iid AND container = $cont
                    GROUP BY owner_id";
                cmd.Parameters.AddWithValue("$iid", (long)itemId);
                cmd.Parameters.AddWithValue("$cont", container);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result[(ulong)r.GetInt64(0)] = r.GetInt64(1);
                }
            }
            catch (Exception ex)
            {
                LogDbError("GetItemSumPerCharacterPlayerOnly", ex);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns all (variable, owner_id) pairs from resource_history whose reverse-mapped legacy
    /// variable name starts with <paramref name="prefix"/> and, when <paramref name="suffix"/>
    /// is non-null/non-empty, also ends with that suffix.
    /// Used by <see cref="TimeSeriesCacheService"/> to discover which owners have history for a
    /// given variable pattern.  After Phase 3 the legacy <c>series</c> table no longer exists.
    /// </summary>
    public List<(string Variable, ulong CharacterId)> GetSeriesByVariablePrefixSuffix(string prefix, string? suffix)
    {
        var result = new List<(string, ulong)>();
        var seen   = new HashSet<(string, ulong)>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT DISTINCT item_id, owner_id, container FROM resource_history
                    WHERE owner_id != 0";
                cmd.CommandText = cmd.CommandText; // force assignment (no-op; keeps pattern)
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var itemId  = (uint)r.GetInt64(0);
                    var ownerId = (ulong)r.GetInt64(1);
                    var cont    = (Kaleidoscope.Models.Resources.Container)r.GetInt32(2);

                    if (ownerId == 0) continue;

                    string? varName = cont switch
                    {
                        Kaleidoscope.Models.Resources.Container.PlayerAggregate   => $"Item_{itemId}",
                        Kaleidoscope.Models.Resources.Container.RetainerAggregate => $"ItemRetainer_{itemId}",
                        Kaleidoscope.Models.Resources.Container.RetainerPage1 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage2 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage3 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage4 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage5 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage6 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage7     => $"ItemRetainerX_{ownerId}_{itemId}",
                        _ => itemId >= 1_000_000
                            ? Kaleidoscope.Services.Resources.ResourceCatalog.GetLegacyVariableName(itemId, cont)
                            : null,
                    };

                    if (varName == null) continue;
                    if (!varName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (!string.IsNullOrEmpty(suffix) && !varName.EndsWith(suffix, StringComparison.Ordinal)) continue;

                    if (seen.Add((varName, ownerId)))
                        result.Add((varName, ownerId));
                }
            }
            catch (Exception ex)
            {
                LogDbError("GetSeriesByVariablePrefixSuffix", ex);
            }
        }
        return result;
    }
}
