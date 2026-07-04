using Kaleidoscope.Services.Resources.Adapters;
using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Exports all history rows for a variable to a CSV string.
    /// After Phase 3 this queries resource_history rather than the dropped points/series tables.
    /// </summary>
    public string ExportToCsv(string variable, ulong? characterId = null)
    {
        var sb = new StringBuilder();

        var resolvedOwnerId = characterId.HasValue && characterId.Value != 0 ? characterId.Value : (ulong)0;
        var mapping = LegacyVariableTranslator.Translate(variable, resolvedOwnerId);
        if (mapping == null) return sb.ToString();

        return ExecuteRead("ExportToCsv", sb.ToString(), conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("$iid", (long)mapping.Value.ItemId);
            cmd.Parameters.AddWithValue("$cont", (int)mapping.Value.Container);

            if (characterId == null || characterId == 0)
            {
                sb.AppendLine("timestamp_utc,quantity,owner_id");
                cmd.CommandText = @"SELECT timestamp, quantity, owner_id FROM resource_history
                        WHERE item_id = $iid AND container = $cont
                        ORDER BY timestamp ASC";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ticks = reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    var cid = reader.GetInt64(2);
                    sb.AppendLine($"{new DateTime(ticks, DateTimeKind.Utc):O},{value},{cid}");
                }
            }
            else
            {
                sb.AppendLine("timestamp_utc,quantity");
                cmd.CommandText = @"SELECT timestamp, quantity FROM resource_history
                        WHERE item_id = $iid AND container = $cont AND owner_id = $oid
                        ORDER BY timestamp ASC";
                cmd.Parameters.AddWithValue("$oid", (long)mapping.Value.OwnerId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ticks = reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    sb.AppendLine($"{new DateTime(ticks, DateTimeKind.Utc):O},{value}");
                }
            }

            return sb.ToString();
        });
    }

}
