using Dalamud.Plugin.Services;
using Microsoft.Data.Sqlite;

namespace Kaleidoscope.Services;

/// <summary>
/// Background service that periodically samples game data (e.g., gil) and persists it to a database.
/// </summary>
public class SamplerService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly FilenameService _filenames;
    private readonly ConfigurationService _configService;

    private Timer? _timer;
    private volatile bool _enabled = true;
    private int _intervalSeconds = 1;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public int IntervalMs
    {
        get => _intervalSeconds * 1000;
        set
        {
            if (value <= 0) value = 1000;
            var sec = (value + 999) / 1000;
            _intervalSeconds = sec;
            _timer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_intervalSeconds));
        }
    }

    public SamplerService(IPluginLog log, FilenameService filenames, ConfigurationService configService)
    {
        _log = log;
        _filenames = filenames;
        _configService = configService;

        // Load initial values from config
        _enabled = configService.SamplerConfig.SamplerEnabled;
        _intervalSeconds = Math.Max(1, configService.SamplerConfig.SamplerIntervalMs / 1000);

        StartTimer();
    }

    private void StartTimer()
    {
        _timer = new Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(_intervalSeconds));
    }

    private void OnTimerTick(object? state)
    {
        if (!_enabled) return;

        try
        {
            var cid = GameStateService.PlayerContentId;
            if (cid == 0) return;

            uint gil = 0;
            unsafe
            {
                var im = GameStateService.InventoryManagerInstance();
                if (im != null) gil = im->GetGil();
                var cm = GameStateService.CurrencyManagerInstance();
                if (gil == 0 && cm != null)
                {
                    try { gil = cm->GetItemCount(1); } catch { gil = 0; }
                }
            }

            PersistSample(cid, gil);
        }
        catch (Exception ex)
        {
            _log.Error($"Sampler tick error: {ex.Message}");
        }
    }

    private void PersistSample(ulong characterId, uint gil)
    {
        var dbPath = _filenames.GilTrackerDbPath;
        if (string.IsNullOrEmpty(dbPath)) return;

        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            EnsureSchema(conn);

            var seriesId = GetOrCreateSeries(conn, "Gil", (long)characterId);
            if (ShouldInsert(conn, seriesId, (long)gil))
            {
                InsertPoint(conn, seriesId, (long)gil);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Sampler persist error: {ex.Message}");
        }
    }

    private static void EnsureSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS series (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    variable TEXT NOT NULL,
    character_id INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS points (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    series_id INTEGER NOT NULL,
    timestamp INTEGER NOT NULL,
    value INTEGER NOT NULL,
    FOREIGN KEY(series_id) REFERENCES series(id)
);

CREATE INDEX IF NOT EXISTS idx_series_variable_character ON series(variable, character_id);
CREATE INDEX IF NOT EXISTS idx_points_series_timestamp ON points(series_id, timestamp);
";
        cmd.ExecuteNonQuery();
    }

    private static long GetOrCreateSeries(SqliteConnection conn, string variable, long characterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM series WHERE variable = $v AND character_id = $c LIMIT 1";
        cmd.Parameters.AddWithValue("$v", variable);
        cmd.Parameters.AddWithValue("$c", characterId);
        var result = cmd.ExecuteScalar();

        if (result != null && result != DBNull.Value)
            return (long)result;

        cmd.CommandText = "INSERT INTO series(variable, character_id) VALUES($v, $c); SELECT last_insert_rowid();";
        return (long)cmd.ExecuteScalar()!;
    }

    private static bool ShouldInsert(SqliteConnection conn, long seriesId, long value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM points WHERE series_id = $s ORDER BY timestamp DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$s", seriesId);
        var lastValObj = cmd.ExecuteScalar();

        if (lastValObj == null || lastValObj == DBNull.Value)
            return true;

        try
        {
            var lastVal = (long)lastValObj;
            return lastVal != value;
        }
        catch
        {
            return true;
        }
    }

    private static void InsertPoint(SqliteConnection conn, long seriesId, long value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO points(series_id, timestamp, value) VALUES($s, $t, $v)";
        cmd.Parameters.AddWithValue("$s", seriesId);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.Ticks);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Gets whether the database exists and is accessible.
    /// </summary>
    public bool HasDb
    {
        get
        {
            var path = _filenames.GilTrackerDbPath;
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }
    }

    /// <summary>
    /// Clears all data from the database.
    /// </summary>
    public void ClearAllData()
    {
        var path = _filenames.GilTrackerDbPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM points; DELETE FROM series;";
            cmd.ExecuteNonQuery();

            _log.Information("Cleared all GilTracker data");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to clear data: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes data for characters without a name association in the character_names table.
    /// </summary>
    public int CleanUnassociatedCharacters()
    {
        var path = _filenames.GilTrackerDbPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;

        try
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            // Get series IDs for characters that don't have a name association
            using var selectCmd = conn.CreateCommand();
            selectCmd.CommandText = @"
SELECT s.id FROM series s
LEFT JOIN character_names cn ON s.character_id = cn.character_id
WHERE cn.character_id IS NULL";
            var seriesIds = new List<long>();
            using (var reader = selectCmd.ExecuteReader())
            {
                while (reader.Read())
                    seriesIds.Add(reader.GetInt64(0));
            }

            if (seriesIds.Count == 0) return 0;

            // Delete points and series
            foreach (var sid in seriesIds)
            {
                using var delPoints = conn.CreateCommand();
                delPoints.CommandText = "DELETE FROM points WHERE series_id = $s";
                delPoints.Parameters.AddWithValue("$s", sid);
                delPoints.ExecuteNonQuery();

                using var delSeries = conn.CreateCommand();
                delSeries.CommandText = "DELETE FROM series WHERE id = $s";
                delSeries.Parameters.AddWithValue("$s", sid);
                delSeries.ExecuteNonQuery();
            }

            _log.Information($"Cleaned {seriesIds.Count} unassociated character series");
            return seriesIds.Count;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to clean unassociated characters: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Exports all data to a CSV file.
    /// </summary>
    public string? ExportCsv()
    {
        var dbPath = _filenames.GilTrackerDbPath;
        if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath)) return null;

        try
        {
            var csvPath = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", $"giltracker_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT s.variable, s.character_id, p.timestamp, p.value, cn.name
FROM points p
JOIN series s ON p.series_id = s.id
LEFT JOIN character_names cn ON s.character_id = cn.character_id
ORDER BY p.timestamp";

            using var writer = new StreamWriter(csvPath);
            writer.WriteLine("Variable,CharacterId,Timestamp,Value,CharacterName");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var variable = reader.GetString(0);
                var charId = reader.GetInt64(1);
                var timestamp = new DateTime(reader.GetInt64(2), DateTimeKind.Utc);
                var value = reader.GetInt64(3);
                var charName = reader.IsDBNull(4) ? "" : reader.GetString(4);

                writer.WriteLine($"{variable},{charId},{timestamp:O},{value},{charName}");
            }

            _log.Information($"Exported data to {csvPath}");
            return csvPath;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to export CSV: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        GC.SuppressFinalize(this);
    }
}
