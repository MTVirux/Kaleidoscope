using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Gets total crystal count for a character across all retainer inventories.
    /// Uses the inventory cache system instead of the legacy retainer_crystals table.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="element">Crystal element (0=Fire, 1=Ice, 2=Wind, 3=Earth, 4=Lightning, 5=Water).</param>
    /// <param name="tier">Crystal tier (0=Shard, 1=Crystal, 2=Cluster).</param>
    /// <returns>Total quantity across all retainers.</returns>
    public long GetTotalRetainerCrystals(ulong characterId, int element, int tier)
    {
        // Crystal item IDs: Shard = 2 + element, Crystal = 8 + element, Cluster = 14 + element
        uint itemId = (uint)(2 + element + tier * 6);
        return GetRetainerItemCount(characterId, itemId);
    }

    /// <summary>
    /// Gets total count of a specific item across all retainer inventories for a character.
    /// </summary>
    public long GetRetainerItemCount(ulong characterId, uint itemId)
    {
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return 0;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(ii.quantity), 0)
                    FROM inventory_items ii
                    JOIN inventory_cache ic ON ii.cache_id = ic.id
                    WHERE ic.character_id = $cid 
                      AND ic.source_type = 1
                      AND ii.item_id = $iid";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.Parameters.AddWithValue("$iid", (long)itemId);

                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? (long)result : 0;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetRetainerItemCount failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Saves or updates an inventory cache entry and its items.
    /// Replaces all existing items for this cache.
    /// </summary>
    public void SaveInventoryCache(Models.Inventory.InventoryCacheEntry entry)
    {
        if (entry == null) return;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var transaction = _connection.BeginTransaction();

                try
                {
                    using var cacheCmd = _connection.CreateCommand();
                    cacheCmd.Transaction = transaction;
                    cacheCmd.CommandText = @"
                        INSERT INTO inventory_cache (character_id, source_type, retainer_id, name, world, gil, updated_at)
                        VALUES ($cid, $type, $rid, $name, $world, $gil, $time)
                        ON CONFLICT(character_id, source_type, retainer_id) DO UPDATE SET
                            name = excluded.name,
                            world = excluded.world,
                            gil = excluded.gil,
                            updated_at = excluded.updated_at
                        RETURNING id";
                    cacheCmd.Parameters.AddWithValue("$cid", (long)entry.CharacterId);
                    cacheCmd.Parameters.AddWithValue("$type", (int)entry.SourceType);
                    cacheCmd.Parameters.AddWithValue("$rid", (long)entry.RetainerId);
                    cacheCmd.Parameters.AddWithValue("$name", entry.Name ?? (object)DBNull.Value);
                    cacheCmd.Parameters.AddWithValue("$world", entry.World ?? (object)DBNull.Value);
                    cacheCmd.Parameters.AddWithValue("$gil", entry.Gil);
                    cacheCmd.Parameters.AddWithValue("$time", entry.UpdatedAt.Ticks);

                    var cacheId = (long)cacheCmd.ExecuteScalar()!;

                    using var deleteCmd = _connection.CreateCommand();
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM inventory_items WHERE cache_id = $id";
                    deleteCmd.Parameters.AddWithValue("$id", cacheId);
                    deleteCmd.ExecuteNonQuery();

                    if (entry.Items.Count > 0)
                    {
                        using var itemCmd = _connection.CreateCommand();
                        itemCmd.Transaction = transaction;
                        itemCmd.CommandText = @"
                            INSERT INTO inventory_items 
                            (cache_id, item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id)
                            VALUES ($cid, $iid, $qty, $hq, $col, $slot, $cont, $sb, $cond, $glam)";

                        var cidParam = itemCmd.Parameters.Add("$cid", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var iidParam = itemCmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var qtyParam = itemCmd.Parameters.Add("$qty", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var hqParam = itemCmd.Parameters.Add("$hq", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var colParam = itemCmd.Parameters.Add("$col", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var slotParam = itemCmd.Parameters.Add("$slot", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var contParam = itemCmd.Parameters.Add("$cont", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var sbParam = itemCmd.Parameters.Add("$sb", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var condParam = itemCmd.Parameters.Add("$cond", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var glamParam = itemCmd.Parameters.Add("$glam", Microsoft.Data.Sqlite.SqliteType.Integer);

                        cidParam.Value = cacheId;

                        foreach (var item in entry.Items)
                        {
                            iidParam.Value = (long)item.ItemId;
                            qtyParam.Value = item.Quantity;
                            hqParam.Value = item.IsHq ? 1 : 0;
                            colParam.Value = item.IsCollectable ? 1 : 0;
                            slotParam.Value = item.Slot;
                            contParam.Value = (long)item.ContainerType;
                            sbParam.Value = item.SpiritbondOrCollectability;
                            condParam.Value = item.Condition;
                            glamParam.Value = (long)item.GlamourId;
                            itemCmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                    LogService.Verbose(LogCategory.Database, $"[KaleidoscopeDb] Saved inventory cache for {entry.SourceType} {entry.Name}: {entry.Items.Count} items");
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] SaveInventoryCache failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets a specific inventory cache entry by character ID and source.
    /// For players, use retainerId = 0.
    /// </summary>
    public Models.Inventory.InventoryCacheEntry? GetInventoryCache(
        ulong characterId, 
        Models.Inventory.InventorySourceType sourceType, 
        ulong retainerId = 0)
    {
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return null;

            try
            {
                using var cacheCmd = conn.CreateCommand();
                cacheCmd.CommandText = @"
                    SELECT id, name, world, gil, updated_at 
                    FROM inventory_cache 
                    WHERE character_id = $cid AND source_type = $type AND retainer_id = $rid";
                cacheCmd.Parameters.AddWithValue("$cid", (long)characterId);
                cacheCmd.Parameters.AddWithValue("$type", (int)sourceType);
                cacheCmd.Parameters.AddWithValue("$rid", (long)retainerId);

                using var reader = cacheCmd.ExecuteReader();
                if (!reader.Read()) return null;

                var cacheId = reader.GetInt64(0);
                var entry = new Models.Inventory.InventoryCacheEntry
                {
                    CharacterId = characterId,
                    SourceType = sourceType,
                    RetainerId = retainerId,
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    World = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Gil = reader.GetInt64(3),
                    UpdatedAt = new DateTime(reader.GetInt64(4), DateTimeKind.Utc)
                };
                reader.Close();

                using var itemCmd = conn.CreateCommand();
                itemCmd.CommandText = @"
                    SELECT item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id
                    FROM inventory_items
                    WHERE cache_id = $id";
                itemCmd.Parameters.AddWithValue("$id", cacheId);

                using var itemReader = itemCmd.ExecuteReader();
                while (itemReader.Read())
                {
                    entry.Items.Add(new Models.Inventory.InventoryItemSnapshot
                    {
                        ItemId = (uint)itemReader.GetInt64(0),
                        Quantity = itemReader.GetInt32(1),
                        IsHq = itemReader.GetInt32(2) != 0,
                        IsCollectable = itemReader.GetInt32(3) != 0,
                        Slot = (short)itemReader.GetInt32(4),
                        ContainerType = (uint)itemReader.GetInt64(5),
                        SpiritbondOrCollectability = (ushort)itemReader.GetInt32(6),
                        Condition = (ushort)itemReader.GetInt32(7),
                        GlamourId = (uint)itemReader.GetInt64(8)
                    });
                }

                return entry;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetInventoryCache failed: {ex.Message}", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Gets all inventory cache entries for a character (player + all retainers).
    /// </summary>
    public List<Models.Inventory.InventoryCacheEntry> GetAllInventoryCaches(ulong characterId)
    {
        var result = new List<Models.Inventory.InventoryCacheEntry>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                // Single JOIN query replaces the N+1 pattern (1 cache query + N item queries)
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT ic.id, ic.source_type, ic.retainer_id, ic.name, ic.world, ic.gil, ic.updated_at,
                           ii.item_id, ii.quantity, ii.is_hq, ii.is_collectable, ii.slot, ii.container_type, ii.spiritbond, ii.condition, ii.glamour_id
                    FROM inventory_cache ic
                    LEFT JOIN inventory_items ii ON ii.cache_id = ic.id
                    WHERE ic.character_id = $cid
                    ORDER BY ic.source_type, ic.retainer_id, ii.id";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);

                var cacheMap = new Dictionary<long, Models.Inventory.InventoryCacheEntry>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cacheId = reader.GetInt64(0);
                    if (!cacheMap.TryGetValue(cacheId, out var entry))
                    {
                        entry = new Models.Inventory.InventoryCacheEntry
                        {
                            CharacterId = characterId,
                            SourceType = (Models.Inventory.InventorySourceType)reader.GetInt32(1),
                            RetainerId = (ulong)reader.GetInt64(2),
                            Name = reader.IsDBNull(3) ? null : reader.GetString(3),
                            World = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Gil = reader.GetInt64(5),
                            UpdatedAt = new DateTime(reader.GetInt64(6), DateTimeKind.Utc)
                        };
                        cacheMap[cacheId] = entry;
                        result.Add(entry);
                    }

                    // LEFT JOIN means item columns may be null for empty caches
                    if (!reader.IsDBNull(7))
                    {
                        entry.Items.Add(new Models.Inventory.InventoryItemSnapshot
                        {
                            ItemId = (uint)reader.GetInt64(7),
                            Quantity = reader.GetInt32(8),
                            IsHq = reader.GetInt32(9) != 0,
                            IsCollectable = reader.GetInt32(10) != 0,
                            Slot = (short)reader.GetInt32(11),
                            ContainerType = (uint)reader.GetInt64(12),
                            SpiritbondOrCollectability = (ushort)reader.GetInt32(13),
                            Condition = (ushort)reader.GetInt32(14),
                            GlamourId = (uint)reader.GetInt64(15)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetAllInventoryCaches failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all inventory cache entries across all characters.
    /// </summary>
    public List<Models.Inventory.InventoryCacheEntry> GetAllInventoryCachesAllCharacters()
    {
        var result = new List<Models.Inventory.InventoryCacheEntry>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                // Single JOIN query replaces the N+1 pattern (1 cache query + N item queries)
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT ic.id, ic.character_id, ic.source_type, ic.retainer_id, ic.name, ic.world, ic.gil, ic.updated_at,
                           ii.item_id, ii.quantity, ii.is_hq, ii.is_collectable, ii.slot, ii.container_type, ii.spiritbond, ii.condition, ii.glamour_id
                    FROM inventory_cache ic
                    LEFT JOIN inventory_items ii ON ii.cache_id = ic.id
                    ORDER BY ic.character_id, ic.source_type, ic.retainer_id, ii.id";

                var cacheMap = new Dictionary<long, Models.Inventory.InventoryCacheEntry>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cacheId = reader.GetInt64(0);
                    if (!cacheMap.TryGetValue(cacheId, out var entry))
                    {
                        entry = new Models.Inventory.InventoryCacheEntry
                        {
                            CharacterId = (ulong)reader.GetInt64(1),
                            SourceType = (Models.Inventory.InventorySourceType)reader.GetInt32(2),
                            RetainerId = (ulong)reader.GetInt64(3),
                            Name = reader.IsDBNull(4) ? null : reader.GetString(4),
                            World = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Gil = reader.GetInt64(6),
                            UpdatedAt = new DateTime(reader.GetInt64(7), DateTimeKind.Utc)
                        };
                        cacheMap[cacheId] = entry;
                        result.Add(entry);
                    }

                    // LEFT JOIN means item columns may be null for empty caches
                    if (!reader.IsDBNull(8))
                    {
                        entry.Items.Add(new Models.Inventory.InventoryItemSnapshot
                        {
                            ItemId = (uint)reader.GetInt64(8),
                            Quantity = reader.GetInt32(9),
                            IsHq = reader.GetInt32(10) != 0,
                            IsCollectable = reader.GetInt32(11) != 0,
                            Slot = (short)reader.GetInt32(12),
                            ContainerType = (uint)reader.GetInt64(13),
                            SpiritbondOrCollectability = (ushort)reader.GetInt32(14),
                            Condition = (ushort)reader.GetInt32(15),
                            GlamourId = (uint)reader.GetInt64(16)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetAllInventoryCachesAllCharacters failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Deletes an inventory cache entry and its items.
    /// </summary>
    public void DeleteInventoryCache(ulong characterId, Models.Inventory.InventorySourceType sourceType, ulong retainerId = 0)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"DELETE FROM inventory_cache 
                    WHERE character_id = $cid AND source_type = $type AND retainer_id = $rid";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.Parameters.AddWithValue("$type", (int)sourceType);
                cmd.Parameters.AddWithValue("$rid", (long)retainerId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] DeleteInventoryCache failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets a summary of item counts across all caches (for a specific item or all items).
    /// Returns dictionary of itemId -> total quantity across all caches.
    /// </summary>
    public Dictionary<uint, long> GetItemCountSummary(ulong? characterId = null, uint? itemId = null)
    {
        var result = new Dictionary<uint, long>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                var sql = @"
                    SELECT ii.item_id, SUM(ii.quantity) as total
                    FROM inventory_items ii
                    JOIN inventory_cache ic ON ii.cache_id = ic.id
                    WHERE 1=1";

                if (characterId.HasValue)
                {
                    sql += " AND ic.character_id = $cid";
                    cmd.Parameters.AddWithValue("$cid", (long)characterId.Value);
                }
                if (itemId.HasValue)
                {
                    sql += " AND ii.item_id = $iid";
                    cmd.Parameters.AddWithValue("$iid", (long)itemId.Value);
                }

                sql += " GROUP BY ii.item_id";
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = (uint)reader.GetInt64(0);
                    var total = reader.GetInt64(1);
                    result[id] = total;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetItemCountSummary failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Saves multiple inventory cache entries to the database using batched INSERTs for efficiency.
    /// Each entry's items are inserted in chunks of ~70 rows to stay within SQLite's parameter limits.
    /// </summary>
    /// <param name="entries">The inventory cache entries to save.</param>
    public void SaveInventoryCachesBatched(List<Models.Inventory.InventoryCacheEntry> entries)
    {
        if (entries == null || entries.Count == 0) return;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var transaction = _connection.BeginTransaction();

                try
                {
                    foreach (var entry in entries)
                    {
                        using var cacheCmd = _connection.CreateCommand();
                        cacheCmd.Transaction = transaction;
                        cacheCmd.CommandText = @"
                            INSERT INTO inventory_cache (character_id, source_type, retainer_id, name, world, gil, updated_at)
                            VALUES ($cid, $type, $rid, $name, $world, $gil, $time)
                            ON CONFLICT(character_id, source_type, retainer_id) DO UPDATE SET
                                name = excluded.name,
                                world = excluded.world,
                                gil = excluded.gil,
                                updated_at = excluded.updated_at
                            RETURNING id";
                        cacheCmd.Parameters.AddWithValue("$cid", (long)entry.CharacterId);
                        cacheCmd.Parameters.AddWithValue("$type", (int)entry.SourceType);
                        cacheCmd.Parameters.AddWithValue("$rid", (long)entry.RetainerId);
                        cacheCmd.Parameters.AddWithValue("$name", entry.Name ?? (object)DBNull.Value);
                        cacheCmd.Parameters.AddWithValue("$world", entry.World ?? (object)DBNull.Value);
                        cacheCmd.Parameters.AddWithValue("$gil", entry.Gil);
                        cacheCmd.Parameters.AddWithValue("$time", entry.UpdatedAt.Ticks);

                        var cacheId = (long)cacheCmd.ExecuteScalar()!;

                        using var deleteCmd = _connection.CreateCommand();
                        deleteCmd.Transaction = transaction;
                        deleteCmd.CommandText = "DELETE FROM inventory_items WHERE cache_id = $id";
                        deleteCmd.Parameters.AddWithValue("$id", cacheId);
                        deleteCmd.ExecuteNonQuery();

                        if (entry.Items.Count > 0)
                        {
                            SaveItemsBatched(cacheId, entry.Items, transaction);
                        }
                    }

                    transaction.Commit();
                    LogService.Verbose(LogCategory.Database, $"[KaleidoscopeDb] Saved {entries.Count} inventory caches batched");
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] SaveInventoryCachesBatched failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Inserts inventory items using a single prepared statement with parameter reuse.
    /// Reuses a single command/parameter set across all items, avoiding per-batch allocations
    /// of StringBuilder, SqliteCommand, and SqliteParameter objects.
    /// </summary>
    private void SaveItemsBatched(long cacheId, List<Models.Inventory.InventoryItemSnapshot> items, SqliteTransaction transaction)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO inventory_items 
            (cache_id, item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id)
            VALUES ($cid, $iid, $qty, $hq, $col, $slot, $cont, $sb, $cond, $glam)";

        var cidParam = cmd.Parameters.Add("$cid", SqliteType.Integer);
        var iidParam = cmd.Parameters.Add("$iid", SqliteType.Integer);
        var qtyParam = cmd.Parameters.Add("$qty", SqliteType.Integer);
        var hqParam = cmd.Parameters.Add("$hq", SqliteType.Integer);
        var colParam = cmd.Parameters.Add("$col", SqliteType.Integer);
        var slotParam = cmd.Parameters.Add("$slot", SqliteType.Integer);
        var contParam = cmd.Parameters.Add("$cont", SqliteType.Integer);
        var sbParam = cmd.Parameters.Add("$sb", SqliteType.Integer);
        var condParam = cmd.Parameters.Add("$cond", SqliteType.Integer);
        var glamParam = cmd.Parameters.Add("$glam", SqliteType.Integer);

        cidParam.Value = cacheId;

        foreach (var item in items)
        {
            iidParam.Value = (long)item.ItemId;
            qtyParam.Value = item.Quantity;
            hqParam.Value = item.IsHq ? 1 : 0;
            colParam.Value = item.IsCollectable ? 1 : 0;
            slotParam.Value = item.Slot;
            contParam.Value = (long)item.ContainerType;
            sbParam.Value = item.SpiritbondOrCollectability;
            condParam.Value = item.Condition;
            glamParam.Value = (long)item.GlamourId;
            cmd.ExecuteNonQuery();
        }
    }

}
