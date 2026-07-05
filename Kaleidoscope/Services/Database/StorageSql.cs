// Kaleidoscope/Services/Database/StorageSql.cs
namespace Kaleidoscope.Services.Database;

/// <summary>
/// SQL for the sale_records recent-N ring. Dalamud-free so the test project can compile it.
/// sale_records keeps only the newest <see cref="SaleRingKeep"/> sales per (item, world, hq);
/// consumers only ever read the most recent few sales per key.
/// </summary>
public static class StorageSql
{
    public const int SaleRingKeep = 10;

    /// <summary>Trims one (item, world, hq) key to the newest $keep rows. Run after inserting a batch.</summary>
    public const string TrimSaleRingForKeySql = @"
DELETE FROM sale_records
WHERE item_id = $iid AND world_id = $wid AND is_hq = $hq
  AND id NOT IN (
      SELECT id FROM sale_records
      WHERE item_id = $iid AND world_id = $wid AND is_hq = $hq
      ORDER BY timestamp DESC, id DESC
      LIMIT $keep)";

    /// <summary>One-time global trim (v8 migration): newest $keep rows per (item, world, hq).</summary>
    public const string TrimSaleRingAllSql = @"
DELETE FROM sale_records
WHERE id IN (
    SELECT id FROM (
        SELECT id, ROW_NUMBER() OVER (
            PARTITION BY item_id, world_id, is_hq
            ORDER BY timestamp DESC, id DESC) AS rn
        FROM sale_records)
    WHERE rn > $keep)";
}
