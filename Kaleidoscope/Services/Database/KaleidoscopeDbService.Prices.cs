using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Saves multiple item prices in a single transaction for better performance.
    /// Reduces lock contention by batching writes together.
    /// </summary>
    public void SaveItemPricesBatch(IEnumerable<(int ItemId, int WorldId, int MinPriceNq, int MinPriceHq, int LastSaleNq, int LastSaleHq)> prices)
    {
        var priceList = prices.ToList();
        if (priceList.Count == 0) return;

        ExecuteWrite("SaveItemPricesBatch", conn =>
        {
            RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                        INSERT INTO item_prices (item_id, world_id, min_price_nq, min_price_hq, avg_price_nq, avg_price_hq, last_sale_nq, last_sale_hq, sale_velocity, last_updated)
                        VALUES ($iid, $wid, $mnq, $mhq, 0, 0, $lsnq, $lshq, 0, $time)
                        ON CONFLICT(item_id, world_id) DO UPDATE SET
                            min_price_nq = excluded.min_price_nq,
                            min_price_hq = excluded.min_price_hq,
                            last_sale_nq = CASE WHEN excluded.last_sale_nq > 0 THEN excluded.last_sale_nq ELSE item_prices.last_sale_nq END,
                            last_sale_hq = CASE WHEN excluded.last_sale_hq > 0 THEN excluded.last_sale_hq ELSE item_prices.last_sale_hq END,
                            last_updated = excluded.last_updated";

                var iidParam = cmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                var widParam = cmd.Parameters.Add("$wid", Microsoft.Data.Sqlite.SqliteType.Integer);
                var mnqParam = cmd.Parameters.Add("$mnq", Microsoft.Data.Sqlite.SqliteType.Integer);
                var mhqParam = cmd.Parameters.Add("$mhq", Microsoft.Data.Sqlite.SqliteType.Integer);
                var lsnqParam = cmd.Parameters.Add("$lsnq", Microsoft.Data.Sqlite.SqliteType.Integer);
                var lshqParam = cmd.Parameters.Add("$lshq", Microsoft.Data.Sqlite.SqliteType.Integer);
                var timeParam = cmd.Parameters.Add("$time", Microsoft.Data.Sqlite.SqliteType.Integer);

                var now = DateTime.UtcNow.Ticks;

                foreach (var (itemId, worldId, minNq, minHq, lastNq, lastHq) in priceList)
                {
                    iidParam.Value = itemId;
                    widParam.Value = worldId;
                    mnqParam.Value = minNq;
                    mhqParam.Value = minHq;
                    lsnqParam.Value = lastNq;
                    lshqParam.Value = lastHq;
                    timeParam.Value = now;
                    cmd.ExecuteNonQuery();
                }
            });
        });
    }

    /// <summary>
    /// Gets the current price for an item on a world.
    /// Returns (minPriceNq, minPriceHq, lastUpdated) or null if not found.
    /// </summary>
    public (int MinPriceNq, int MinPriceHq, int AvgPriceNq, int AvgPriceHq, float SaleVelocity, DateTime LastUpdated)? GetItemPrice(int itemId, int worldId)
    {
        return ExecuteRead<(int, int, int, int, float, DateTime)?>("GetItemPrice", null, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                    SELECT min_price_nq, min_price_hq, avg_price_nq, avg_price_hq, sale_velocity, last_updated
                    FROM item_prices
                    WHERE item_id = $iid AND world_id = $wid";
            cmd.Parameters.AddWithValue("$iid", itemId);
            cmd.Parameters.AddWithValue("$wid", worldId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetFloat(4),
                    new DateTime(reader.GetInt64(5), DateTimeKind.Utc)
                );
            }

            return null;
        });
    }

    public int? GetMinPrice(int itemId, bool preferHq = false)
    {
        return ExecuteRead<int?>("GetMinPrice", null, conn =>
        {
            using var cmd = conn.CreateCommand();
            if (preferHq)
            {
                cmd.CommandText = @"
                        SELECT CASE
                            WHEN MIN(CASE WHEN min_price_hq > 0 THEN min_price_hq END) IS NOT NULL
                            THEN MIN(CASE WHEN min_price_hq > 0 THEN min_price_hq END)
                            ELSE MIN(CASE WHEN min_price_nq > 0 THEN min_price_nq END)
                        END
                        FROM item_prices
                        WHERE item_id = $iid";
            }
            else
            {
                cmd.CommandText = @"
                        SELECT MIN(CASE WHEN min_price_nq > 0 THEN min_price_nq WHEN min_price_hq > 0 THEN min_price_hq END)
                        FROM item_prices
                        WHERE item_id = $iid";
            }
            cmd.Parameters.AddWithValue("$iid", itemId);

            var result = cmd.ExecuteScalar();
            if (result != null && result != DBNull.Value)
            {
                return Convert.ToInt32(result);
            }

            return null;
        });
    }

    /// <summary>
    /// Gets all item prices for multiple items at once.
    /// Returns last sale prices for inventory value calculation.
    /// </summary>
    public Dictionary<int, (int LastSaleNq, int LastSaleHq)> GetItemPricesBatch(IEnumerable<int> itemIds, int? worldId = null)
    {
        var result = new Dictionary<int, (int, int)>();

        return ExecuteRead("GetItemPricesBatch", result, conn =>
        {
            var itemIdList = itemIds.ToList();
            if (itemIdList.Count == 0) return result;

            using var cmd = conn.CreateCommand();

            var inClause = AddParameterizedInClause(cmd, itemIdList);

            if (worldId.HasValue)
            {
                cmd.CommandText = $@"
                        SELECT item_id, last_sale_nq, last_sale_hq
                        FROM item_prices
                        WHERE item_id IN ({inClause}) AND world_id = $wid";
                cmd.Parameters.AddWithValue("$wid", worldId.Value);
            }
            else
            {
                // Get the most recent last sale price across all worlds (prefer most recently updated)
                cmd.CommandText = $@"
                        SELECT item_id,
                               MAX(CASE WHEN last_sale_nq > 0 THEN last_sale_nq END) as sale_nq,
                               MAX(CASE WHEN last_sale_hq > 0 THEN last_sale_hq END) as sale_hq
                        FROM item_prices
                        WHERE item_id IN ({inClause})
                        GROUP BY item_id";
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var iid = reader.GetInt32(0);
                var snq = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var shq = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                result[iid] = (snq, shq);
            }

            return result;
        });
    }

    /// <summary>
    /// Gets detailed price information for multiple items, including which world has the best price.
    /// Returns item ID -> (MinPrice, WorldId with min price, LastUpdated)
    /// </summary>
    public Dictionary<int, (int MinPrice, int WorldId, DateTime LastUpdated)> GetItemPricesDetailedBatch(IEnumerable<int> itemIds)
    {
        var result = new Dictionary<int, (int MinPrice, int WorldId, DateTime LastUpdated)>();

        return ExecuteRead("GetItemPricesDetailedBatch", result, conn =>
        {
            var itemIdList = itemIds.ToList();
            if (itemIdList.Count == 0) return result;

            using var cmd = conn.CreateCommand();

            var inClause = AddParameterizedInClause(cmd, itemIdList);

            // For each item, find the world with the lowest non-zero NQ price (or HQ if no NQ)
            // Using a subquery to get the row with the minimum price per item
            cmd.CommandText = $@"
                    WITH min_prices AS (
                        SELECT item_id, 
                               CASE WHEN min_price_nq > 0 THEN min_price_nq ELSE min_price_hq END as effective_price,
                               world_id,
                               last_updated,
                               ROW_NUMBER() OVER (
                                   PARTITION BY item_id 
                                   ORDER BY CASE WHEN min_price_nq > 0 THEN min_price_nq ELSE min_price_hq END ASC
                               ) as rn
                        FROM item_prices
                        WHERE item_id IN ({inClause})
                          AND (min_price_nq > 0 OR min_price_hq > 0)
                    )
                    SELECT item_id, effective_price, world_id, last_updated
                    FROM min_prices
                    WHERE rn = 1";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var iid = reader.GetInt32(0);
                var price = reader.GetInt32(1);
                var wid = reader.GetInt32(2);
                var lastUpdated = new DateTime(reader.GetInt64(3), DateTimeKind.Utc);
                result[iid] = (price, wid, lastUpdated);
            }

            return result;
        });
    }

    /// <summary>
    /// Gets the last updated time for items that have stale or missing price data.
    /// Returns item IDs where the last_updated is older than the specified threshold or no price exists.
    /// </summary>
    public HashSet<int> GetStaleItemIds(IEnumerable<int> itemIds, TimeSpan staleThreshold)
    {
        var staleItems = new HashSet<int>();
        var itemIdList = itemIds.ToList();
        if (itemIdList.Count == 0) return staleItems;

        foreach (var id in itemIdList)
            staleItems.Add(id);

        return ExecuteRead("GetStaleItemIds", staleItems, conn =>
        {
            var thresholdTicks = (DateTime.UtcNow - staleThreshold).Ticks;

            using var cmd = conn.CreateCommand();
            var inClause = AddParameterizedInClause(cmd, itemIdList);
            cmd.CommandText = $@"
                    SELECT item_id
                    FROM item_prices
                    WHERE item_id IN ({inClause})
                      AND last_updated > $threshold
                      AND (last_sale_nq > 0 OR last_sale_hq > 0)";
            cmd.Parameters.AddWithValue("$threshold", thresholdTicks);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var itemId = reader.GetInt32(0);
                staleItems.Remove(itemId);
            }

            return staleItems;
        });
    }

    /// <summary>
    /// Saves inventory value history for a character.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="totalValue">Total value (gil + items).</param>
    /// <param name="gilValue">Gil value.</param>
    /// <param name="itemValue">Item value.</param>
    /// <param name="itemContributions">Optional per-item breakdown: (itemId, quantity, unitPrice).</param>
    public void SaveInventoryValueHistory(ulong characterId, long totalValue, long gilValue, long itemValue, 
        List<(int ItemId, long Quantity, int UnitPrice)>? itemContributions = null)
    {
        ExecuteWrite("SaveInventoryValueHistory", conn =>
        {
            RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                        INSERT INTO inventory_value_history (character_id, timestamp, total_value, gil_value, item_value)
                        VALUES ($cid, $time, $total, $gil, $item);
                        SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
                cmd.Parameters.AddWithValue("$total", totalValue);
                cmd.Parameters.AddWithValue("$gil", gilValue);
                cmd.Parameters.AddWithValue("$item", itemValue);
                var historyId = (long)cmd.ExecuteScalar()!;

                if (itemContributions != null && itemContributions.Count > 0)
                {
                    using var itemCmd = conn.CreateCommand();
                    itemCmd.Transaction = tx;
                    itemCmd.CommandText = @"
                            INSERT INTO inventory_value_items (history_id, item_id, quantity, unit_price)
                            VALUES ($hid, $iid, $qty, $price)";

                    var hidParam = itemCmd.Parameters.Add("$hid", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var iidParam = itemCmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var qtyParam = itemCmd.Parameters.Add("$qty", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var priceParam = itemCmd.Parameters.Add("$price", Microsoft.Data.Sqlite.SqliteType.Integer);

                    hidParam.Value = historyId;

                    foreach (var (itemId, quantity, unitPrice) in itemContributions)
                    {
                        iidParam.Value = itemId;
                        qtyParam.Value = quantity;
                        priceParam.Value = unitPrice;
                        itemCmd.ExecuteNonQuery();
                    }
                }
            });

            InvalidateInventoryValueStatsCache();
        });
    }

    public List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> GetInventoryValueHistory(ulong characterId, DateTime? since = null)
    {
        var result = new List<(DateTime, long, long, long)>();

        return ExecuteRead("GetInventoryValueHistory", result, conn =>
        {
            using var cmd = conn.CreateCommand();
            var sql = @"
                    SELECT timestamp, total_value, gil_value, item_value
                    FROM inventory_value_history
                    WHERE character_id = $cid";

            if (since.HasValue)
            {
                sql += " AND timestamp >= $since";
                cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
            }

            sql += " ORDER BY timestamp ASC";
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$cid", (long)characterId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    new DateTime(reader.GetInt64(0), DateTimeKind.Utc),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3)
                ));
            }

            return result;
        });
    }

    public List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> GetAllInventoryValueHistory(DateTime? since = null)
    {
        var result = new List<(ulong, DateTime, long, long, long)>();

        return ExecuteRead("GetAllInventoryValueHistory", result, conn =>
        {
            using var cmd = conn.CreateCommand();
            var sql = @"
                    SELECT character_id, timestamp, total_value, gil_value, item_value
                    FROM inventory_value_history";

            if (since.HasValue)
            {
                sql += " WHERE timestamp >= $since";
                cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
            }

            sql += " ORDER BY timestamp ASC";
            cmd.CommandText = sql;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    (ulong)reader.GetInt64(0),
                    new DateTime(reader.GetInt64(1), DateTimeKind.Utc),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4)
                ));
            }

            return result;
        });
    }

    /// <summary>
    /// Gets inventory value history aggregated across all characters (summed by timestamp) directly in SQL.
    /// More efficient than fetching all data and grouping in C#.
    /// </summary>
    public List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> GetAggregatedInventoryValueHistory(DateTime? since = null)
    {
        var result = new List<(DateTime, long, long, long)>();

        return ExecuteRead("GetAggregatedInventoryValueHistory", result, conn =>
        {
            using var cmd = conn.CreateCommand();
            var sql = @"
                    SELECT timestamp, SUM(total_value), SUM(gil_value), SUM(item_value)
                    FROM inventory_value_history";

            if (since.HasValue)
            {
                sql += " WHERE timestamp >= $since";
                cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
            }

            sql += " GROUP BY timestamp ORDER BY timestamp ASC";
            cmd.CommandText = sql;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    new DateTime(reader.GetInt64(0), DateTimeKind.Utc),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3)
                ));
            }

            return result;
        });
    }

    /// <summary>
    /// Gets the latest timestamp and total record count from inventory_value_history.
    /// Uses an in-memory cache that's invalidated when data is written.
    /// This version is safe to call from the main thread as it avoids DB queries when cache is valid.
    /// </summary>
    /// <param name="characterId">Optional character filter (null = all characters).</param>
    /// <returns>Cached stats. Call RefreshInventoryValueStatsCacheAsync() to update from DB.</returns>
    public (long recordCount, long? maxTimestampTicks) GetInventoryValueHistoryStatsCached(ulong? characterId = null)
    {
        // The in-memory cache only holds GLOBAL stats. A per-character request cannot be served
        // from it, so fall through to a direct (uncached) query for that case.
        if (characterId.HasValue)
            return GetInventoryValueHistoryStats(characterId);

        lock (_inventoryValueStatsLock)
        {
            if (_inventoryValueStatsCacheValid)
            {
                return (_cachedInventoryValueRecordCount, _cachedInventoryValueMaxTimestamp);
            }
        }
        
        // Cache not valid - need to refresh (this will block, but only happens once after invalidation)
        RefreshInventoryValueStatsCache();
        
        lock (_inventoryValueStatsLock)
        {
            return (_cachedInventoryValueRecordCount, _cachedInventoryValueMaxTimestamp);
        }
    }
    
    /// <summary>
    /// Refreshes the inventory value stats cache from the database.
    /// Call this on a background thread after writes to pre-populate the cache.
    /// </summary>
    public void RefreshInventoryValueStatsCache()
    {
        var stats = GetInventoryValueHistoryStats(null);
        lock (_inventoryValueStatsLock)
        {
            _cachedInventoryValueRecordCount = stats.recordCount;
            _cachedInventoryValueMaxTimestamp = stats.maxTimestampTicks;
            _inventoryValueStatsCacheValid = true;
        }
    }
    
    /// <summary>
    /// Invalidates the inventory value stats cache.
    /// Called automatically after writes.
    /// </summary>
    public void InvalidateInventoryValueStatsCache()
    {
        lock (_inventoryValueStatsLock)
        {
            _inventoryValueStatsCacheValid = false;
        }
    }

    /// <summary>
    /// Gets the latest timestamp and total record count from inventory_value_history.
    /// Used for cache invalidation detection.
    /// WARNING: This directly queries the database - prefer GetInventoryValueHistoryStatsCached() for main thread.
    /// </summary>
    public (long recordCount, long? maxTimestampTicks) GetInventoryValueHistoryStats(ulong? characterId = null)
    {
        return ExecuteRead<(long recordCount, long? maxTimestampTicks)>("GetInventoryValueHistoryStats", (0, null), conn =>
        {
            using var cmd = conn.CreateCommand();
            var whereClause = characterId.HasValue ? " WHERE character_id = $cid" : "";
            cmd.CommandText = $"SELECT COUNT(*), MAX(timestamp) FROM inventory_value_history{whereClause}";

            if (characterId.HasValue)
                cmd.Parameters.AddWithValue("$cid", (long)characterId.Value);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var count = reader.GetInt64(0);
                var maxTs = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                return (count, maxTs);
            }

            return (0, null);
        });
    }

    /// <summary>
    /// Clears all price tracking data (item_prices, inventory_value_history, sale_records).
    /// </summary>
    public bool ClearAllPriceData()
    {
        return ExecuteWrite("ClearAllPriceData", false, conn =>
        {
            return RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                cmd.CommandText = "DELETE FROM item_prices";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM inventory_value_history";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM sale_records";
                cmd.ExecuteNonQuery();

                LogService.Info(LogCategory.Database, "[KaleidoscopeDb] Cleared all price tracking data");
                return true;
            });
        });
    }

}
