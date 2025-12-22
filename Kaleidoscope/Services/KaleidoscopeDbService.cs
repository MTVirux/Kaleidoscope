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

CREATE TABLE IF NOT EXISTS retainer_crystals (
    character_id INTEGER NOT NULL,
    retainer_id INTEGER NOT NULL,
    retainer_name TEXT,
    element INTEGER NOT NULL,
    tier INTEGER NOT NULL,
    count INTEGER NOT NULL DEFAULT 0,
    updated_at INTEGER NOT NULL,
    PRIMARY KEY (character_id, retainer_id, element, tier)
);

CREATE TABLE IF NOT EXISTS inventory_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    character_id INTEGER NOT NULL,
    source_type INTEGER NOT NULL,
    retainer_id INTEGER NOT NULL DEFAULT 0,
    name TEXT,
    world TEXT,
    gil INTEGER NOT NULL DEFAULT 0,
    updated_at INTEGER NOT NULL,
    UNIQUE (character_id, source_type, retainer_id)
);

CREATE TABLE IF NOT EXISTS inventory_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    cache_id INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    quantity INTEGER NOT NULL,
    is_hq INTEGER NOT NULL DEFAULT 0,
    is_collectable INTEGER NOT NULL DEFAULT 0,
    slot INTEGER NOT NULL,
    container_type INTEGER NOT NULL,
    spiritbond INTEGER NOT NULL DEFAULT 0,
    condition INTEGER NOT NULL DEFAULT 0,
    glamour_id INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(cache_id) REFERENCES inventory_cache(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_series_variable_character ON series(variable, character_id);
CREATE INDEX IF NOT EXISTS idx_points_series_timestamp ON points(series_id, timestamp);
CREATE INDEX IF NOT EXISTS idx_retainer_crystals_char ON retainer_crystals(character_id);
CREATE INDEX IF NOT EXISTS idx_inventory_cache_char ON inventory_cache(character_id);
CREATE INDEX IF NOT EXISTS idx_inventory_cache_lookup ON inventory_cache(character_id, source_type, retainer_id);
CREATE INDEX IF NOT EXISTS idx_inventory_items_cache ON inventory_items(cache_id);
CREATE INDEX IF NOT EXISTS idx_inventory_items_item ON inventory_items(item_id);
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
    /// Clears all data from all tables (points, series, character_names, and retainer_crystals).
    /// </summary>
    public bool ClearAllTables()
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                
                cmd.CommandText = "DELETE FROM points";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM series";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM character_names";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM retainer_crystals";
                cmd.ExecuteNonQuery();

                LogService.Info("[KaleidoscopeDb] Cleared all data from all tables");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] ClearAllTables failed: {ex.Message}", ex);
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
                    try { tx.Rollback(); } 
                    catch (Exception rollbackEx) { LogService.Debug($"[KaleidoscopeDb] Rollback also failed: {rollbackEx.Message}"); }
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

    #region Retainer Crystal Cache

    /// <summary>
    /// Saves or updates the cached crystal count for a specific retainer.
    /// </summary>
    public void SaveRetainerCrystals(ulong characterId, ulong retainerId, string? retainerName, int element, int tier, long count)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"INSERT OR REPLACE INTO retainer_crystals 
                    (character_id, retainer_id, retainer_name, element, tier, count, updated_at)
                    VALUES ($cid, $rid, $name, $elem, $tier, $count, $time)";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.Parameters.AddWithValue("$rid", (long)retainerId);
                cmd.Parameters.AddWithValue("$name", retainerName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$elem", element);
                cmd.Parameters.AddWithValue("$tier", tier);
                cmd.Parameters.AddWithValue("$count", count);
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] SaveRetainerCrystals failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets all cached retainer crystal counts for a character.
    /// Returns list of (retainerId, retainerName, element, tier, count).
    /// </summary>
    public List<(ulong retainerId, string? retainerName, int element, int tier, long count)> GetRetainerCrystals(ulong characterId)
    {
        var result = new List<(ulong retainerId, string? retainerName, int element, int tier, long count)>();

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT retainer_id, retainer_name, element, tier, count 
                    FROM retainer_crystals WHERE character_id = $cid";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var retainerId = (ulong)reader.GetInt64(0);
                    var retainerName = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var element = reader.GetInt32(2);
                    var tier = reader.GetInt32(3);
                    var count = reader.GetInt64(4);
                    result.Add((retainerId, retainerName, element, tier, count));
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetRetainerCrystals failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets total cached crystal count for a character across all retainers for a specific element and tier.
    /// </summary>
    public long GetTotalRetainerCrystals(ulong characterId, int element, int tier)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT COALESCE(SUM(count), 0) FROM retainer_crystals 
                    WHERE character_id = $cid AND element = $elem AND tier = $tier";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.Parameters.AddWithValue("$elem", element);
                cmd.Parameters.AddWithValue("$tier", tier);

                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? (long)result : 0;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetTotalRetainerCrystals failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Clears all cached retainer crystal data for a character.
    /// </summary>
    public void ClearRetainerCrystals(ulong characterId)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM retainer_crystals WHERE character_id = $cid";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] ClearRetainerCrystals failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Clears all cached retainer crystal data.
    /// </summary>
    public void ClearAllRetainerCrystals()
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM retainer_crystals";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] ClearAllRetainerCrystals failed: {ex.Message}", ex);
            }
        }
    }

    #endregion

    #region Inventory Cache Operations

    /// <summary>
    /// Saves or updates an inventory cache entry and its items.
    /// Replaces all existing items for this cache.
    /// </summary>
    public void SaveInventoryCache(Models.Inventory.InventoryCacheEntry entry)
    {
        if (entry == null) return;

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var transaction = _connection.BeginTransaction();

                try
                {
                    // Upsert the cache entry
                    using var cacheCmd = _connection.CreateCommand();
                    cacheCmd.Transaction = transaction;
                    cacheCmd.CommandText = @"
                        INSERT INTO inventory_cache (character_id, source_type, retainer_id, name, world, gil, updated_at)
                        VALUES ($cid, $type, $rid, $name, $world, $gil, $time)
                        ON CONFLICT(character_id, source_type, retainer_id) DO UPDATE SET
                            name = excluded.name,
                            world = excluded.world,
                            gil = excluded.gil,
                            updated_at = excluded.updated_at
                        RETURNING id";
                    cacheCmd.Parameters.AddWithValue("$cid", (long)entry.CharacterId);
                    cacheCmd.Parameters.AddWithValue("$type", (int)entry.SourceType);
                    cacheCmd.Parameters.AddWithValue("$rid", (long)entry.RetainerId);
                    cacheCmd.Parameters.AddWithValue("$name", entry.Name ?? (object)DBNull.Value);
                    cacheCmd.Parameters.AddWithValue("$world", entry.World ?? (object)DBNull.Value);
                    cacheCmd.Parameters.AddWithValue("$gil", entry.Gil);
                    cacheCmd.Parameters.AddWithValue("$time", entry.UpdatedAt.Ticks);

                    var cacheId = (long)cacheCmd.ExecuteScalar()!;

                    // Delete existing items for this cache
                    using var deleteCmd = _connection.CreateCommand();
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM inventory_items WHERE cache_id = $id";
                    deleteCmd.Parameters.AddWithValue("$id", cacheId);
                    deleteCmd.ExecuteNonQuery();

                    // Insert new items
                    if (entry.Items.Count > 0)
                    {
                        using var itemCmd = _connection.CreateCommand();
                        itemCmd.Transaction = transaction;
                        itemCmd.CommandText = @"
                            INSERT INTO inventory_items 
                            (cache_id, item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id)
                            VALUES ($cid, $iid, $qty, $hq, $col, $slot, $cont, $sb, $cond, $glam)";

                        var cidParam = itemCmd.Parameters.Add("$cid", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var iidParam = itemCmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var qtyParam = itemCmd.Parameters.Add("$qty", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var hqParam = itemCmd.Parameters.Add("$hq", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var colParam = itemCmd.Parameters.Add("$col", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var slotParam = itemCmd.Parameters.Add("$slot", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var contParam = itemCmd.Parameters.Add("$cont", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var sbParam = itemCmd.Parameters.Add("$sb", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var condParam = itemCmd.Parameters.Add("$cond", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var glamParam = itemCmd.Parameters.Add("$glam", Microsoft.Data.Sqlite.SqliteType.Integer);

                        cidParam.Value = cacheId;

                        foreach (var item in entry.Items)
                        {
                            iidParam.Value = (long)item.ItemId;
                            qtyParam.Value = item.Quantity;
                            hqParam.Value = item.IsHq ? 1 : 0;
                            colParam.Value = item.IsCollectable ? 1 : 0;
                            slotParam.Value = item.Slot;
                            contParam.Value = (long)item.ContainerType;
                            sbParam.Value = item.SpiritbondOrCollectability;
                            condParam.Value = item.Condition;
                            glamParam.Value = (long)item.GlamourId;
                            itemCmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                    LogService.Debug($"[KaleidoscopeDb] Saved inventory cache for {entry.SourceType} {entry.Name}: {entry.Items.Count} items");
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] SaveInventoryCache failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets a specific inventory cache entry by character ID and source.
    /// For players, use retainerId = 0.
    /// </summary>
    public Models.Inventory.InventoryCacheEntry? GetInventoryCache(
        ulong characterId, 
        Models.Inventory.InventorySourceType sourceType, 
        ulong retainerId = 0)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return null;

            try
            {
                // Get the cache entry
                using var cacheCmd = _connection.CreateCommand();
                cacheCmd.CommandText = @"
                    SELECT id, name, world, gil, updated_at 
                    FROM inventory_cache 
                    WHERE character_id = $cid AND source_type = $type AND retainer_id = $rid";
                cacheCmd.Parameters.AddWithValue("$cid", (long)characterId);
                cacheCmd.Parameters.AddWithValue("$type", (int)sourceType);
                cacheCmd.Parameters.AddWithValue("$rid", (long)retainerId);

                using var reader = cacheCmd.ExecuteReader();
                if (!reader.Read()) return null;

                var cacheId = reader.GetInt64(0);
                var entry = new Models.Inventory.InventoryCacheEntry
                {
                    CharacterId = characterId,
                    SourceType = sourceType,
                    RetainerId = retainerId,
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    World = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Gil = reader.GetInt64(3),
                    UpdatedAt = new DateTime(reader.GetInt64(4), DateTimeKind.Utc)
                };
                reader.Close();

                // Get the items
                using var itemCmd = _connection.CreateCommand();
                itemCmd.CommandText = @"
                    SELECT item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id
                    FROM inventory_items
                    WHERE cache_id = $id";
                itemCmd.Parameters.AddWithValue("$id", cacheId);

                using var itemReader = itemCmd.ExecuteReader();
                while (itemReader.Read())
                {
                    entry.Items.Add(new Models.Inventory.InventoryItemSnapshot
                    {
                        ItemId = (uint)itemReader.GetInt64(0),
                        Quantity = itemReader.GetInt32(1),
                        IsHq = itemReader.GetInt32(2) != 0,
                        IsCollectable = itemReader.GetInt32(3) != 0,
                        Slot = (short)itemReader.GetInt32(4),
                        ContainerType = (uint)itemReader.GetInt64(5),
                        SpiritbondOrCollectability = (ushort)itemReader.GetInt32(6),
                        Condition = (ushort)itemReader.GetInt32(7),
                        GlamourId = (uint)itemReader.GetInt64(8)
                    });
                }

                return entry;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetInventoryCache failed: {ex.Message}", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Gets all inventory cache entries for a character (player + all retainers).
    /// </summary>
    public List<Models.Inventory.InventoryCacheEntry> GetAllInventoryCaches(ulong characterId)
    {
        var result = new List<Models.Inventory.InventoryCacheEntry>();

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                // Get all cache entries for this character
                using var cacheCmd = _connection.CreateCommand();
                cacheCmd.CommandText = @"
                    SELECT id, source_type, retainer_id, name, world, gil, updated_at 
                    FROM inventory_cache 
                    WHERE character_id = $cid
                    ORDER BY source_type, retainer_id";
                cacheCmd.Parameters.AddWithValue("$cid", (long)characterId);

                var cacheEntries = new List<(long id, Models.Inventory.InventoryCacheEntry entry)>();
                using (var reader = cacheCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var cacheId = reader.GetInt64(0);
                        var entry = new Models.Inventory.InventoryCacheEntry
                        {
                            CharacterId = characterId,
                            SourceType = (Models.Inventory.InventorySourceType)reader.GetInt32(1),
                            RetainerId = (ulong)reader.GetInt64(2),
                            Name = reader.IsDBNull(3) ? null : reader.GetString(3),
                            World = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Gil = reader.GetInt64(5),
                            UpdatedAt = new DateTime(reader.GetInt64(6), DateTimeKind.Utc)
                        };
                        cacheEntries.Add((cacheId, entry));
                    }
                }

                // Get items for each cache entry
                foreach (var (cacheId, entry) in cacheEntries)
                {
                    using var itemCmd = _connection.CreateCommand();
                    itemCmd.CommandText = @"
                        SELECT item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id
                        FROM inventory_items
                        WHERE cache_id = $id";
                    itemCmd.Parameters.AddWithValue("$id", cacheId);

                    using var itemReader = itemCmd.ExecuteReader();
                    while (itemReader.Read())
                    {
                        entry.Items.Add(new Models.Inventory.InventoryItemSnapshot
                        {
                            ItemId = (uint)itemReader.GetInt64(0),
                            Quantity = itemReader.GetInt32(1),
                            IsHq = itemReader.GetInt32(2) != 0,
                            IsCollectable = itemReader.GetInt32(3) != 0,
                            Slot = (short)itemReader.GetInt32(4),
                            ContainerType = (uint)itemReader.GetInt64(5),
                            SpiritbondOrCollectability = (ushort)itemReader.GetInt32(6),
                            Condition = (ushort)itemReader.GetInt32(7),
                            GlamourId = (uint)itemReader.GetInt64(8)
                        });
                    }

                    result.Add(entry);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetAllInventoryCaches failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all inventory cache entries across all characters.
    /// </summary>
    public List<Models.Inventory.InventoryCacheEntry> GetAllInventoryCachesAllCharacters()
    {
        var result = new List<Models.Inventory.InventoryCacheEntry>();

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                // Get all cache entries
                using var cacheCmd = _connection.CreateCommand();
                cacheCmd.CommandText = @"
                    SELECT id, character_id, source_type, retainer_id, name, world, gil, updated_at 
                    FROM inventory_cache 
                    ORDER BY character_id, source_type, retainer_id";

                var cacheEntries = new List<(long id, Models.Inventory.InventoryCacheEntry entry)>();
                using (var reader = cacheCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var cacheId = reader.GetInt64(0);
                        var entry = new Models.Inventory.InventoryCacheEntry
                        {
                            CharacterId = (ulong)reader.GetInt64(1),
                            SourceType = (Models.Inventory.InventorySourceType)reader.GetInt32(2),
                            RetainerId = (ulong)reader.GetInt64(3),
                            Name = reader.IsDBNull(4) ? null : reader.GetString(4),
                            World = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Gil = reader.GetInt64(6),
                            UpdatedAt = new DateTime(reader.GetInt64(7), DateTimeKind.Utc)
                        };
                        cacheEntries.Add((cacheId, entry));
                    }
                }

                // Get items for each cache entry
                foreach (var (cacheId, entry) in cacheEntries)
                {
                    using var itemCmd = _connection.CreateCommand();
                    itemCmd.CommandText = @"
                        SELECT item_id, quantity, is_hq, is_collectable, slot, container_type, spiritbond, condition, glamour_id
                        FROM inventory_items
                        WHERE cache_id = $id";
                    itemCmd.Parameters.AddWithValue("$id", cacheId);

                    using var itemReader = itemCmd.ExecuteReader();
                    while (itemReader.Read())
                    {
                        entry.Items.Add(new Models.Inventory.InventoryItemSnapshot
                        {
                            ItemId = (uint)itemReader.GetInt64(0),
                            Quantity = itemReader.GetInt32(1),
                            IsHq = itemReader.GetInt32(2) != 0,
                            IsCollectable = itemReader.GetInt32(3) != 0,
                            Slot = (short)itemReader.GetInt32(4),
                            ContainerType = (uint)itemReader.GetInt64(5),
                            SpiritbondOrCollectability = (ushort)itemReader.GetInt32(6),
                            Condition = (ushort)itemReader.GetInt32(7),
                            GlamourId = (uint)itemReader.GetInt64(8)
                        });
                    }

                    result.Add(entry);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetAllInventoryCachesAllCharacters failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Deletes an inventory cache entry and its items.
    /// </summary>
    public void DeleteInventoryCache(ulong characterId, Models.Inventory.InventorySourceType sourceType, ulong retainerId = 0)
    {
        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"DELETE FROM inventory_cache 
                    WHERE character_id = $cid AND source_type = $type AND retainer_id = $rid";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.Parameters.AddWithValue("$type", (int)sourceType);
                cmd.Parameters.AddWithValue("$rid", (long)retainerId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] DeleteInventoryCache failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets a summary of item counts across all caches (for a specific item or all items).
    /// Returns dictionary of itemId -> total quantity across all caches.
    /// </summary>
    public Dictionary<uint, long> GetItemCountSummary(ulong? characterId = null, uint? itemId = null)
    {
        var result = new Dictionary<uint, long>();

        lock (_lock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                var sql = @"
                    SELECT ii.item_id, SUM(ii.quantity) as total
                    FROM inventory_items ii
                    JOIN inventory_cache ic ON ii.cache_id = ic.id
                    WHERE 1=1";

                if (characterId.HasValue)
                {
                    sql += " AND ic.character_id = $cid";
                    cmd.Parameters.AddWithValue("$cid", (long)characterId.Value);
                }
                if (itemId.HasValue)
                {
                    sql += " AND ii.item_id = $iid";
                    cmd.Parameters.AddWithValue("$iid", (long)itemId.Value);
                }

                sql += " GROUP BY ii.item_id";
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = (uint)reader.GetInt64(0);
                    var total = reader.GetInt64(1);
                    result[id] = total;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetItemCountSummary failed: {ex.Message}", ex);
            }
        }

        return result;
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
