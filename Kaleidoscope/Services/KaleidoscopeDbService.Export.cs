using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Exports data to a CSV string.
    /// </summary>
    public string ExportToCsv(string variable, ulong? characterId = null)
    {
        var sb = new StringBuilder();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return sb.ToString();

            try
            {
                using var cmd = _connection.CreateCommand();

                if (characterId == null || characterId == 0)
                {
                    sb.AppendLine("timestamp_utc,value,character_id");
                    cmd.CommandText = @"SELECT p.timestamp, p.value, s.character_id FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable = $v
                        ORDER BY p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$v", variable);

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
                    sb.AppendLine("timestamp_utc,value");
                    cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable = $v AND s.character_id = $c
                        ORDER BY p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$v", variable);
                    cmd.Parameters.AddWithValue("$c", (long)characterId);

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var ticks = reader.GetInt64(0);
                        var value = reader.GetInt64(1);
                        sb.AppendLine($"{new DateTime(ticks, DateTimeKind.Utc):O},{value}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] ExportToCsv failed: {ex.Message}", ex);
            }
        }

        return sb.ToString();
    }

}
