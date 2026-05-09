namespace Kaleidoscope.Services.Database;

/// <summary>
/// Standalone holder for migration SQL constants. Not a partial class so the test
/// project can compile this file in isolation via &lt;Compile Include&gt; and assert on
/// the SQL strings directly without dragging in Dalamud-dependent code.
/// </summary>
internal static class MigrationSql
{
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
}
