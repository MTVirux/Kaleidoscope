using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services;

/// <summary>
/// Centralized database service for Kaleidoscope plugin data persistence.
/// Provides thread-safe access to the SQLite database for storing time-series data
/// such as gil tracking, inventory snapshots, currency tracking, and other plugin data.
/// </summary>
/// <remarks>
/// This service is intentionally not marked with IService because it is created
/// manually by SamplerService to share the database connection. If you need to use
/// this service directly, inject SamplerService and access its DbService property.
/// </remarks>
public sealed class KaleidoscopeDbService : IDisposable
{
    private readonly object _lock = new();
    private readonly string? _dbPath;
    private SqliteConnection? _connection;

    public string? DbPath => _dbPath;

    public KaleidoscopeDbService(string? dbPath)
    {
        _dbPath = dbPath;
        EnsureConnection();
    }

    #region Connection Management

    private void EnsureConnection()
    {
        if (string.IsNullOrEmpty(_dbPath)) return;

        lock (_lock)
        {
            if (_connection != null) return;

            try
            {
                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var csb = new SqliteConnectionStringBuilder
                {
                    DataSource = _dbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                };

                _connection = new SqliteConnection(csb.ToString());
                _connection.Open();

                EnsureSchema();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] Failed to initialize database: {ex.Message}", ex);
                _connection = null;
            }
        }
    }

    private void EnsureSchema()
    {
        if (_connection == null) return;

        using var cmd = _connection.CreateCommand();
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

CREATE TABLE IF NOT EXISTS character_names (
    character_id INTEGER PRIMARY KEY,
    name TEXT
);

CREATE INDEX IF NOT EXISTS idx_series_variable_character ON series(variable, character_id);
CREATE INDEX IF NOT EXISTS idx_points_series_timestamp ON points(series_id, timestamp);
";
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region Series & Points Operations

    /// <summary>
    /// Gets or creates a series ID for the given variable and character.
    /// When creating a new series, inserts an initial data point with value 0.
    /// </summary>
    public long? GetOrCreateSeries(string variable, ulong characterId)
    {
        if (string.IsNullOrEmpty(_dbPath)) return null;

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return null;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id FROM series WHERE variable = $v AND character_id = $c LIMIT 1";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                var result = cmd.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                    return (long)result;

                // Create new series
                cmd.CommandText = "INSERT INTO series(variable, character_id) VALUES($v, $c); SELECT last_insert_rowid();";
                var newSeriesId = (long)cmd.ExecuteScalar()!;

                // Insert initial 0 value for the new series
                cmd.CommandText = "INSERT INTO points(series_id, timestamp, value) VALUES($s, $t, 0)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$s", newSeriesId);
                cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.Ticks);
                cmd.ExecuteNonQuery();

                return newSeriesId;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetOrCreateSeries failed: {ex.Message}", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the last recorded value for the given series.
    /// </summary>
    public long? GetLastValue(long seriesId)
    {
        lock (_lock)
        {
            if (_connection == null) return null;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM points WHERE series_id = $s ORDER BY timestamp DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$s", seriesId);
                var result = cmd.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                    return (long)result;

                return null;
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] GetLastValue failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the last recorded value for a character directly.
    /// </summary>
    public long? GetLastValueForCharacter(string variable, ulong characterId)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return null;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT p.value FROM points p 
                    JOIN series s ON p.series_id = s.id 
                    WHERE s.variable = $v AND s.character_id = $c 
                    ORDER BY p.timestamp DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                var result = cmd.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                    return (long)result;

                return null;
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] GetLastValueForCharacter failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Inserts a new data point for the given series.
    /// </summary>
    public bool InsertPoint(long seriesId, long value, DateTime? timestamp = null)
    {
        lock (_lock)
        {
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT INTO points(series_id, timestamp, value) VALUES($s, $t, $v)";
                cmd.Parameters.AddWithValue("$s", seriesId);
                cmd.Parameters.AddWithValue("$t", (timestamp ?? DateTime.UtcNow).Ticks);
                cmd.Parameters.AddWithValue("$v", value);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] InsertPoint failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Saves a sample value, only inserting if different from the last value.
    /// Returns true if a new point was inserted.
    /// </summary>
    public bool SaveSampleIfChanged(string variable, ulong characterId, long value)
    {
        var seriesId = GetOrCreateSeries(variable, characterId);
        if (seriesId == null) return false;

        var lastValue = GetLastValue(seriesId.Value);
        if (lastValue.HasValue && lastValue.Value == value)
            return false;

        return InsertPoint(seriesId.Value, value);
    }

    /// <summary>
    /// Gets all points for a character, optionally limited.
    /// </summary>
    public List<(DateTime timestamp, long value)> GetPoints(string variable, ulong characterId, int? limit = null)
    {
        var result = new List<(DateTime, long)>();

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                var limitClause = limit.HasValue ? $" LIMIT {limit.Value}" : "";
                cmd.CommandText = $@"SELECT p.timestamp, p.value FROM points p
                    JOIN series s ON p.series_id = s.id
                    WHERE s.variable = $v AND s.character_id = $c
                    ORDER BY p.timestamp ASC{limitClause}";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$c", (long)characterId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var ticks = reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    result.Add((new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] GetPoints failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all points across all characters for a variable.
    /// </summary>
    public List<(ulong characterId, DateTime timestamp, long value)> GetAllPoints(string variable)
    {
        var result = new List<(ulong, DateTime, long)>();

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT s.character_id, p.timestamp, p.value FROM points p
                    JOIN series s ON p.series_id = s.id
                    WHERE s.variable = $v
                    ORDER BY p.timestamp ASC";
                cmd.Parameters.AddWithValue("$v", variable);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var charId = (ulong)reader.GetInt64(0);
                    var ticks = reader.GetInt64(1);
                    var value = reader.GetInt64(2);
                    if (charId != 0)
                        result.Add((charId, new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] GetAllPoints failed: {ex.Message}");
            }
        }

        return result;
    }

    #endregion

    #region Character Operations

    /// <summary>
    /// Gets all character IDs that have data for a variable.
    /// </summary>
    public List<ulong> GetAvailableCharacters(string variable)
    {
        var result = new List<ulong>();

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT character_id FROM series WHERE variable = $v ORDER BY character_id";
                cmd.Parameters.AddWithValue("$v", variable);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cid = reader.GetInt64(0);
                    if (cid != 0)
                        result.Add((ulong)cid);
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] GetAvailableCharacters failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Saves or updates a character name mapping.
    /// </summary>
    public bool SaveCharacterName(ulong characterId, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO character_names(character_id, name) VALUES($c, $n)";
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] SaveCharacterName failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the stored name for a character.
    /// </summary>
    public string? GetCharacterName(ulong characterId)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return null;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT name FROM character_names WHERE character_id = $c LIMIT 1";
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                var result = cmd.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                    return result as string;

                return null;
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] GetCharacterName failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Gets all stored character name mappings.
    /// </summary>
    public List<(ulong characterId, string? name)> GetAllCharacterNames()
    {
        var result = new List<(ulong, string?)>();

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT character_id, name FROM character_names ORDER BY character_id";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cid = reader.GetInt64(0);
                    var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (cid != 0)
                        result.Add(((ulong)cid, name));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] GetAllCharacterNames failed: {ex.Message}");
            }
        }

        return result;
    }

    #endregion

    #region Data Management

    /// <summary>
    /// Clears all data for a specific character and variable.
    /// </summary>
    public bool ClearCharacterData(string variable, ulong characterId)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable = $v AND character_id = $c)";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM series WHERE variable = $v AND character_id = $c";
                cmd.ExecuteNonQuery();

                return true;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] ClearCharacterData failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Clears all data for a variable across all characters.
    /// </summary>
    public bool ClearAllData(string variable)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable = $v)";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM series WHERE variable = $v";
                cmd.ExecuteNonQuery();

                LogService.Info($"[KaleidoscopeDb] Cleared all data for variable '{variable}'");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] ClearAllData failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Removes data for characters that don't have a name association.
    /// Returns the number of characters removed.
    /// </summary>
    public int CleanUnassociatedCharacters(string variable)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                // Find character IDs with data but no name
                var idsToRemove = new List<long>();
                using (var selectCmd = _connection.CreateCommand())
                {
                    selectCmd.CommandText = @"SELECT DISTINCT character_id FROM series 
                        WHERE variable = $v 
                        AND character_id NOT IN (SELECT character_id FROM character_names)";
                    selectCmd.Parameters.AddWithValue("$v", variable);

                    using var reader = selectCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var cid = reader.GetInt64(0);
                        if (cid != 0) idsToRemove.Add(cid);
                    }
                }

                if (idsToRemove.Count == 0) return 0;

                using var tx = _connection.BeginTransaction();
                try
                {
                    foreach (var cid in idsToRemove)
                    {
                        using var cmd = _connection.CreateCommand();
                        cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable = $v AND character_id = $c)";
                        cmd.Parameters.AddWithValue("$v", variable);
                        cmd.Parameters.AddWithValue("$c", cid);
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "DELETE FROM series WHERE variable = $v AND character_id = $c";
                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                    LogService.Info($"[KaleidoscopeDb] Cleaned {idsToRemove.Count} unassociated characters");
                    return idsToRemove.Count;
                }
                catch (Exception ex)
                {
                    LogService.Error($"[KaleidoscopeDb] Transaction failed: {ex.Message}", ex);
                    try { tx.Rollback(); } catch { }
                    return 0;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] CleanUnassociatedCharacters failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Migrates stored names to clean format (removes "You (Name)" wrappers, etc.).
    /// </summary>
    public void MigrateStoredNames()
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT character_id, name FROM character_names";
                var updates = new List<(long cid, string newName)>();
                var deletes = new List<long>();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var cid = reader.GetInt64(0);
                        var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        var sanitized = SanitizeName(name);

                        // If the stored name contains any digit, treat it as invalid
                        if (!string.IsNullOrEmpty(sanitized) && sanitized.Any(char.IsDigit))
                        {
                            deletes.Add(cid);
                            continue;
                        }

                        // If the stored name sanitizes to just "You", treat it as a placeholder
                        if (!string.IsNullOrEmpty(sanitized) && string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
                        {
                            deletes.Add(cid);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(sanitized) && !string.Equals(sanitized, name, StringComparison.Ordinal))
                        {
                            updates.Add((cid, sanitized));
                        }
                    }
                }

                foreach (var (cid, newName) in updates)
                {
                    try
                    {
                        using var updateCmd = _connection.CreateCommand();
                        updateCmd.CommandText = "UPDATE character_names SET name = $n WHERE character_id = $c";
                        updateCmd.Parameters.AddWithValue("$n", newName);
                        updateCmd.Parameters.AddWithValue("$c", cid);
                        updateCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug($"[KaleidoscopeDb] Name update failed for CID {cid}: {ex.Message}");
                    }
                }

                foreach (var cid in deletes)
                {
                    try
                    {
                        using var deleteCmd = _connection.CreateCommand();
                        deleteCmd.CommandText = "DELETE FROM character_names WHERE character_id = $c";
                        deleteCmd.Parameters.AddWithValue("$c", cid);
                        deleteCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug($"[KaleidoscopeDb] Name delete failed for CID {cid}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] MigrateStoredNames failed: {ex.Message}");
            }
        }
    }

    private static string? SanitizeName(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        try
        {
            var s = raw.Trim();

            // Look for patterns like "You (Name)" and extract the inner name
            var idxOpen = s.IndexOf('(');
            var idxClose = s.LastIndexOf(')');
            if (idxOpen >= 0 && idxClose > idxOpen)
            {
                var inner = s.Substring(idxOpen + 1, idxClose - idxOpen - 1).Trim();
                if (!string.IsNullOrEmpty(inner)) return inner;
            }

            // If it starts with "You " then strip that prefix
            if (s.StartsWith("You ", StringComparison.OrdinalIgnoreCase))
            {
                var rem = s.Substring(4).Trim();
                if (!string.IsNullOrEmpty(rem)) return rem;
            }

            return s;
        }
        catch
        {
            return raw;
        }
    }

    #endregion

    #region Export

    /// <summary>
    /// Exports data to a CSV string.
    /// </summary>
    public string ExportToCsv(string variable, ulong? characterId = null)
    {
        var sb = new StringBuilder();

        lock (_lock)
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
                LogService.Error($"[KaleidoscopeDb] ExportToCsv failed: {ex.Message}", ex);
            }
        }

        return sb.ToString();
    }

    #endregion

    public void Dispose()
    {
        lock (_lock)
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
        GC.SuppressFinalize(this);
    }
}
