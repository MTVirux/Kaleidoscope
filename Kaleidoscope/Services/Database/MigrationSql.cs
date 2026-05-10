using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;

namespace Kaleidoscope.Services.Database;

/// <summary>
/// Standalone holder for migration SQL constants. Not a partial class so the test
/// project can compile this file in isolation via &lt;Compile Include&gt; and assert on
/// the SQL strings directly without dragging in Dalamud-dependent code.
/// </summary>
internal static class MigrationSql
{
    /// <summary>
    /// One-shot migration SQL for the v5→v6 upgrade. Reads from the legacy inventory_cache
    /// and inventory_items tables (dropped by migration v7). On a v6+ DB, this code never runs.
    /// Kept for reference and for users upgrading from a v5 backup.
    /// </summary>
    public const string BackfillResourcesFromInventoryItemsSql = @"
INSERT OR REPLACE INTO resources (owner_id, owner_kind, parent_owner_id, container, item_id, slot,
                                  quantity, flags, spiritbond, collectability, condition, glamour_id, updated_at)
SELECT
    CASE ic.source_type WHEN 0 THEN ic.character_id ELSE ic.retainer_id END,
    ic.source_type,
    CASE ic.source_type WHEN 0 THEN 0 ELSE ic.character_id END,
    ii.container_type,
    ii.item_id,
    ii.slot,
    ii.quantity,
    (CASE ii.is_hq WHEN 1 THEN 1 ELSE 0 END) | (CASE ii.is_collectable WHEN 1 THEN 2 ELSE 0 END),
    CASE WHEN ii.is_collectable = 0 THEN ii.spiritbond ELSE 0 END,
    CASE WHEN ii.is_collectable = 1 THEN ii.spiritbond ELSE 0 END,
    ii.condition,
    ii.glamour_id,
    ic.updated_at
FROM inventory_items ii
JOIN inventory_cache ic ON ii.cache_id = ic.id;
";

    /// <summary>
    /// One-shot migration SQL for the v5→v6 upgrade. Reads gil values from the legacy
    /// inventory_cache table (dropped by migration v7). On a v6+ DB, this code never runs.
    /// </summary>
    public const string BackfillGilRowsSql = @"
INSERT OR REPLACE INTO resources (owner_id, owner_kind, parent_owner_id, container, item_id, slot,
                                  quantity, flags, spiritbond, collectability, condition, glamour_id, updated_at)
SELECT
    CASE ic.source_type WHEN 0 THEN ic.character_id ELSE ic.retainer_id END,
    ic.source_type,
    CASE ic.source_type WHEN 0 THEN 0 ELSE ic.character_id END,
    90000,           -- Container.SpecialPlayer
    1000001,         -- ResourceCatalog.GilItemId
    -1,
    ic.gil,
    0, 0, 0, 0, 0,
    ic.updated_at
FROM inventory_cache ic
WHERE ic.gil > 0;
";

    /// <summary>
    /// One-shot migration helper for the v5→v6 upgrade. Reads from the legacy series and
    /// points tables (dropped by migration v7) and writes corresponding rows into
    /// resource_history. On a v6+ DB, this code never runs.
    /// </summary>
    /// <returns>(written, skipped) — count of resource_history rows written and series skipped due to unknown variable names.</returns>
    public static (int Written, int Skipped) BackfillResourceHistoryFromSeries(SqliteConnection conn, SqliteTransaction? tx)
    {
        var written = 0;
        var skipped = 0;

        using var insert = conn.CreateCommand();
        if (tx != null) insert.Transaction = tx;
        insert.CommandText = @"
            INSERT INTO resource_history (owner_id, owner_kind, container, item_id, timestamp,
                                          quantity, change_amount, source_kind, source_detail)
            VALUES ($oid, $okind, $container, $iid, $ts, $qty, $chg, 0, NULL)";
        var oidP   = insert.Parameters.Add("$oid",       SqliteType.Integer);
        var okindP = insert.Parameters.Add("$okind",     SqliteType.Integer);
        var contP  = insert.Parameters.Add("$container", SqliteType.Integer);
        var iidP   = insert.Parameters.Add("$iid",       SqliteType.Integer);
        var tsP    = insert.Parameters.Add("$ts",        SqliteType.Integer);
        var qtyP   = insert.Parameters.Add("$qty",       SqliteType.Integer);
        var chgP   = insert.Parameters.Add("$chg",       SqliteType.Integer);

        // Read all series first; parsing only depends on variable + character_id
        var jobs = new List<(long SeriesId, ResourceCatalog.LegacyVariableMapping Mapping)>();
        using (var seriesCmd = conn.CreateCommand())
        {
            if (tx != null) seriesCmd.Transaction = tx;
            seriesCmd.CommandText = "SELECT id, variable, character_id FROM series";
            using var sr = seriesCmd.ExecuteReader();
            while (sr.Read())
            {
                var sid      = sr.GetInt64(0);
                var variable = sr.GetString(1);
                var charId   = (ulong)sr.GetInt64(2);

                var mapping = ResourceCatalog.ParseLegacyVariableName(variable, charId);
                if (mapping == null) { skipped++; continue; }
                jobs.Add((sid, mapping.Value));
            }
        }

        foreach (var (seriesId, mapping) in jobs)
        {
            using var pointsCmd = conn.CreateCommand();
            if (tx != null) pointsCmd.Transaction = tx;
            pointsCmd.CommandText = "SELECT timestamp, value FROM points WHERE series_id = $sid ORDER BY timestamp";
            pointsCmd.Parameters.AddWithValue("$sid", seriesId);
            using var pr = pointsCmd.ExecuteReader();

            long? prevQty = null;
            while (pr.Read())
            {
                var ts  = pr.GetInt64(0);
                var qty = pr.GetInt64(1);
                var chg = prevQty.HasValue ? qty - prevQty.Value : qty;
                prevQty = qty;

                oidP.Value   = (long)mapping.OwnerId;
                okindP.Value = (int)mapping.OwnerKind;
                contP.Value  = (int)mapping.Container;
                iidP.Value   = (long)mapping.ItemId;
                tsP.Value    = ts;
                qtyP.Value   = qty;
                chgP.Value   = chg;
                insert.ExecuteNonQuery();
                written++;
            }
        }

        return (written, skipped);
    }
}
