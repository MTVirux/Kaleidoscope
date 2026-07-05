using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{

    public void SaveSaleRecord(int itemId, int worldId, int pricePerUnit, int quantity, bool isHq, int total, string? buyerName = null)
    {
        ExecuteWrite("SaveSaleRecord", conn =>
        {
            using var cmd = conn.CreateCommand();
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

            using var trimCmd = conn.CreateCommand();
            trimCmd.CommandText = StorageSql.TrimSaleRingForKeySql;
            trimCmd.Parameters.AddWithValue("$iid", itemId);
            trimCmd.Parameters.AddWithValue("$wid", worldId);
            trimCmd.Parameters.AddWithValue("$hq", isHq ? 1 : 0);
            trimCmd.Parameters.AddWithValue("$keep", StorageSql.SaleRingKeep);
            trimCmd.ExecuteNonQuery();
        });
    }

    /// <summary>
    /// Saves multiple sale records in a single transaction for better performance.
    /// Reduces lock contention by batching writes together.
    /// </summary>
    public void SaveSaleRecordsBatch(IEnumerable<(int ItemId, int WorldId, int PricePerUnit, int Quantity, bool IsHq, int Total, string? BuyerName)> records)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return;

        ExecuteWrite("SaveSaleRecordsBatch", conn =>
        {
            RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
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

                // Enforce the recent-N ring for every key this batch touched, in the same
                // transaction so a crash can't leave a key over-cap.
                using var trimCmd = cmd.Connection!.CreateCommand();
                trimCmd.Transaction = tx;
                trimCmd.CommandText = StorageSql.TrimSaleRingForKeySql;
                var trimIid = trimCmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                var trimWid = trimCmd.Parameters.Add("$wid", Microsoft.Data.Sqlite.SqliteType.Integer);
                var trimHq = trimCmd.Parameters.Add("$hq", Microsoft.Data.Sqlite.SqliteType.Integer);
                trimCmd.Parameters.AddWithValue("$keep", StorageSql.SaleRingKeep);

                foreach (var (itemId, worldId, isHq) in recordList
                             .Select(r => (r.ItemId, r.WorldId, r.IsHq)).Distinct())
                {
                    trimIid.Value = itemId;
                    trimWid.Value = worldId;
                    trimHq.Value = isHq ? 1 : 0;
                    trimCmd.ExecuteNonQuery();
                }
            });
        });
    }

    /// <summary>
    /// Gets the most recent sale price for a specific item, used for filtering price spikes.
    /// Returns the latest price_per_unit or 0 if no sales exist.
    /// </summary>
    public int GetMostRecentSalePrice(int itemId, bool isHq)
    {
        return ExecuteRead("GetMostRecentSalePrice", 0, conn =>
        {
            using var cmd = conn.CreateCommand();
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
        });
    }

    public int GetMostRecentSalePriceForWorld(int itemId, int worldId, bool isHq)
    {
        return ExecuteRead("GetMostRecentSalePriceForWorld", 0, conn =>
        {
            using var cmd = conn.CreateCommand();
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
        });
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

        return ExecuteRead("GetLatestSalePrices", result, conn =>
        {
            var itemIdList = itemIds.ToList();
            if (itemIdList.Count == 0) return result;

            var includedList = includedWorldIds?.ToList() ?? new List<int>();
            var excludedList = excludedWorldIds?.ToList() ?? new List<int>();

            using var cmd = conn.CreateCommand();

            var itemInClause = AddParameterizedInClause(cmd, itemIdList, "$item");

            var sql = new System.Text.StringBuilder();
                sql.Append($@"
                    WITH latest_sales AS (
                        SELECT item_id, is_hq, price_per_unit,
                               ROW_NUMBER() OVER (PARTITION BY item_id, is_hq ORDER BY timestamp DESC) as rn
                        FROM sale_records
                        WHERE item_id IN ({itemInClause})");

                // Inclusion filter takes precedence over exclusion
                if (includedList.Count > 0)
                {
                    var worldInClause = AddParameterizedInClause(cmd, includedList, "$wld");
                    sql.Append($" AND world_id IN ({worldInClause})");
                }
                else if (excludedList.Count > 0)
                {
                    var worldExClause = AddParameterizedInClause(cmd, excludedList, "$wex");
                    sql.Append($" AND world_id NOT IN ({worldExClause})");
                }

                if (maxAge.HasValue)
                {
                    var cutoffTicks = (DateTime.UtcNow - maxAge.Value).Ticks;
                    sql.Append(" AND timestamp >= $cutoff");
                    cmd.Parameters.AddWithValue("$cutoff", cutoffTicks);
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

            return result;
        });
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

        return ExecuteRead("GetRecentSalesForCache", result, conn =>
        {
            using var cmd = conn.CreateCommand();

            // Use window function to get the N most recent sales per item/world/hq combination
            var sql = new System.Text.StringBuilder();
                sql.Append(@"
                    WITH ranked_sales AS (
                        SELECT item_id, world_id, is_hq, price_per_unit,
                               ROW_NUMBER() OVER (PARTITION BY item_id, world_id, is_hq ORDER BY timestamp DESC) as rn
                        FROM sale_records
                        WHERE 1=1");
                
                if (maxAge.HasValue)
                {
                    var cutoffTicks = (DateTime.UtcNow - maxAge.Value).Ticks;
                    sql.Append(" AND timestamp >= $cutoff");
                    cmd.Parameters.AddWithValue("$cutoff", cutoffTicks);
                }
                
                sql.Append(@"
                    )
                    SELECT item_id, world_id, is_hq, price_per_unit
                    FROM ranked_sales
                    WHERE rn <= $maxSales
                    ORDER BY item_id, world_id, is_hq, rn");
                
                cmd.Parameters.AddWithValue("$maxSales", maxSalesPerType);
                cmd.CommandText = sql.ToString();

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

            return result;
        });
    }

    public List<(long Id, int WorldId, int PricePerUnit, int Quantity, bool IsHq, int Total, DateTime Timestamp, string? BuyerName)> GetSaleRecords(
        int itemId,
        IEnumerable<int>? excludedWorldIds = null,
        DateTime? since = null,
        int? limit = null)
    {
        var result = new List<(long, int, int, int, bool, int, DateTime, string?)>();

        return ExecuteRead("GetSaleRecords", result, conn =>
        {
            var excludedList = excludedWorldIds?.ToList() ?? new List<int>();

            using var cmd = conn.CreateCommand();
            var sql = new System.Text.StringBuilder();
                sql.Append("SELECT id, world_id, price_per_unit, quantity, is_hq, total, timestamp, buyer_name FROM sale_records WHERE item_id = $iid");
                cmd.Parameters.AddWithValue("$iid", itemId);

                if (excludedList.Count > 0)
                {
                    var worldExClause = AddParameterizedInClause(cmd, excludedList, "$wex");
                    sql.Append($" AND world_id NOT IN ({worldExClause})");
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

            return result;
        });
    }

    public bool DeleteSaleRecord(long id)
    {
        return ExecuteWrite("DeleteSaleRecord", false, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM sale_records WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            var rowsAffected = cmd.ExecuteNonQuery();
            return rowsAffected > 0;
        });
    }

    public int GetSaleRecordCount()
    {
        return ExecuteRead("GetSaleRecordCount", 0, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sale_records";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        });
    }

    public int CleanupOldPriceData(int retentionDays)
    {
        return ExecuteWrite("CleanupOldPriceData", 0, conn =>
        {
            var cutoffTicks = DateTime.UtcNow.AddDays(-retentionDays).Ticks;
            var totalDeleted = 0;

            RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.Parameters.AddWithValue("$cutoff", cutoffTicks);

                cmd.CommandText = "DELETE FROM inventory_value_history WHERE timestamp < $cutoff";
                totalDeleted += cmd.ExecuteNonQuery();
            });

            if (totalDeleted > 0)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Cleaned up {totalDeleted} old inventory value records");
            }

            return totalDeleted;
        });
    }

}
