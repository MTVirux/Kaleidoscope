using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Saves an individual sale record to the database.
    /// </summary>
    public void SaveSaleRecord(int itemId, int worldId, int pricePerUnit, int quantity, bool isHq, int total, string? buyerName = null)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO sale_records (item_id, world_id, price_per_unit, quantity, is_hq, total, timestamp, buyer_name)
                    VALUES ($iid, $wid, $ppu, $qty, $hq, $total, $time, $buyer)";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$wid", worldId);
                cmd.Parameters.AddWithValue("$ppu", pricePerUnit);
                cmd.Parameters.AddWithValue("$qty", quantity);
                cmd.Parameters.AddWithValue("$hq", isHq ? 1 : 0);
                cmd.Parameters.AddWithValue("$total", total);
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
                cmd.Parameters.AddWithValue("$buyer", (object?)buyerName ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] SaveSaleRecord failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Saves multiple sale records in a single transaction for better performance.
    /// Reduces lock contention by batching writes together.
    /// </summary>
    public void SaveSaleRecordsBatch(IEnumerable<(int ItemId, int WorldId, int PricePerUnit, int Quantity, bool IsHq, int Total, string? BuyerName)> records)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var transaction = _connection.BeginTransaction();
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO sale_records (item_id, world_id, price_per_unit, quantity, is_hq, total, timestamp, buyer_name)
                        VALUES ($iid, $wid, $ppu, $qty, $hq, $total, $time, $buyer)";

                    var iidParam = cmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var widParam = cmd.Parameters.Add("$wid", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var ppuParam = cmd.Parameters.Add("$ppu", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var qtyParam = cmd.Parameters.Add("$qty", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var hqParam = cmd.Parameters.Add("$hq", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var totalParam = cmd.Parameters.Add("$total", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var timeParam = cmd.Parameters.Add("$time", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var buyerParam = cmd.Parameters.Add("$buyer", Microsoft.Data.Sqlite.SqliteType.Text);

                    var now = DateTime.UtcNow.Ticks;

                    foreach (var (itemId, worldId, pricePerUnit, quantity, isHq, total, buyerName) in recordList)
                    {
                        iidParam.Value = itemId;
                        widParam.Value = worldId;
                        ppuParam.Value = pricePerUnit;
                        qtyParam.Value = quantity;
                        hqParam.Value = isHq ? 1 : 0;
                        totalParam.Value = total;
                        timeParam.Value = now;
                        buyerParam.Value = (object?)buyerName ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] SaveSaleRecordsBatch failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets the most recent sale price for a specific item, used for filtering price spikes.
    /// Returns the latest price_per_unit or 0 if no sales exist.
    /// </summary>
    public int GetMostRecentSalePrice(int itemId, bool isHq)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT price_per_unit
                    FROM sale_records
                    WHERE item_id = $iid AND is_hq = $hq
                    ORDER BY timestamp DESC
                    LIMIT 1";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$hq", isHq ? 1 : 0);

                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetMostRecentSalePrice failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Gets the most recent sale price for a specific item on a specific world.
    /// Returns the latest price_per_unit or 0 if no sales exist.
    /// </summary>
    public int GetMostRecentSalePriceForWorld(int itemId, int worldId, bool isHq)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT price_per_unit
                    FROM sale_records
                    WHERE item_id = $iid AND world_id = $wid AND is_hq = $hq
                    ORDER BY timestamp DESC
                    LIMIT 1";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$wid", worldId);
                cmd.Parameters.AddWithValue("$hq", isHq ? 1 : 0);

                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetMostRecentSalePriceForWorld failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Gets the latest sale price for items, optionally filtering by included or excluded worlds.
    /// Returns item ID -> (LastSaleNq, LastSaleHq) based on the most recent sales.
    /// </summary>
    /// <param name="itemIds">Item IDs to get prices for.</param>
    /// <param name="includedWorldIds">If specified, only include sales from these worlds.</param>
    /// <param name="excludedWorldIds">If specified, exclude sales from these worlds (ignored if includedWorldIds is set).</param>
    /// <param name="maxAge">Optional maximum age for sale records.</param>
    public Dictionary<int, (int LastSaleNq, int LastSaleHq)> GetLatestSalePrices(
        IEnumerable<int> itemIds, 
        IEnumerable<int>? includedWorldIds = null,
        IEnumerable<int>? excludedWorldIds = null,
        TimeSpan? maxAge = null)
    {
        var result = new Dictionary<int, (int, int)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                var itemIdList = itemIds.ToList();
                if (itemIdList.Count == 0) return result;

                var includedList = includedWorldIds?.ToList() ?? new List<int>();
                var excludedList = excludedWorldIds?.ToList() ?? new List<int>();

                using var cmd = _connection.CreateCommand();
                
                var sql = new System.Text.StringBuilder();
                sql.Append(@"
                    WITH latest_sales AS (
                        SELECT item_id, is_hq, price_per_unit,
                               ROW_NUMBER() OVER (PARTITION BY item_id, is_hq ORDER BY timestamp DESC) as rn
                        FROM sale_records
                        WHERE item_id IN (");
                sql.Append(string.Join(",", itemIdList));
                sql.Append(")");

                // Inclusion filter takes precedence over exclusion
                if (includedList.Count > 0)
                {
                    sql.Append(" AND world_id IN (");
                    sql.Append(string.Join(",", includedList));
                    sql.Append(")");
                }
                else if (excludedList.Count > 0)
                {
                    sql.Append(" AND world_id NOT IN (");
                    sql.Append(string.Join(",", excludedList));
                    sql.Append(")");
                }

                if (maxAge.HasValue)
                {
                    var cutoffTicks = (DateTime.UtcNow - maxAge.Value).Ticks;
                    sql.Append(" AND timestamp >= ");
                    sql.Append(cutoffTicks);
                }

                sql.Append(@"
                    )
                    SELECT item_id,
                           MAX(CASE WHEN is_hq = 0 AND rn = 1 THEN price_per_unit END) as sale_nq,
                           MAX(CASE WHEN is_hq = 1 AND rn = 1 THEN price_per_unit END) as sale_hq
                    FROM latest_sales
                    WHERE rn = 1
                    GROUP BY item_id");

                cmd.CommandText = sql.ToString();

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var iid = reader.GetInt32(0);
                    var snq = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    var shq = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    result[iid] = (snq, shq);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetLatestSalePrices failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the most recent N sale prices per item/world combination for populating the in-memory cache.
    /// Returns a dictionary keyed by (itemId, worldId) with lists of recent NQ and HQ prices.
    /// </summary>
    /// <param name="maxSalesPerType">Maximum number of sales to return per NQ/HQ type.</param>
    /// <param name="maxAge">Optional maximum age for sale records.</param>
    /// <returns>Dictionary of (itemId, worldId) -> (List of NQ prices, List of HQ prices) in most-recent-first order.</returns>
    public Dictionary<(int ItemId, int WorldId), (List<int> NqPrices, List<int> HqPrices)> GetRecentSalesForCache(
        int maxSalesPerType = 5,
        TimeSpan? maxAge = null)
    {
        var result = new Dictionary<(int, int), (List<int>, List<int>)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                
                // Use window function to get the N most recent sales per item/world/hq combination
                var sql = $@"
                    WITH ranked_sales AS (
                        SELECT item_id, world_id, is_hq, price_per_unit,
                               ROW_NUMBER() OVER (PARTITION BY item_id, world_id, is_hq ORDER BY timestamp DESC) as rn
                        FROM sale_records
                        WHERE 1=1";
                
                if (maxAge.HasValue)
                {
                    var cutoffTicks = (DateTime.UtcNow - maxAge.Value).Ticks;
                    sql += $" AND timestamp >= {cutoffTicks}";
                }
                
                sql += $@"
                    )
                    SELECT item_id, world_id, is_hq, price_per_unit
                    FROM ranked_sales
                    WHERE rn <= {maxSalesPerType}
                    ORDER BY item_id, world_id, is_hq, rn";
                
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var itemId = reader.GetInt32(0);
                    var worldId = reader.GetInt32(1);
                    var isHq = reader.GetInt32(2) == 1;
                    var price = reader.GetInt32(3);

                    var key = (itemId, worldId);
                    if (!result.TryGetValue(key, out var prices))
                    {
                        prices = (new List<int>(), new List<int>());
                        result[key] = prices;
                    }

                    if (isHq)
                        prices.Item2.Add(price);
                    else
                        prices.Item1.Add(price);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetRecentSalesForCache failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets sale records for an item, optionally filtering by world and time range.
    /// </summary>
    public List<(long Id, int WorldId, int PricePerUnit, int Quantity, bool IsHq, int Total, DateTime Timestamp, string? BuyerName)> GetSaleRecords(
        int itemId,
        IEnumerable<int>? excludedWorldIds = null,
        DateTime? since = null,
        int? limit = null)
    {
        var result = new List<(long, int, int, int, bool, int, DateTime, string?)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                var excludedList = excludedWorldIds?.ToList() ?? new List<int>();

                using var cmd = _connection.CreateCommand();
                var sql = new System.Text.StringBuilder();
                sql.Append("SELECT id, world_id, price_per_unit, quantity, is_hq, total, timestamp, buyer_name FROM sale_records WHERE item_id = $iid");
                cmd.Parameters.AddWithValue("$iid", itemId);

                if (excludedList.Count > 0)
                {
                    sql.Append(" AND world_id NOT IN (");
                    sql.Append(string.Join(",", excludedList));
                    sql.Append(")");
                }

                if (since.HasValue)
                {
                    sql.Append(" AND timestamp >= $since");
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }

                sql.Append(" ORDER BY timestamp DESC");

                if (limit.HasValue)
                {
                    sql.Append(" LIMIT $limit");
                    cmd.Parameters.AddWithValue("$limit", limit.Value);
                }

                cmd.CommandText = sql.ToString();

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add((
                        reader.GetInt64(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        reader.GetInt32(3),
                        reader.GetInt32(4) == 1,
                        reader.GetInt32(5),
                        new DateTime(reader.GetInt64(6), DateTimeKind.Utc),
                        reader.IsDBNull(7) ? null : reader.GetString(7)
                    ));
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetSaleRecords failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Deletes a specific sale record by ID.
    /// </summary>
    /// <param name="id">The ID of the sale record to delete.</param>
    /// <returns>True if the record was deleted, false otherwise.</returns>
    public bool DeleteSaleRecord(long id)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM sale_records WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                var rowsAffected = cmd.ExecuteNonQuery();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] DeleteSaleRecord failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Deletes a specific sale record by ID and recalculates all inventory value history records
    /// that occurred after the sale's timestamp to ensure consistency.
    /// </summary>
    /// <param name="id">The ID of the sale record to delete.</param>
    /// <param name="saleTimestamp">The timestamp of the sale being deleted.</param>
    /// <returns>True if the record was deleted, false otherwise.</returns>
    public bool DeleteSaleRecordWithHistoryCleanup(long id, DateTime saleTimestamp)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var transaction = _connection.BeginTransaction();

                try
                {
                    // First, get the item_id from the sale record we're about to delete
                    int? saleItemId = null;
                    using (var getItemCmd = _connection.CreateCommand())
                    {
                        getItemCmd.Transaction = transaction;
                        getItemCmd.CommandText = "SELECT item_id FROM sale_records WHERE id = $id";
                        getItemCmd.Parameters.AddWithValue("$id", id);
                        var result = getItemCmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            saleItemId = Convert.ToInt32(result);
                        }
                    }

                    if (!saleItemId.HasValue)
                    {
                        transaction.Rollback();
                        return false;
                    }

                    // Delete the sale record
                    using var deleteCmd = _connection.CreateCommand();
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM sale_records WHERE id = $id";
                    deleteCmd.Parameters.AddWithValue("$id", id);
                    var rowsAffected = deleteCmd.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        // Get the new latest sale price for this item (after deletion)
                        int newPrice = 0;
                        using (var priceCmd = _connection.CreateCommand())
                        {
                            priceCmd.Transaction = transaction;
                            priceCmd.CommandText = @"
                                SELECT price_per_unit FROM sale_records 
                                WHERE item_id = $iid 
                                ORDER BY timestamp DESC 
                                LIMIT 1";
                            priceCmd.Parameters.AddWithValue("$iid", saleItemId.Value);
                            var priceResult = priceCmd.ExecuteScalar();
                            if (priceResult != null && priceResult != DBNull.Value)
                            {
                                newPrice = Convert.ToInt32(priceResult);
                            }
                        }

                        // Find all inventory_value_history records at or after the sale timestamp
                        // that have contributions for this item
                        var historyToUpdate = new List<(long HistoryId, long OldQuantity, int OldPrice)>();
                        using (var findCmd = _connection.CreateCommand())
                        {
                            findCmd.Transaction = transaction;
                            findCmd.CommandText = @"
                                SELECT h.id, i.quantity, i.unit_price
                                FROM inventory_value_history h
                                JOIN inventory_value_items i ON i.history_id = h.id
                                WHERE h.timestamp >= $timestamp AND i.item_id = $iid";
                            findCmd.Parameters.AddWithValue("$timestamp", saleTimestamp.Ticks);
                            findCmd.Parameters.AddWithValue("$iid", saleItemId.Value);
                            
                            using var reader = findCmd.ExecuteReader();
                            while (reader.Read())
                            {
                                historyToUpdate.Add((
                                    reader.GetInt64(0),
                                    reader.GetInt64(1),
                                    reader.GetInt32(2)
                                ));
                            }
                        }

                        // Update each affected history record
                        if (historyToUpdate.Count > 0)
                        {
                            using var updateHistoryCmd = _connection.CreateCommand();
                            updateHistoryCmd.Transaction = transaction;
                            updateHistoryCmd.CommandText = @"
                                UPDATE inventory_value_history 
                                SET item_value = item_value - $oldContrib + $newContrib,
                                    total_value = total_value - $oldContrib + $newContrib
                                WHERE id = $hid";
                            
                            var hidParam = updateHistoryCmd.Parameters.Add("$hid", Microsoft.Data.Sqlite.SqliteType.Integer);
                            var oldContribParam = updateHistoryCmd.Parameters.Add("$oldContrib", Microsoft.Data.Sqlite.SqliteType.Integer);
                            var newContribParam = updateHistoryCmd.Parameters.Add("$newContrib", Microsoft.Data.Sqlite.SqliteType.Integer);

                            using var updateItemCmd = _connection.CreateCommand();
                            updateItemCmd.Transaction = transaction;
                            updateItemCmd.CommandText = @"
                                UPDATE inventory_value_items 
                                SET unit_price = $newPrice
                                WHERE history_id = $hid AND item_id = $iid";
                            
                            var itemHidParam = updateItemCmd.Parameters.Add("$hid", Microsoft.Data.Sqlite.SqliteType.Integer);
                            var newPriceParam = updateItemCmd.Parameters.Add("$newPrice", Microsoft.Data.Sqlite.SqliteType.Integer);
                            var iidParam = updateItemCmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                            iidParam.Value = saleItemId.Value;
                            newPriceParam.Value = newPrice;

                            foreach (var (historyId, quantity, oldPrice) in historyToUpdate)
                            {
                                var oldContribution = quantity * oldPrice;
                                var newContribution = quantity * newPrice;

                                hidParam.Value = historyId;
                                oldContribParam.Value = oldContribution;
                                newContribParam.Value = newContribution;
                                updateHistoryCmd.ExecuteNonQuery();

                                itemHidParam.Value = historyId;
                                updateItemCmd.ExecuteNonQuery();
                            }

                            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Recalculated {historyToUpdate.Count} inventory value history records for item {saleItemId.Value} (new price: {newPrice})");
                        }
                    }

                    transaction.Commit();
                    return rowsAffected > 0;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] DeleteSaleRecordWithHistoryCleanup failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the count of sale records in the database.
    /// </summary>
    public int GetSaleRecordCount()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sale_records";
                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetSaleRecordCount failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Cleans up old price history data based on retention settings.
    /// </summary>
    public int CleanupOldPriceData(int retentionDays)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                var cutoffTicks = DateTime.UtcNow.AddDays(-retentionDays).Ticks;

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM price_history WHERE timestamp < $cutoff";
                cmd.Parameters.AddWithValue("$cutoff", cutoffTicks);
                var deleted = cmd.ExecuteNonQuery();

                // Also clean inventory value history
                cmd.CommandText = "DELETE FROM inventory_value_history WHERE timestamp < $cutoff";
                deleted += cmd.ExecuteNonQuery();

                // Also clean old sale records
                cmd.CommandText = "DELETE FROM sale_records WHERE timestamp < $cutoff";
                deleted += cmd.ExecuteNonQuery();

                if (deleted > 0)
                {
                    LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Cleaned up {deleted} old price/value/sale records");
                }

                return deleted;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] CleanupOldPriceData failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Gets the approximate size of price data in bytes.
    /// </summary>
    public long GetPriceDataSize()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT 
                        (SELECT COUNT(*) FROM item_prices) * 50 +
                        (SELECT COUNT(*) FROM price_history) * 30 +
                        (SELECT COUNT(*) FROM inventory_value_history) * 40 +
                        (SELECT COUNT(*) FROM sale_records) * 45";
                
                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetPriceDataSize failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Cleans up price data to fit within size limit.
    /// </summary>
    public int CleanupPriceDataBySize(long maxSizeBytes)
    {
        var currentSize = GetPriceDataSize();
        if (currentSize <= maxSizeBytes) return 0;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                // Delete oldest records until we're under the limit
                var totalDeleted = 0;
                while (currentSize > maxSizeBytes)
                {
                    using var cmd = _connection.CreateCommand();
                    
                    // Delete oldest price history first
                    cmd.CommandText = @"
                        DELETE FROM price_history 
                        WHERE id IN (SELECT id FROM price_history ORDER BY timestamp ASC LIMIT 1000)";
                    var deleted = cmd.ExecuteNonQuery();
                    
                    // Also delete oldest sale records
                    cmd.CommandText = @"
                        DELETE FROM sale_records 
                        WHERE id IN (SELECT id FROM sale_records ORDER BY timestamp ASC LIMIT 1000)";
                    deleted += cmd.ExecuteNonQuery();
                    
                    totalDeleted += deleted;

                    if (deleted == 0) break;
                    currentSize = GetPriceDataSize();
                }

                if (totalDeleted > 0)
                {
                    LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Cleaned up {totalDeleted} price history/sale records to fit size limit");
                }

                return totalDeleted;
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] CleanupPriceDataBySize failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

}
