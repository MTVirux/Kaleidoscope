using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{

    public List<ulong> GetAvailableCharacters(string variable)
    {
        var result = new List<ulong>();

        // Translate legacy variable name to resource_history coordinates
        // characterId=0 is used here because we want all characters; the
        // mapping only needs the variable type, not a specific owner.
        var mapping = Kaleidoscope.Services.Resources.Adapters.LegacyVariableTranslator.Translate(variable, 0);
        if (mapping == null) return result;

        return ExecuteRead("GetAvailableCharacters", result, conn =>
        {
            using var cmd = conn.CreateCommand();
            // For retainer-aggregate variables (ItemRetainer_) owner_id is the character,
            // so we join owner_names to discover the real character owners.
            // For all other variables, owner_id IS the character_id.
            cmd.CommandText = @"
                    SELECT DISTINCT owner_id FROM resource_history
                    WHERE item_id = $iid AND container = $cont AND owner_id != 0
                    ORDER BY owner_id";
            cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
            cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var cid = reader.GetInt64(0);
                if (cid != 0)
                    result.Add((ulong)cid);
            }

            return result;
        }, debugLog: true);
    }

    /// <summary>
    /// Gets all unique variable names that start with the given prefix.
    /// Used to find all item tracking series (Item_*, ItemRetainer_*, etc.).
    /// </summary>
    public List<string> GetAllVariablesWithPrefix(string prefix)
    {
        // After Phase 3 the series table no longer exists. Reconstruct distinct legacy
        // variable names from resource_history by reading distinct (item_id, container,
        // owner_id) rows and reverse-mapping to the Item_* / ItemRetainer_* / ItemRetainerX_*
        // naming convention.  Used only for startup cache pre-population; callers tolerate
        // an empty list gracefully.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        return ExecuteRead("GetAllVariablesWithPrefix", result, conn =>
        {
            using var cmd = conn.CreateCommand();
                // Containers of interest (int values from Container enum):
                //   PlayerAggregate   = item from player inventory  → "Item_{itemId}"
                //   RetainerAggregate = item from retainer (agg)    → "ItemRetainer_{itemId}"
                //   RetainerPage1-7   = item per-retainer row       → "ItemRetainerX_{ownerId}_{itemId}"
                // We distinguish them by container value.
                cmd.CommandText = @"
                    SELECT DISTINCT item_id, container, owner_id FROM resource_history
                    WHERE owner_id != 0 AND item_id < 1000000";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var itemId  = (uint)reader.GetInt64(0);
                    var cont    = (Kaleidoscope.Models.Resources.Container)reader.GetInt32(1);
                    var ownerId = (ulong)reader.GetInt64(2);

                    string? legacyName = cont switch
                    {
                        Kaleidoscope.Models.Resources.Container.PlayerAggregate    => $"Item_{itemId}",
                        Kaleidoscope.Models.Resources.Container.RetainerAggregate  => $"ItemRetainer_{itemId}",
                        Kaleidoscope.Models.Resources.Container.RetainerPage1 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage2 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage3 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage4 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage5 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage6 or
                        Kaleidoscope.Models.Resources.Container.RetainerPage7      => $"ItemRetainerX_{ownerId}_{itemId}",
                        _ => null,
                    };

                if (legacyName != null
                    && legacyName.StartsWith(prefix, StringComparison.Ordinal)
                    && seen.Add(legacyName))
                    result.Add(legacyName);
            }

            result.Sort(StringComparer.Ordinal);

            return result;
        }, debugLog: true);
    }

    /// <summary>
    /// Saves or updates a character's game name (automatically detected from the game).
    /// Preserves any existing display_name that was set by the user.
    /// </summary>
    public bool SaveCharacterName(ulong characterId, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        return ExecuteWrite("SaveCharacterName", false, conn =>
        {
            // Upsert only the name column; display_name and time_series_color are preserved on
            // existing rows (and default to NULL on insert) — same result as the prior
            // SELECT-then-INSERT-OR-REPLACE, matching the sibling upserts below.
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                    INSERT INTO character_names(character_id, name)
                    VALUES($c, $n)
                    ON CONFLICT(character_id) DO UPDATE SET name = excluded.name";
            cmd.Parameters.AddWithValue("$c", (long)characterId);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();

            return true;
        }, debugLog: true);
    }

    /// <summary>
    /// Saves or updates a character's display name (user-customizable).
    /// </summary>
    /// <param name="characterId">The character's content ID.</param>
    /// <param name="displayName">The custom display name. Pass null to clear and use game name.</param>
    /// <returns>True if successful.</returns>
    public bool SaveCharacterDisplayName(ulong characterId, string? displayName)
    {
        return ExecuteWrite("SaveCharacterDisplayName", false, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                    INSERT INTO character_names(character_id, name, display_name)
                    VALUES($c, NULL, $d)
                    ON CONFLICT(character_id) DO UPDATE SET display_name = $d";
            cmd.Parameters.AddWithValue("$c", (long)characterId);
            cmd.Parameters.AddWithValue("$d", string.IsNullOrEmpty(displayName) ? (object)DBNull.Value : displayName);
            cmd.ExecuteNonQuery();

            return true;
        }, debugLog: true);
    }

    /// <summary>
    /// Saves or updates a character's time series color.
    /// </summary>
    /// <param name="characterId">The character's content ID.</param>
    /// <param name="color">The ARGB color value. Pass null to clear and use default colors.</param>
    /// <returns>True if successful.</returns>
    public bool SaveCharacterTimeSeriesColor(ulong characterId, uint? color)
    {
        return ExecuteWrite("SaveCharacterTimeSeriesColor", false, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                    INSERT INTO character_names(character_id, name, display_name, time_series_color)
                    VALUES($c, NULL, NULL, $col)
                    ON CONFLICT(character_id) DO UPDATE SET time_series_color = $col";
            cmd.Parameters.AddWithValue("$c", (long)characterId);
            cmd.Parameters.AddWithValue("$col", color.HasValue ? (object)(long)color.Value : DBNull.Value);
            cmd.ExecuteNonQuery();

            return true;
        }, debugLog: true);
    }

    /// <summary>
    /// Gets the display name for a character (custom display_name if set, otherwise game name).
    /// Queries the database directly. Prefer using CharacterDataCacheService for cached lookups.
    /// </summary>
    public string? GetCharacterName(ulong characterId)
    {
        var rows = ReadCharacterRows(characterId);
        return rows.Count > 0 ? (rows[0].displayName ?? rows[0].gameName) : null;
    }

    /// <summary>
    /// Gets the time series color for a character (null if not set).
    /// Queries the database directly. Prefer using CharacterDataCacheService for cached lookups.
    /// </summary>
    public uint? GetCharacterTimeSeriesColor(ulong characterId)
    {
        var rows = ReadCharacterRows(characterId);
        return rows.Count > 0 ? rows[0].timeSeriesColor : null;
    }

    /// <summary>
    /// Gets all stored character name mappings (returns display_name if set, otherwise game name).
    /// Queries the database directly. Prefer using CharacterDataCacheService for cached lookups.
    /// </summary>
    public List<(ulong characterId, string? name)> GetAllCharacterNames()
    {
        var result = new List<(ulong, string?)>();
        foreach (var (cid, gameName, displayName, _) in ReadCharacterRows())
            if (cid != 0)
                result.Add((cid, displayName ?? gameName));
        return result;
    }

    /// <summary>
    /// Gets all stored character name mappings with both game and display names.
    /// Queries the database directly. Prefer using CharacterDataCacheService for cached lookups.
    /// </summary>
    public List<(ulong characterId, string? gameName, string? displayName)> GetAllCharacterNamesExtended()
    {
        var result = new List<(ulong, string?, string?)>();
        foreach (var (cid, gameName, displayName, _) in ReadCharacterRows())
            if (cid != 0)
                result.Add((cid, gameName, displayName));
        return result;
    }

    /// <summary>
    /// Gets all stored character data including time series colors.
    /// Queries the database directly. Prefer using CharacterDataCacheService for cached lookups.
    /// </summary>
    public List<(ulong characterId, string? gameName, string? displayName, uint? timeSeriesColor)> GetAllCharacterDataExtended()
    {
        var result = new List<(ulong, string?, string?, uint?)>();
        foreach (var row in ReadCharacterRows())
            if (row.characterId != 0)
                result.Add(row);
        return result;
    }

    /// <summary>
    /// Gets all stored character name mappings as a dictionary (display_name if set, otherwise game name).
    /// Queries the database directly. Prefer using CharacterDataCacheService for cached lookups.
    /// </summary>
    public IReadOnlyDictionary<ulong, string?> GetAllCharacterNamesDict()
    {
        var result = new Dictionary<ulong, string?>();
        foreach (var (cid, gameName, displayName, _) in ReadCharacterRows())
            if (cid != 0)
                result[cid] = displayName ?? gameName;
        return result;
    }

    /// <summary>
    /// Shared full-row reader for character_names. Returns raw rows (NOT filtered by
    /// character_id == 0 — the public projections apply that filter). When a specific
    /// <paramref name="characterId"/> is given the lookup is a single indexed primary-key read.
    /// </summary>
    private List<(ulong characterId, string? gameName, string? displayName, uint? timeSeriesColor)> ReadCharacterRows(ulong? characterId = null)
    {
        var rows = new List<(ulong, string?, string?, uint?)>();
        return ExecuteRead("ReadCharacterRows", rows, conn =>
        {
            using var cmd = conn.CreateCommand();
            if (characterId.HasValue)
            {
                cmd.CommandText = "SELECT character_id, name, display_name, time_series_color FROM character_names WHERE character_id = $c";
                cmd.Parameters.AddWithValue("$c", (long)characterId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT character_id, name, display_name, time_series_color FROM character_names";
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var cid = (ulong)reader.GetInt64(0);
                var gameName = reader.IsDBNull(1) ? null : reader.GetString(1);
                var displayName = reader.IsDBNull(2) ? null : reader.GetString(2);
                uint? color = reader.IsDBNull(3) ? null : (uint)reader.GetInt64(3);
                rows.Add((cid, gameName, displayName, color));
            }

            return rows;
        }, debugLog: true);
    }

    /// <summary>
    /// Deletes all data associated with a character across all character-scoped tables:
    /// resource_history, resources, owner_names (character + its retainers),
    /// character_names, and inventory value history + items (CASCADE).
    /// World-scoped tables (sale_records, price_history, item_prices) are not affected.
    /// Returns the total number of rows deleted.
    /// </summary>
    public int DeleteAllCharacterData(ulong characterId)
    {
        return ExecuteWrite("DeleteAllCharacterData", 0, conn =>
        {
            var totalDeleted = 0;

            RunInTransaction(tx =>
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.Parameters.AddWithValue("$c", (long)characterId);

                    // Unified resources tables — character-owned rows
                    cmd.CommandText = "DELETE FROM resource_history WHERE owner_id = $c";
                    totalDeleted += cmd.ExecuteNonQuery();

                    // Retainer-owned resource_history rows (retainers whose parent is this character)
                    cmd.CommandText = @"DELETE FROM resource_history
                        WHERE owner_id IN (
                            SELECT owner_id FROM resources WHERE parent_owner_id = $c AND owner_kind = 1
                        )";
                    totalDeleted += cmd.ExecuteNonQuery();

                    cmd.CommandText = "DELETE FROM resources WHERE owner_id = $c OR parent_owner_id = $c";
                    totalDeleted += cmd.ExecuteNonQuery();

                    // owner_names: character itself (owner_kind = 0)
                    cmd.CommandText = "DELETE FROM owner_names WHERE owner_id = $c AND owner_kind = 0";
                    totalDeleted += cmd.ExecuteNonQuery();

                    // owner_names: retainers that belong to this character
                    cmd.CommandText = @"DELETE FROM owner_names
                        WHERE owner_kind = 1
                          AND owner_id IN (
                              SELECT owner_id FROM resources WHERE parent_owner_id = $c AND owner_kind = 1
                          )";
                    totalDeleted += cmd.ExecuteNonQuery();

                    // Character name registry
                    cmd.CommandText = "DELETE FROM character_names WHERE character_id = $c";
                    totalDeleted += cmd.ExecuteNonQuery();

                    // Inventory value history (CASCADE deletes inventory_value_items)
                    // Delete in batches of 100 to limit CASCADE impact
                    int batchDeleted;
                    do
                    {
                        cmd.CommandText = @"DELETE FROM inventory_value_history
                            WHERE id IN (SELECT id FROM inventory_value_history WHERE character_id = $c LIMIT 100)";
                        batchDeleted = cmd.ExecuteNonQuery();
                        totalDeleted += batchDeleted;
                } while (batchDeleted > 0);
            });

            if (totalDeleted > 0)
            {
                LogService.Info(LogCategory.Database,
                    $"[KaleidoscopeDb] Deleted all data for character {characterId}: {totalDeleted} rows removed");
            }

            return totalDeleted;
        });
    }

}
