using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Gets or creates a series ID for the given variable and character.
    /// When creating a new series, inserts an initial data point with value 0.
    /// </summary>
    public long? GetOrCreateSeries(string variable, ulong characterId)
    {
        if (string.IsNullOrEmpty(_dbPath)) return null;

        lock (_writeLock)
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
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] GetOrCreateSeries failed: {ex.Message}", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the last recorded value for the given series.
    /// </summary>
    public long? GetLastValue(long seriesId)
    {
        lock (_writeLock)
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
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetLastValue failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the last recorded value for a character directly.
    /// </summary>
    public long? GetLastValueForCharacter(string variable, ulong characterId)
    {
        lock (_writeLock)
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
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetLastValueForCharacter failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the last recorded point (timestamp and value) for a character.
    /// Used to ensure the cache always has the most recent data point regardless of time window.
    /// </summary>
    public (DateTime timestamp, long value)? GetLastPointForCharacter(string variable, ulong characterId)
    {
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return null;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p 
                    JOIN series s ON p.series_id = s.id 
                    WHERE s.variable = $v AND s.character_id = $c 
                    ORDER BY p.timestamp DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$c", (long)characterId);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var ticks = reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    return (new DateTime(ticks, DateTimeKind.Utc), value);
                }

                return null;
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetLastPointForCharacter failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Gets the latest recorded value for each character for a given variable.
    /// Much more efficient than GetAllPointsBatch when only the current values are needed.
    /// Uses a single optimized SQL query with window functions to avoid fetching full history.
    /// </summary>
    /// <param name="variable">The variable name to query (e.g., "Gil")</param>
    /// <returns>Dictionary mapping characterId to their latest value</returns>
    public Dictionary<ulong, long> GetLatestValuesForVariable(string variable)
    {
        var result = new Dictionary<ulong, long>();

        lock (_readLock)
        {
            // Fall back to write connection if read connection not available
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                // Optimized query: use GROUP BY with MAX to get only the latest point per character
                // This avoids fetching and sorting the entire history
                cmd.CommandText = @"
                    SELECT s.character_id, p.value
                    FROM points p
                    JOIN series s ON p.series_id = s.id
                    WHERE s.variable = $var
                      AND p.timestamp = (
                          SELECT MAX(p2.timestamp) 
                          FROM points p2 
                          WHERE p2.series_id = s.id
                      )";
                cmd.Parameters.AddWithValue("$var", variable);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var charId = (ulong)reader.GetInt64(0);
                    var value = reader.GetInt64(1);
                    
                    if (charId == 0) continue;
                    result[charId] = value;
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetLatestValuesForVariable failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Inserts a new data point for the given series.
    /// </summary>
    public bool InsertPoint(long seriesId, long value, DateTime? timestamp = null)
    {
        lock (_writeLock)
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
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] InsertPoint failed: {ex.Message}", ex);
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

        lock (_writeLock)
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
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetPoints failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Gets points for a character filtered by time.
    /// Used for cache population on startup.
    /// </summary>
    public List<(DateTime timestamp, long value)> GetPointsSince(string variable, ulong characterId, DateTime since)
    {
        var result = new List<(DateTime, long)>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
                    JOIN series s ON p.series_id = s.id
                    WHERE s.variable = $v AND s.character_id = $c AND p.timestamp >= $since
                    ORDER BY p.timestamp ASC";
                cmd.Parameters.AddWithValue("$v", variable);
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.Parameters.AddWithValue("$since", since.Ticks);

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
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetPointsSince failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all points across all characters for a variable.
    /// Uses the read connection for better concurrent performance.
    /// </summary>
    public List<(ulong characterId, DateTime timestamp, long value)> GetAllPoints(string variable)
    {
        var result = new List<(ulong, DateTime, long)>();

        lock (_readLock)
        {
            // Fall back to write connection if read connection not available
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
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
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetAllPoints failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all points for multiple variables in a single batch query.
    /// More efficient than calling GetAllPoints multiple times.
    /// Always includes the latest point for each series regardless of time filter.
    /// </summary>
    /// <param name="variablePrefix">Variable name prefix to match (e.g., "Crystal_" to get all crystal variables)</param>
    /// <param name="since">Optional: only get points after this timestamp (latest point per series always included)</param>
    /// <returns>Dictionary keyed by variable name, containing list of (characterId, timestamp, value) tuples</returns>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetAllPointsBatch(string variablePrefix, DateTime? since = null)
    {
        var result = new Dictionary<string, List<(ulong, DateTime, long)>>();

        lock (_readLock)
        {
            // Fall back to write connection if read connection not available
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                
                if (since.HasValue)
                {
                    // Optimized query: First compute max timestamp per series in a CTE,
                    // then fetch points >= since OR at max timestamp in single pass
                    cmd.CommandText = @"
                        WITH series_max AS (
                            SELECT s.id AS series_id, s.variable, s.character_id, MAX(p.timestamp) AS max_ts
                            FROM series s
                            JOIN points p ON p.series_id = s.id
                            WHERE s.variable LIKE $prefix
                            GROUP BY s.id
                        )
                        SELECT sm.variable, sm.character_id, p.timestamp, p.value
                        FROM series_max sm
                        JOIN points p ON p.series_id = sm.series_id
                        WHERE p.timestamp >= $since OR p.timestamp = sm.max_ts
                        ORDER BY sm.variable, p.timestamp";
                    cmd.Parameters.AddWithValue("$prefix", variablePrefix + "%");
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }
                else
                {
                    cmd.CommandText = @"SELECT s.variable, s.character_id, p.timestamp, p.value FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable LIKE $prefix
                        ORDER BY s.variable, p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$prefix", variablePrefix + "%");
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var variable = reader.GetString(0);
                    var charId = (ulong)reader.GetInt64(1);
                    var ticks = reader.GetInt64(2);
                    var value = reader.GetInt64(3);
                    
                    if (charId == 0) continue;
                    
                    if (!result.TryGetValue(variable, out var list))
                    {
                        list = new List<(ulong, DateTime, long)>();
                        result[variable] = list;
                    }
                    list.Add((charId, new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetAllPointsBatch failed: {ex.Message}");
            }
        }

        return result;
    }
    
    /// <summary>
    /// Gets all points for variables matching both a prefix and suffix pattern.
    /// More efficient than fetching all data and filtering client-side.
    /// </summary>
    /// <param name="variablePrefix">Variable name prefix to match (e.g., "ItemRetainerX_")</param>
    /// <param name="variableSuffix">Variable name suffix to match (e.g., "_1234" for item ID)</param>
    /// <param name="since">Optional: only get points after this timestamp</param>
    /// <returns>Dictionary keyed by variable name, containing list of (characterId, timestamp, value) tuples</returns>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPointsBatchWithSuffix(
        string variablePrefix, string variableSuffix, DateTime? since = null)
    {
        var result = new Dictionary<string, List<(ulong, DateTime, long)>>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                using var cmd = conn.CreateCommand();
                // Use LIKE with both prefix% and %suffix pattern via GLOB or compound WHERE
                var pattern = variablePrefix + "%" + variableSuffix;
                
                if (since.HasValue)
                {
                    cmd.CommandText = @"
                        WITH series_max AS (
                            SELECT s.id AS series_id, s.variable, s.character_id, MAX(p.timestamp) AS max_ts
                            FROM series s
                            JOIN points p ON p.series_id = s.id
                            WHERE s.variable LIKE $pattern
                            GROUP BY s.id
                        )
                        SELECT sm.variable, sm.character_id, p.timestamp, p.value
                        FROM series_max sm
                        JOIN points p ON p.series_id = sm.series_id
                        WHERE p.timestamp >= $since OR p.timestamp = sm.max_ts
                        ORDER BY sm.variable, p.timestamp";
                    cmd.Parameters.AddWithValue("$pattern", pattern);
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }
                else
                {
                    cmd.CommandText = @"SELECT s.variable, s.character_id, p.timestamp, p.value FROM points p
                        JOIN series s ON p.series_id = s.id
                        WHERE s.variable LIKE $pattern
                        ORDER BY s.variable, p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$pattern", pattern);
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var variable = reader.GetString(0);
                    var charId = (ulong)reader.GetInt64(1);
                    var ticks = reader.GetInt64(2);
                    var value = reader.GetInt64(3);
                    
                    if (charId == 0) continue;
                    
                    if (!result.TryGetValue(variable, out var list))
                    {
                        list = new List<(ulong, DateTime, long)>();
                        result[variable] = list;
                    }
                    list.Add((charId, new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetPointsBatchWithSuffix failed: {ex.Message}");
            }
        }

        return result;
    }
    
    /// <summary>
    /// Gets points within a specific time window for multiple variables.
    /// Optimized for virtualized/windowed loading - only fetches visible data.
    /// For each series, also includes the latest point BEFORE the window for line continuity.
    /// </summary>
    /// <param name="variablePrefix">Variable name prefix to match (e.g., "Crystal_")</param>
    /// <param name="windowStart">Start of the visible time window</param>
    /// <param name="windowEnd">End of the visible time window</param>
    /// <returns>Dictionary keyed by variable name, containing list of (characterId, timestamp, value) tuples</returns>
    public Dictionary<string, List<(ulong characterId, DateTime timestamp, long value)>> GetPointsInWindow(
        string variablePrefix, DateTime windowStart, DateTime windowEnd)
    {
        var result = new Dictionary<string, List<(ulong, DateTime, long)>>();

        lock (_readLock)
        {
            // Fall back to write connection if read connection not available
            var conn = _readConnection ?? _connection;
            if (conn == null) return result;

            try
            {
                // First, get all points within the window
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    WITH series_match AS (
                        SELECT id, variable, character_id FROM series WHERE variable LIKE $prefix
                    ),
                    -- Get the latest point before window for each series (for line continuity)
                    last_before AS (
                        SELECT sm.variable, sm.character_id, p.timestamp, p.value
                        FROM series_match sm
                        JOIN points p ON p.series_id = sm.id
                        WHERE p.timestamp < $windowStart
                        GROUP BY sm.id
                        HAVING p.timestamp = MAX(p.timestamp)
                    ),
                    -- Get all points within the window
                    in_window AS (
                        SELECT sm.variable, sm.character_id, p.timestamp, p.value
                        FROM series_match sm
                        JOIN points p ON p.series_id = sm.id
                        WHERE p.timestamp >= $windowStart AND p.timestamp <= $windowEnd
                    )
                    SELECT * FROM last_before
                    UNION ALL
                    SELECT * FROM in_window
                    ORDER BY variable, timestamp ASC";
                
                cmd.Parameters.AddWithValue("$prefix", variablePrefix + "%");
                cmd.Parameters.AddWithValue("$windowStart", windowStart.Ticks);
                cmd.Parameters.AddWithValue("$windowEnd", windowEnd.Ticks);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var variable = reader.GetString(0);
                    var charId = (ulong)reader.GetInt64(1);
                    var ticks = reader.GetInt64(2);
                    var value = reader.GetInt64(3);
                    
                    if (charId == 0) continue;
                    
                    if (!result.TryGetValue(variable, out var list))
                    {
                        list = new List<(ulong, DateTime, long)>();
                        result[variable] = list;
                    }
                    list.Add((charId, new DateTime(ticks, DateTimeKind.Utc), value));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetPointsInWindow failed: {ex.Message}");
            }
        }

        return result;
    }
    
    /// <summary>
    /// Gets the time range of available data for a variable prefix.
    /// Useful for determining scroll bounds without loading all data.
    /// </summary>
    /// <param name="variablePrefix">Variable name prefix to match</param>
    /// <returns>Tuple of (earliest timestamp, latest timestamp), or null if no data</returns>
    public (DateTime earliest, DateTime latest)? GetDataTimeRange(string variablePrefix)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return null;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT MIN(p.timestamp), MAX(p.timestamp)
                    FROM points p
                    JOIN series s ON p.series_id = s.id
                    WHERE s.variable LIKE $prefix";
                cmd.Parameters.AddWithValue("$prefix", variablePrefix + "%");

                using var reader = cmd.ExecuteReader();
                if (reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1))
                {
                    var earliest = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
                    var latest = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);
                    return (earliest, latest);
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetDataTimeRange failed: {ex.Message}");
            }
        }

        return null;
    }

}
