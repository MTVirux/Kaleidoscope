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
/// 
/// Uses WAL mode with a separate read-only connection for better concurrent read performance.
/// The write connection uses a lock to ensure single-writer semantics.
/// The read connection can operate concurrently with writes due to WAL mode.
/// </remarks>
public sealed class KaleidoscopeDbService : IDisposable
{
    private readonly object _writeLock = new();
    private readonly object _readLock = new();
    private readonly string? _dbPath;
    private SqliteConnection? _connection;
    private SqliteConnection? _readConnection;
    
    // Character name cache to avoid repeated DB queries
    // Stores (gameName, displayName, timeSeriesColor) tuple for each character
    private Dictionary<ulong, (string? GameName, string? DisplayName, uint? TimeSeriesColor)>? _characterNameCache;
    private DateTime _characterNameCacheTime = DateTime.MinValue;
    private const double CharacterNameCacheExpirySeconds = 30.0; // Cache for 30 seconds
    
    // Inventory value history stats cache (updated on writes, read without DB access)
    private readonly object _inventoryValueStatsLock = new();
    private long _cachedInventoryValueRecordCount;
    private long? _cachedInventoryValueMaxTimestamp;
    private bool _inventoryValueStatsCacheValid;
    
    // Cache size in KB (negative value for SQLite PRAGMA)
    private readonly int _cacheSizeKb;

    public string? DbPath => _dbPath;

    /// <summary>
    /// Creates a new database service with the specified cache size.
    /// </summary>
    /// <param name="dbPath">Path to the SQLite database file.</param>
    /// <param name="cacheSizeMb">Cache size in megabytes (1-64). Default is 8 MB.</param>
    public KaleidoscopeDbService(string? dbPath, int cacheSizeMb = 8)
    {
        _dbPath = dbPath;
        // Clamp to reasonable range and convert to KB
        cacheSizeMb = Math.Clamp(cacheSizeMb, 1, 64);
        _cacheSizeKb = cacheSizeMb * 1000; // Convert MB to KB (approximate)
        EnsureConnection();
    }

    #region Connection Management

    private void EnsureConnection()
    {
        if (string.IsNullOrEmpty(_dbPath)) return;

        lock (_writeLock)
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

                // Enable WAL mode for better concurrent read performance
                // WAL allows readers to continue while a write is in progress
                using (var walCmd = _connection.CreateCommand())
                {
                    walCmd.CommandText = "PRAGMA journal_mode = WAL";
                    walCmd.ExecuteNonQuery();
                }

                // Enable foreign key constraints for CASCADE deletes
                using (var pragmaCmd = _connection.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA foreign_keys = ON";
                    pragmaCmd.ExecuteNonQuery();
                }

                // Optimize synchronous mode for WAL - NORMAL is safe and faster than FULL
                using (var syncCmd = _connection.CreateCommand())
                {
                    syncCmd.CommandText = "PRAGMA synchronous = NORMAL";
                    syncCmd.ExecuteNonQuery();
                }

                // Set cache size for better read performance (negative = KB)
                using (var cacheCmd = _connection.CreateCommand())
                {
                    cacheCmd.CommandText = $"PRAGMA cache_size = -{_cacheSizeKb}";
                    cacheCmd.ExecuteNonQuery();
                }

                EnsureSchema();
                
                // Initialize read-only connection for concurrent reads
                EnsureReadConnection();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] Failed to initialize database: {ex.Message}", ex);
                _connection = null;
            }
        }
    }

    /// <summary>
    /// Ensures the read-only connection is initialized.
    /// Uses a separate connection for reads to allow concurrent access with WAL mode.
    /// </summary>
    private void EnsureReadConnection()
    {
        if (string.IsNullOrEmpty(_dbPath)) return;
        if (_readConnection != null) return;

        try
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadOnly
            };

            _readConnection = new SqliteConnection(csb.ToString());
            _readConnection.Open();

            // Set read connection cache size (same as write connection)
            using (var cacheCmd = _readConnection.CreateCommand())
            {
                cacheCmd.CommandText = $"PRAGMA cache_size = -{_cacheSizeKb}";
                cacheCmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[KaleidoscopeDb] Failed to initialize read connection: {ex.Message}");
            _readConnection = null;
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
    name TEXT,
    display_name TEXT,
    time_series_color INTEGER
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
CREATE INDEX IF NOT EXISTS idx_series_variable ON series(variable);
CREATE INDEX IF NOT EXISTS idx_points_series_timestamp ON points(series_id, timestamp);
CREATE INDEX IF NOT EXISTS idx_points_series_timestamp_value ON points(series_id, timestamp DESC, value);
CREATE INDEX IF NOT EXISTS idx_inventory_cache_char ON inventory_cache(character_id);
CREATE INDEX IF NOT EXISTS idx_inventory_cache_lookup ON inventory_cache(character_id, source_type, retainer_id);
CREATE INDEX IF NOT EXISTS idx_inventory_items_cache ON inventory_items(cache_id);
CREATE INDEX IF NOT EXISTS idx_inventory_items_item ON inventory_items(item_id);

-- Price tracking tables
CREATE TABLE IF NOT EXISTS item_prices (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    item_id INTEGER NOT NULL,
    world_id INTEGER NOT NULL,
    min_price_nq INTEGER NOT NULL DEFAULT 0,
    min_price_hq INTEGER NOT NULL DEFAULT 0,
    avg_price_nq INTEGER NOT NULL DEFAULT 0,
    avg_price_hq INTEGER NOT NULL DEFAULT 0,
    last_sale_nq INTEGER NOT NULL DEFAULT 0,
    last_sale_hq INTEGER NOT NULL DEFAULT 0,
    sale_velocity REAL NOT NULL DEFAULT 0,
    last_updated INTEGER NOT NULL,
    UNIQUE (item_id, world_id)
);

CREATE TABLE IF NOT EXISTS price_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    item_id INTEGER NOT NULL,
    world_id INTEGER NOT NULL,
    timestamp INTEGER NOT NULL,
    min_price_nq INTEGER NOT NULL DEFAULT 0,
    min_price_hq INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS inventory_value_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    character_id INTEGER NOT NULL,
    timestamp INTEGER NOT NULL,
    total_value INTEGER NOT NULL DEFAULT 0,
    gil_value INTEGER NOT NULL DEFAULT 0,
    item_value INTEGER NOT NULL DEFAULT 0
);

-- Per-item breakdown for inventory value history (enables recalculation when sales are deleted)
CREATE TABLE IF NOT EXISTS inventory_value_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    history_id INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price INTEGER NOT NULL,
    FOREIGN KEY(history_id) REFERENCES inventory_value_history(id) ON DELETE CASCADE
);

-- Individual sale records table for per-world sale tracking
CREATE TABLE IF NOT EXISTS sale_records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    item_id INTEGER NOT NULL,
    world_id INTEGER NOT NULL,
    price_per_unit INTEGER NOT NULL,
    quantity INTEGER NOT NULL DEFAULT 1,
    is_hq INTEGER NOT NULL DEFAULT 0,
    total INTEGER NOT NULL,
    timestamp INTEGER NOT NULL,
    buyer_name TEXT
);

CREATE INDEX IF NOT EXISTS idx_item_prices_item ON item_prices(item_id);
CREATE INDEX IF NOT EXISTS idx_item_prices_world ON item_prices(world_id);
CREATE INDEX IF NOT EXISTS idx_item_prices_lookup ON item_prices(item_id, world_id);
CREATE INDEX IF NOT EXISTS idx_price_history_item_world ON price_history(item_id, world_id);
CREATE INDEX IF NOT EXISTS idx_price_history_timestamp ON price_history(timestamp);
CREATE INDEX IF NOT EXISTS idx_inventory_value_char ON inventory_value_history(character_id);
CREATE INDEX IF NOT EXISTS idx_inventory_value_timestamp ON inventory_value_history(timestamp);
CREATE INDEX IF NOT EXISTS idx_inventory_value_items_history ON inventory_value_items(history_id);
CREATE INDEX IF NOT EXISTS idx_inventory_value_items_item ON inventory_value_items(item_id);
CREATE INDEX IF NOT EXISTS idx_sale_records_item ON sale_records(item_id);
CREATE INDEX IF NOT EXISTS idx_sale_records_world ON sale_records(world_id);
CREATE INDEX IF NOT EXISTS idx_sale_records_item_world ON sale_records(item_id, world_id);
CREATE INDEX IF NOT EXISTS idx_sale_records_timestamp ON sale_records(timestamp);
";
        cmd.ExecuteNonQuery();

        // Run migrations for existing databases
        RunMigrations();
    }

    /// <summary>
    /// Runs database migrations for schema updates.
    /// </summary>
    private void RunMigrations()
    {
        if (_connection == null) return;

        try
        {
            // Migration: Add last_sale_nq and last_sale_hq columns to item_prices table
            MigrateAddLastSaleColumns();
            
            // Migration: Add inventory_value_items table for per-item value tracking
            MigrateAddInventoryValueItemsTable();
            
            // Migration: Add display_name column to character_names table
            MigrateAddDisplayNameColumn();
            
            // Migration: Add time_series_color column to character_names table
            MigrateAddTimeSeriesColorColumn();
        }
        catch (Exception ex)
        {
            LogService.Error($"[KaleidoscopeDb] Migration failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Adds last_sale_nq and last_sale_hq columns to item_prices table if they don't exist.
    /// </summary>
    private void MigrateAddLastSaleColumns()
    {
        if (_connection == null) return;

        // Check if columns exist
        using var checkCmd = _connection.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(item_prices)";
        
        bool hasLastSaleNq = false;
        bool hasLastSaleHq = false;
        
        using (var reader = checkCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var columnName = reader.GetString(1);
                if (columnName == "last_sale_nq") hasLastSaleNq = true;
                if (columnName == "last_sale_hq") hasLastSaleHq = true;
            }
        }

        // Add missing columns
        if (!hasLastSaleNq)
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE item_prices ADD COLUMN last_sale_nq INTEGER NOT NULL DEFAULT 0";
            alterCmd.ExecuteNonQuery();
            LogService.Debug("[KaleidoscopeDb] Migration: Added last_sale_nq column to item_prices");
        }

        if (!hasLastSaleHq)
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE item_prices ADD COLUMN last_sale_hq INTEGER NOT NULL DEFAULT 0";
            alterCmd.ExecuteNonQuery();
            LogService.Debug("[KaleidoscopeDb] Migration: Added last_sale_hq column to item_prices");
        }
    }

    /// <summary>
    /// Creates the inventory_value_items table if it doesn't exist.
    /// </summary>
    private void MigrateAddInventoryValueItemsTable()
    {
        if (_connection == null) return;

        // Check if table exists
        using var checkCmd = _connection.CreateCommand();
        checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='inventory_value_items'";
        var exists = checkCmd.ExecuteScalar() != null;

        if (!exists)
        {
            using var createCmd = _connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS inventory_value_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    history_id INTEGER NOT NULL,
                    item_id INTEGER NOT NULL,
                    quantity INTEGER NOT NULL,
                    unit_price INTEGER NOT NULL,
                    FOREIGN KEY(history_id) REFERENCES inventory_value_history(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_inventory_value_items_history ON inventory_value_items(history_id);
                CREATE INDEX IF NOT EXISTS idx_inventory_value_items_item ON inventory_value_items(item_id);";
            createCmd.ExecuteNonQuery();
            LogService.Debug("[KaleidoscopeDb] Migration: Created inventory_value_items table");
        }
    }

    /// <summary>
    /// Adds display_name column to character_names table if it doesn't exist.
    /// This allows users to set custom display names separate from the game name.
    /// </summary>
    private void MigrateAddDisplayNameColumn()
    {
        if (_connection == null) return;

        // Check if column exists
        using var checkCmd = _connection.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(character_names)";
        
        bool hasDisplayName = false;
        
        using (var reader = checkCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var columnName = reader.GetString(1);
                if (columnName == "display_name") hasDisplayName = true;
            }
        }

        // Add missing column
        if (!hasDisplayName)
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE character_names ADD COLUMN display_name TEXT";
            alterCmd.ExecuteNonQuery();
            LogService.Debug("[KaleidoscopeDb] Migration: Added display_name column to character_names");
        }
    }

    /// <summary>
    /// Adds time_series_color column to character_names table if it doesn't exist.
    /// This allows users to set custom colors for time-series graphs.
    /// </summary>
    private void MigrateAddTimeSeriesColorColumn()
    {
        if (_connection == null) return;

        // Check if column exists
        using var checkCmd = _connection.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(character_names)";
        
        bool hasTimeSeriesColor = false;
        
        using (var reader = checkCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var columnName = reader.GetString(1);
                if (columnName == "time_series_color") hasTimeSeriesColor = true;
            }
        }

        // Add missing column
        if (!hasTimeSeriesColor)
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE character_names ADD COLUMN time_series_color INTEGER";
            alterCmd.ExecuteNonQuery();
            LogService.Debug("[KaleidoscopeDb] Migration: Added time_series_color column to character_names");
        }
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
                LogService.Debug($"[KaleidoscopeDb] GetPoints failed: {ex.Message}");
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
                LogService.Debug($"[KaleidoscopeDb] GetPointsSince failed: {ex.Message}");
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
                LogService.Debug($"[KaleidoscopeDb] GetAllPoints failed: {ex.Message}");
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
                LogService.Debug($"[KaleidoscopeDb] GetAllPointsBatch failed: {ex.Message}");
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
                LogService.Debug($"[KaleidoscopeDb] GetPointsBatchWithSuffix failed: {ex.Message}");
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
                LogService.Debug($"[KaleidoscopeDb] GetPointsInWindow failed: {ex.Message}");
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
                LogService.Debug($"[KaleidoscopeDb] GetDataTimeRange failed: {ex.Message}");
            }
        }

        return null;
    }

    #endregion

    #region Character Operations

    /// <summary>
    /// Gets all character IDs that have data for a variable.
    /// </summary>
    public List<ulong> GetAvailableCharacters(string variable)
    {
        var result = new List<ulong>();

        lock (_writeLock)
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
    /// Saves or updates a character's game name (automatically detected from the game).
    /// Preserves any existing display_name that was set by the user.
    /// </summary>
    public bool SaveCharacterName(ulong characterId, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                // First check if record exists to preserve display_name and time_series_color
                cmd.CommandText = "SELECT display_name, time_series_color FROM character_names WHERE character_id = $c";
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                string? existingDisplayName = null;
                long? existingColor = null;
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        existingDisplayName = reader.IsDBNull(0) ? null : reader.GetString(0);
                        existingColor = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                    }
                }
                
                cmd.CommandText = "INSERT OR REPLACE INTO character_names(character_id, name, display_name, time_series_color) VALUES($c, $n, $d, $col)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$d", existingDisplayName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$col", existingColor.HasValue ? (object)existingColor.Value : DBNull.Value);
                cmd.ExecuteNonQuery();
                
                // Invalidate cache so next lookup gets fresh data
                InvalidateCharacterNameCache();
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
    /// Saves or updates a character's display name (user-customizable).
    /// </summary>
    /// <param name="characterId">The character's content ID.</param>
    /// <param name="displayName">The custom display name. Pass null to clear and use game name.</param>
    /// <returns>True if successful.</returns>
    public bool SaveCharacterDisplayName(ulong characterId, string? displayName)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                // Update only the display_name column, or insert if not exists
                cmd.CommandText = @"
                    INSERT INTO character_names(character_id, name, display_name) 
                    VALUES($c, NULL, $d)
                    ON CONFLICT(character_id) DO UPDATE SET display_name = $d";
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.Parameters.AddWithValue("$d", string.IsNullOrEmpty(displayName) ? (object)DBNull.Value : displayName);
                cmd.ExecuteNonQuery();
                
                // Invalidate cache so next lookup gets fresh data
                InvalidateCharacterNameCache();
                return true;
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] SaveCharacterDisplayName failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Saves or updates a character's time series color.
    /// </summary>
    /// <param name="characterId">The character's content ID.</param>
    /// <param name="color">The ARGB color value. Pass null to clear and use default colors.</param>
    /// <returns>True if successful.</returns>
    public bool SaveCharacterTimeSeriesColor(ulong characterId, uint? color)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                // Update only the time_series_color column, or insert if not exists
                cmd.CommandText = @"
                    INSERT INTO character_names(character_id, name, display_name, time_series_color) 
                    VALUES($c, NULL, NULL, $col)
                    ON CONFLICT(character_id) DO UPDATE SET time_series_color = $col";
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.Parameters.AddWithValue("$col", color.HasValue ? (object)(long)color.Value : DBNull.Value);
                cmd.ExecuteNonQuery();
                
                // Invalidate cache so next lookup gets fresh data
                InvalidateCharacterNameCache();
                return true;
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] SaveCharacterTimeSeriesColor failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the display name for a character (custom display_name if set, otherwise game name).
    /// Uses cached data if available to avoid repeated DB queries.
    /// </summary>
    public string? GetCharacterName(ulong characterId)
    {
        // Try cache first
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return names.DisplayName ?? names.GameName;
        
        return null;
    }

    /// <summary>
    /// Gets the game name for a character (the name automatically detected from the game).
    /// </summary>
    public string? GetCharacterGameName(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return names.GameName;
        
        return null;
    }

    /// <summary>
    /// Gets the custom display name for a character (null if not set).
    /// </summary>
    public string? GetCharacterDisplayName(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return names.DisplayName;
        
        return null;
    }

    /// <summary>
    /// Gets both the game name and display name for a character.
    /// </summary>
    public (string? GameName, string? DisplayName) GetCharacterNames(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return (names.GameName, names.DisplayName);
        
        return (null, null);
    }

    /// <summary>
    /// Gets the time series color for a character (null if not set).
    /// </summary>
    public uint? GetCharacterTimeSeriesColor(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return names.TimeSeriesColor;
        
        return null;
    }

    /// <summary>
    /// Gets all character data (game name, display name, and time series color).
    /// </summary>
    public (string? GameName, string? DisplayName, uint? TimeSeriesColor) GetCharacterData(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var data))
            return data;
        
        return (null, null, null);
    }
    
    /// <summary>
    /// Gets or refreshes the character name cache.
    /// </summary>
    private Dictionary<ulong, (string? GameName, string? DisplayName, uint? TimeSeriesColor)> GetCharacterNameCache()
    {
        var now = DateTime.UtcNow;
        if (_characterNameCache != null && (now - _characterNameCacheTime).TotalSeconds < CharacterNameCacheExpirySeconds)
        {
            return _characterNameCache;
        }
        
        // Refresh cache
        var newCache = new Dictionary<ulong, (string?, string?, uint?)>();
        
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return newCache;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT character_id, name, display_name, time_series_color FROM character_names";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cid = reader.GetInt64(0);
                    var gameName = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var displayName = reader.IsDBNull(2) ? null : reader.GetString(2);
                    uint? timeSeriesColor = reader.IsDBNull(3) ? null : (uint)reader.GetInt64(3);
                    if (cid != 0)
                        newCache[(ulong)cid] = (gameName, displayName, timeSeriesColor);
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[KaleidoscopeDb] GetCharacterNameCache failed: {ex.Message}");
            }
        }
        
        _characterNameCache = newCache;
        _characterNameCacheTime = now;
        return newCache;
    }
    
    /// <summary>
    /// Invalidates the character name cache.
    /// Call this after saving or updating character names.
    /// </summary>
    public void InvalidateCharacterNameCache()
    {
        _characterNameCache = null;
        _characterNameCacheTime = DateTime.MinValue;
    }

    /// <summary>
    /// Gets all stored character name mappings (returns display_name if set, otherwise game name).
    /// Uses cached data to avoid repeated DB queries.
    /// </summary>
    public List<(ulong characterId, string? name)> GetAllCharacterNames()
    {
        var cache = GetCharacterNameCache();
        var result = new List<(ulong, string?)>(cache.Count);
        
        foreach (var kvp in cache)
        {
            // Return display_name if set, otherwise game name
            result.Add((kvp.Key, kvp.Value.DisplayName ?? kvp.Value.GameName));
        }
        
        return result;
    }

    /// <summary>
    /// Gets all stored character name mappings with both game and display names.
    /// </summary>
    public List<(ulong characterId, string? gameName, string? displayName)> GetAllCharacterNamesExtended()
    {
        var cache = GetCharacterNameCache();
        var result = new List<(ulong, string?, string?)>(cache.Count);
        
        foreach (var kvp in cache)
        {
            result.Add((kvp.Key, kvp.Value.GameName, kvp.Value.DisplayName));
        }
        
        return result;
    }

    /// <summary>
    /// Gets all stored character data including time series colors.
    /// </summary>
    public List<(ulong characterId, string? gameName, string? displayName, uint? timeSeriesColor)> GetAllCharacterDataExtended()
    {
        var cache = GetCharacterNameCache();
        var result = new List<(ulong, string?, string?, uint?)>(cache.Count);
        
        foreach (var kvp in cache)
        {
            result.Add((kvp.Key, kvp.Value.GameName, kvp.Value.DisplayName, kvp.Value.TimeSeriesColor));
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets all stored character name mappings as a dictionary (display_name if set, otherwise game name).
    /// </summary>
    public IReadOnlyDictionary<ulong, string?> GetAllCharacterNamesDict()
    {
        var cache = GetCharacterNameCache();
        var result = new Dictionary<ulong, string?>(cache.Count);
        foreach (var kvp in cache)
        {
            result[kvp.Key] = kvp.Value.DisplayName ?? kvp.Value.GameName;
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
        lock (_writeLock)
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
        lock (_writeLock)
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
    /// Clears all data from all tables to simulate a fresh install.
    /// </summary>
    public bool ClearAllTables()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                
                // Time-series data
                cmd.CommandText = "DELETE FROM points";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM series";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM character_names";
                cmd.ExecuteNonQuery();

                // Inventory data
                cmd.CommandText = "DELETE FROM inventory_items";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM inventory_cache";
                cmd.ExecuteNonQuery();

                // Price data
                cmd.CommandText = "DELETE FROM item_prices";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM price_history";
                cmd.ExecuteNonQuery();

                // Inventory value history
                cmd.CommandText = "DELETE FROM inventory_value_items";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM inventory_value_history";
                cmd.ExecuteNonQuery();

                // Sale records
                cmd.CommandText = "DELETE FROM sale_records";
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
        lock (_writeLock)
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
        lock (_writeLock)
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
                        var sanitized = NameSanitizer.Sanitize(name);

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

    #endregion

    #region Export

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
                LogService.Error($"[KaleidoscopeDb] ExportToCsv failed: {ex.Message}", ex);
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Inventory Cache Operations

    /// <summary>
    /// Gets total crystal count for a character across all retainer inventories.
    /// Uses the inventory cache system instead of the legacy retainer_crystals table.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="element">Crystal element (0=Fire, 1=Ice, 2=Wind, 3=Earth, 4=Lightning, 5=Water).</param>
    /// <param name="tier">Crystal tier (0=Shard, 1=Crystal, 2=Cluster).</param>
    /// <returns>Total quantity across all retainers.</returns>
    public long GetTotalRetainerCrystals(ulong characterId, int element, int tier)
    {
        // Crystal item IDs: Shard = 2 + element, Crystal = 8 + element, Cluster = 14 + element
        uint itemId = (uint)(2 + element + tier * 6);
        return GetRetainerItemCount(characterId, itemId);
    }

    /// <summary>
    /// Gets total count of a specific item across all retainer inventories for a character.
    /// </summary>
    public long GetRetainerItemCount(ulong characterId, uint itemId)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(ii.quantity), 0)
                    FROM inventory_items ii
                    JOIN inventory_cache ic ON ii.cache_id = ic.id
                    WHERE ic.character_id = $cid 
                      AND ic.source_type = 1
                      AND ii.item_id = $iid";
                cmd.Parameters.AddWithValue("$cid", (long)characterId);
                cmd.Parameters.AddWithValue("$iid", (long)itemId);

                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? (long)result : 0;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetRetainerItemCount failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Saves or updates an inventory cache entry and its items.
    /// Replaces all existing items for this cache.
    /// </summary>
    public void SaveInventoryCache(Models.Inventory.InventoryCacheEntry entry)
    {
        if (entry == null) return;

        lock (_writeLock)
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
                    LogService.Verbose($"[KaleidoscopeDb] Saved inventory cache for {entry.SourceType} {entry.Name}: {entry.Items.Count} items");
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
        lock (_writeLock)
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

        lock (_writeLock)
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

        lock (_writeLock)
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
        lock (_writeLock)
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

        lock (_writeLock)
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

    #region Price Tracking Operations

    /// <summary>
    /// Saves or updates the current price for an item on a world.
    /// </summary>
    public void SaveItemPrice(int itemId, int worldId, int minPriceNq, int minPriceHq, int avgPriceNq = 0, int avgPriceHq = 0, int lastSaleNq = 0, int lastSaleHq = 0, float saleVelocity = 0)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO item_prices (item_id, world_id, min_price_nq, min_price_hq, avg_price_nq, avg_price_hq, last_sale_nq, last_sale_hq, sale_velocity, last_updated)
                    VALUES ($iid, $wid, $mnq, $mhq, $anq, $ahq, $lsnq, $lshq, $sv, $time)
                    ON CONFLICT(item_id, world_id) DO UPDATE SET
                        min_price_nq = excluded.min_price_nq,
                        min_price_hq = excluded.min_price_hq,
                        avg_price_nq = excluded.avg_price_nq,
                        avg_price_hq = excluded.avg_price_hq,
                        last_sale_nq = excluded.last_sale_nq,
                        last_sale_hq = excluded.last_sale_hq,
                        sale_velocity = excluded.sale_velocity,
                        last_updated = excluded.last_updated";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$wid", worldId);
                cmd.Parameters.AddWithValue("$mnq", minPriceNq);
                cmd.Parameters.AddWithValue("$mhq", minPriceHq);
                cmd.Parameters.AddWithValue("$anq", avgPriceNq);
                cmd.Parameters.AddWithValue("$ahq", avgPriceHq);
                cmd.Parameters.AddWithValue("$lsnq", lastSaleNq);
                cmd.Parameters.AddWithValue("$lshq", lastSaleHq);
                cmd.Parameters.AddWithValue("$sv", saleVelocity);
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] SaveItemPrice failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Saves multiple item prices in a single transaction for better performance.
    /// Reduces lock contention by batching writes together.
    /// </summary>
    public void SaveItemPricesBatch(IEnumerable<(int ItemId, int WorldId, int MinPriceNq, int MinPriceHq, int LastSaleNq, int LastSaleHq)> prices)
    {
        var priceList = prices.ToList();
        if (priceList.Count == 0) return;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var transaction = _connection.BeginTransaction();
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO item_prices (item_id, world_id, min_price_nq, min_price_hq, avg_price_nq, avg_price_hq, last_sale_nq, last_sale_hq, sale_velocity, last_updated)
                        VALUES ($iid, $wid, $mnq, $mhq, 0, 0, $lsnq, $lshq, 0, $time)
                        ON CONFLICT(item_id, world_id) DO UPDATE SET
                            min_price_nq = excluded.min_price_nq,
                            min_price_hq = excluded.min_price_hq,
                            last_sale_nq = excluded.last_sale_nq,
                            last_sale_hq = excluded.last_sale_hq,
                            last_updated = excluded.last_updated";

                    var iidParam = cmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var widParam = cmd.Parameters.Add("$wid", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var mnqParam = cmd.Parameters.Add("$mnq", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var mhqParam = cmd.Parameters.Add("$mhq", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var lsnqParam = cmd.Parameters.Add("$lsnq", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var lshqParam = cmd.Parameters.Add("$lshq", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var timeParam = cmd.Parameters.Add("$time", Microsoft.Data.Sqlite.SqliteType.Integer);

                    var now = DateTime.UtcNow.Ticks;

                    foreach (var (itemId, worldId, minNq, minHq, lastNq, lastHq) in priceList)
                    {
                        iidParam.Value = itemId;
                        widParam.Value = worldId;
                        mnqParam.Value = minNq;
                        mhqParam.Value = minHq;
                        lsnqParam.Value = lastNq;
                        lshqParam.Value = lastHq;
                        timeParam.Value = now;
                        cmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] SaveItemPricesBatch failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Saves a price history point for an item.
    /// </summary>
    public void SavePriceHistory(int itemId, int worldId, int minPriceNq, int minPriceHq)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO price_history (item_id, world_id, timestamp, min_price_nq, min_price_hq)
                    VALUES ($iid, $wid, $time, $mnq, $mhq)";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$wid", worldId);
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
                cmd.Parameters.AddWithValue("$mnq", minPriceNq);
                cmd.Parameters.AddWithValue("$mhq", minPriceHq);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] SavePriceHistory failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets the current price for an item on a world.
    /// Returns (minPriceNq, minPriceHq, lastUpdated) or null if not found.
    /// </summary>
    public (int MinPriceNq, int MinPriceHq, int AvgPriceNq, int AvgPriceHq, float SaleVelocity, DateTime LastUpdated)? GetItemPrice(int itemId, int worldId)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return null;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT min_price_nq, min_price_hq, avg_price_nq, avg_price_hq, sale_velocity, last_updated
                    FROM item_prices
                    WHERE item_id = $iid AND world_id = $wid";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$wid", worldId);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return (
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        reader.GetInt32(3),
                        reader.GetFloat(4),
                        new DateTime(reader.GetInt64(5), DateTimeKind.Utc)
                    );
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetItemPrice failed: {ex.Message}", ex);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the minimum price for an item across all tracked worlds.
    /// </summary>
    public int? GetMinPrice(int itemId, bool preferHq = false)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return null;

            try
            {
                using var cmd = _connection.CreateCommand();
                if (preferHq)
                {
                    cmd.CommandText = @"
                        SELECT CASE 
                            WHEN MIN(CASE WHEN min_price_hq > 0 THEN min_price_hq END) IS NOT NULL 
                            THEN MIN(CASE WHEN min_price_hq > 0 THEN min_price_hq END)
                            ELSE MIN(CASE WHEN min_price_nq > 0 THEN min_price_nq END)
                        END
                        FROM item_prices
                        WHERE item_id = $iid";
                }
                else
                {
                    cmd.CommandText = @"
                        SELECT MIN(CASE WHEN min_price_nq > 0 THEN min_price_nq WHEN min_price_hq > 0 THEN min_price_hq END)
                        FROM item_prices
                        WHERE item_id = $iid";
                }
                cmd.Parameters.AddWithValue("$iid", itemId);

                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetMinPrice failed: {ex.Message}", ex);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all item prices for multiple items at once.
    /// Returns last sale prices for inventory value calculation.
    /// </summary>
    public Dictionary<int, (int LastSaleNq, int LastSaleHq)> GetItemPricesBatch(IEnumerable<int> itemIds, int? worldId = null)
    {
        var result = new Dictionary<int, (int, int)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                var itemIdList = itemIds.ToList();
                if (itemIdList.Count == 0) return result;

                using var cmd = _connection.CreateCommand();
                
                if (worldId.HasValue)
                {
                    cmd.CommandText = $@"
                        SELECT item_id, last_sale_nq, last_sale_hq
                        FROM item_prices
                        WHERE item_id IN ({string.Join(",", itemIdList)}) AND world_id = $wid";
                    cmd.Parameters.AddWithValue("$wid", worldId.Value);
                }
                else
                {
                    // Get the most recent last sale price across all worlds (prefer most recently updated)
                    cmd.CommandText = $@"
                        SELECT item_id, 
                               MAX(CASE WHEN last_sale_nq > 0 THEN last_sale_nq END) as sale_nq,
                               MAX(CASE WHEN last_sale_hq > 0 THEN last_sale_hq END) as sale_hq
                        FROM item_prices
                        WHERE item_id IN ({string.Join(",", itemIdList)})
                        GROUP BY item_id";
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var iid = reader.GetInt32(0);
                    var snq = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    var shq = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    result[iid] = (snq, shq);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetItemPricesBatch failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets detailed price information for multiple items, including which world has the best price.
    /// Returns item ID -> (MinPrice, WorldId with min price, LastUpdated)
    /// </summary>
    public Dictionary<int, (int MinPrice, int WorldId, DateTime LastUpdated)> GetItemPricesDetailedBatch(IEnumerable<int> itemIds)
    {
        var result = new Dictionary<int, (int MinPrice, int WorldId, DateTime LastUpdated)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                var itemIdList = itemIds.ToList();
                if (itemIdList.Count == 0) return result;

                using var cmd = _connection.CreateCommand();
                
                // For each item, find the world with the lowest non-zero NQ price (or HQ if no NQ)
                // Using a subquery to get the row with the minimum price per item
                cmd.CommandText = $@"
                    WITH min_prices AS (
                        SELECT item_id, 
                               CASE WHEN min_price_nq > 0 THEN min_price_nq ELSE min_price_hq END as effective_price,
                               world_id,
                               last_updated,
                               ROW_NUMBER() OVER (
                                   PARTITION BY item_id 
                                   ORDER BY CASE WHEN min_price_nq > 0 THEN min_price_nq ELSE min_price_hq END ASC
                               ) as rn
                        FROM item_prices
                        WHERE item_id IN ({string.Join(",", itemIdList)})
                          AND (min_price_nq > 0 OR min_price_hq > 0)
                    )
                    SELECT item_id, effective_price, world_id, last_updated
                    FROM min_prices
                    WHERE rn = 1";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var iid = reader.GetInt32(0);
                    var price = reader.GetInt32(1);
                    var wid = reader.GetInt32(2);
                    var lastUpdated = new DateTime(reader.GetInt64(3), DateTimeKind.Utc);
                    result[iid] = (price, wid, lastUpdated);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetItemPricesDetailedBatch failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the last updated time for items that have stale or missing price data.
    /// Returns item IDs where the last_updated is older than the specified threshold or no price exists.
    /// </summary>
    public HashSet<int> GetStaleItemIds(IEnumerable<int> itemIds, TimeSpan staleThreshold)
    {
        var staleItems = new HashSet<int>();
        var itemIdList = itemIds.ToList();
        if (itemIdList.Count == 0) return staleItems;

        // Start with all items as potentially stale
        foreach (var id in itemIdList)
            staleItems.Add(id);

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return staleItems;

            try
            {
                var thresholdTicks = (DateTime.UtcNow - staleThreshold).Ticks;

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $@"
                    SELECT item_id
                    FROM item_prices
                    WHERE item_id IN ({string.Join(",", itemIdList)})
                      AND last_updated > $threshold
                      AND (last_sale_nq > 0 OR last_sale_hq > 0)";
                cmd.Parameters.AddWithValue("$threshold", thresholdTicks);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var itemId = reader.GetInt32(0);
                    // Remove from stale set - this item has fresh data
                    staleItems.Remove(itemId);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetStaleItemIds failed: {ex.Message}", ex);
            }
        }

        return staleItems;
    }

    /// <summary>
    /// Saves inventory value history for a character.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="totalValue">Total value (gil + items).</param>
    /// <param name="gilValue">Gil value.</param>
    /// <param name="itemValue">Item value.</param>
    /// <param name="itemContributions">Optional per-item breakdown: (itemId, quantity, unitPrice).</param>
    public void SaveInventoryValueHistory(ulong characterId, long totalValue, long gilValue, long itemValue, 
        List<(int ItemId, long Quantity, int UnitPrice)>? itemContributions = null)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var transaction = _connection.BeginTransaction();
                
                try
                {
                    // Insert the main history record
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO inventory_value_history (character_id, timestamp, total_value, gil_value, item_value)
                        VALUES ($cid, $time, $total, $gil, $item);
                        SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$cid", (long)characterId);
                    cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
                    cmd.Parameters.AddWithValue("$total", totalValue);
                    cmd.Parameters.AddWithValue("$gil", gilValue);
                    cmd.Parameters.AddWithValue("$item", itemValue);
                    var historyId = (long)cmd.ExecuteScalar()!;

                    // Insert per-item contributions if provided
                    if (itemContributions != null && itemContributions.Count > 0)
                    {
                        using var itemCmd = _connection.CreateCommand();
                        itemCmd.Transaction = transaction;
                        itemCmd.CommandText = @"
                            INSERT INTO inventory_value_items (history_id, item_id, quantity, unit_price)
                            VALUES ($hid, $iid, $qty, $price)";
                        
                        var hidParam = itemCmd.Parameters.Add("$hid", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var iidParam = itemCmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var qtyParam = itemCmd.Parameters.Add("$qty", Microsoft.Data.Sqlite.SqliteType.Integer);
                        var priceParam = itemCmd.Parameters.Add("$price", Microsoft.Data.Sqlite.SqliteType.Integer);
                        
                        hidParam.Value = historyId;
                        
                        foreach (var (itemId, quantity, unitPrice) in itemContributions)
                        {
                            iidParam.Value = itemId;
                            qtyParam.Value = quantity;
                            priceParam.Value = unitPrice;
                            itemCmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                    
                    // Invalidate cached stats so next read will refresh from DB
                    InvalidateInventoryValueStatsCache();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] SaveInventoryValueHistory failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets inventory value history for a character.
    /// </summary>
    public List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> GetInventoryValueHistory(ulong characterId, DateTime? since = null)
    {
        var result = new List<(DateTime, long, long, long)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                var sql = @"
                    SELECT timestamp, total_value, gil_value, item_value
                    FROM inventory_value_history
                    WHERE character_id = $cid";
                
                if (since.HasValue)
                {
                    sql += " AND timestamp >= $since";
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }
                
                sql += " ORDER BY timestamp ASC";
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("$cid", (long)characterId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add((
                        new DateTime(reader.GetInt64(0), DateTimeKind.Utc),
                        reader.GetInt64(1),
                        reader.GetInt64(2),
                        reader.GetInt64(3)
                    ));
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetInventoryValueHistory failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets inventory value history for all characters.
    /// </summary>
    public List<(ulong CharacterId, DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> GetAllInventoryValueHistory(DateTime? since = null)
    {
        var result = new List<(ulong, DateTime, long, long, long)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                var sql = @"
                    SELECT character_id, timestamp, total_value, gil_value, item_value
                    FROM inventory_value_history";
                
                if (since.HasValue)
                {
                    sql += " WHERE timestamp >= $since";
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }
                
                sql += " ORDER BY timestamp ASC";
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add((
                        (ulong)reader.GetInt64(0),
                        new DateTime(reader.GetInt64(1), DateTimeKind.Utc),
                        reader.GetInt64(2),
                        reader.GetInt64(3),
                        reader.GetInt64(4)
                    ));
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetAllInventoryValueHistory failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets inventory value history aggregated across all characters (summed by timestamp) directly in SQL.
    /// More efficient than fetching all data and grouping in C#.
    /// </summary>
    public List<(DateTime Timestamp, long TotalValue, long GilValue, long ItemValue)> GetAggregatedInventoryValueHistory(DateTime? since = null)
    {
        var result = new List<(DateTime, long, long, long)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                var sql = @"
                    SELECT timestamp, SUM(total_value), SUM(gil_value), SUM(item_value)
                    FROM inventory_value_history";
                
                if (since.HasValue)
                {
                    sql += " WHERE timestamp >= $since";
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }
                
                sql += " GROUP BY timestamp ORDER BY timestamp ASC";
                cmd.CommandText = sql;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add((
                        new DateTime(reader.GetInt64(0), DateTimeKind.Utc),
                        reader.GetInt64(1),
                        reader.GetInt64(2),
                        reader.GetInt64(3)
                    ));
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetAggregatedInventoryValueHistory failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the latest timestamp and total record count from inventory_value_history.
    /// Uses an in-memory cache that's invalidated when data is written.
    /// This version is safe to call from the main thread as it avoids DB queries when cache is valid.
    /// </summary>
    /// <param name="characterId">Optional character filter (null = all characters).</param>
    /// <returns>Cached stats. Call RefreshInventoryValueStatsCacheAsync() to update from DB.</returns>
    public (long recordCount, long? maxTimestampTicks) GetInventoryValueHistoryStatsCached(ulong? characterId = null)
    {
        // Note: characterId filtering is not supported in cached mode - returns global stats
        // This is a deliberate trade-off for performance
        lock (_inventoryValueStatsLock)
        {
            if (_inventoryValueStatsCacheValid)
            {
                return (_cachedInventoryValueRecordCount, _cachedInventoryValueMaxTimestamp);
            }
        }
        
        // Cache not valid - need to refresh (this will block, but only happens once after invalidation)
        RefreshInventoryValueStatsCache();
        
        lock (_inventoryValueStatsLock)
        {
            return (_cachedInventoryValueRecordCount, _cachedInventoryValueMaxTimestamp);
        }
    }
    
    /// <summary>
    /// Refreshes the inventory value stats cache from the database.
    /// Call this on a background thread after writes to pre-populate the cache.
    /// </summary>
    public void RefreshInventoryValueStatsCache()
    {
        var stats = GetInventoryValueHistoryStats(null);
        lock (_inventoryValueStatsLock)
        {
            _cachedInventoryValueRecordCount = stats.recordCount;
            _cachedInventoryValueMaxTimestamp = stats.maxTimestampTicks;
            _inventoryValueStatsCacheValid = true;
        }
    }
    
    /// <summary>
    /// Invalidates the inventory value stats cache.
    /// Called automatically after writes.
    /// </summary>
    public void InvalidateInventoryValueStatsCache()
    {
        lock (_inventoryValueStatsLock)
        {
            _inventoryValueStatsCacheValid = false;
        }
    }

    /// <summary>
    /// Gets the latest timestamp and total record count from inventory_value_history.
    /// Used for cache invalidation detection.
    /// WARNING: This directly queries the database - prefer GetInventoryValueHistoryStatsCached() for main thread.
    /// </summary>
    public (long recordCount, long? maxTimestampTicks) GetInventoryValueHistoryStats(ulong? characterId = null)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return (0, null);

            try
            {
                using var cmd = _connection.CreateCommand();
                var whereClause = characterId.HasValue ? " WHERE character_id = $cid" : "";
                cmd.CommandText = $"SELECT COUNT(*), MAX(timestamp) FROM inventory_value_history{whereClause}";
                
                if (characterId.HasValue)
                    cmd.Parameters.AddWithValue("$cid", (long)characterId.Value);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var count = reader.GetInt64(0);
                    var maxTs = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                    return (count, maxTs);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetInventoryValueHistoryStats failed: {ex.Message}", ex);
            }
        }

        return (0, null);
    }

    /// <summary>
    /// Clears all price tracking data (item_prices, price_history, inventory_value_history, inventory_value_items, sale_records).
    /// </summary>
    public bool ClearAllPriceData()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();

                cmd.CommandText = "DELETE FROM item_prices";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM price_history";
                cmd.ExecuteNonQuery();

                // Delete inventory_value_items first (child table), then inventory_value_history
                cmd.CommandText = "DELETE FROM inventory_value_items";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM inventory_value_history";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "DELETE FROM sale_records";
                cmd.ExecuteNonQuery();

                LogService.Info("[KaleidoscopeDb] Cleared all price tracking data");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] ClearAllPriceData failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    #region Sale Record Operations

    /// <summary>
    /// Saves an individual sale record to the database.
    /// </summary>
    public void SaveSaleRecord(int itemId, int worldId, int pricePerUnit, int quantity, bool isHq, int total, string? buyerName = null)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO sale_records (item_id, world_id, price_per_unit, quantity, is_hq, total, timestamp, buyer_name)
                    VALUES ($iid, $wid, $ppu, $qty, $hq, $total, $time, $buyer)";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$wid", worldId);
                cmd.Parameters.AddWithValue("$ppu", pricePerUnit);
                cmd.Parameters.AddWithValue("$qty", quantity);
                cmd.Parameters.AddWithValue("$hq", isHq ? 1 : 0);
                cmd.Parameters.AddWithValue("$total", total);
                cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
                cmd.Parameters.AddWithValue("$buyer", (object?)buyerName ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] SaveSaleRecord failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Saves multiple sale records in a single transaction for better performance.
    /// Reduces lock contention by batching writes together.
    /// </summary>
    public void SaveSaleRecordsBatch(IEnumerable<(int ItemId, int WorldId, int PricePerUnit, int Quantity, bool IsHq, int Total, string? BuyerName)> records)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                using var transaction = _connection.BeginTransaction();
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO sale_records (item_id, world_id, price_per_unit, quantity, is_hq, total, timestamp, buyer_name)
                        VALUES ($iid, $wid, $ppu, $qty, $hq, $total, $time, $buyer)";

                    var iidParam = cmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var widParam = cmd.Parameters.Add("$wid", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var ppuParam = cmd.Parameters.Add("$ppu", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var qtyParam = cmd.Parameters.Add("$qty", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var hqParam = cmd.Parameters.Add("$hq", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var totalParam = cmd.Parameters.Add("$total", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var timeParam = cmd.Parameters.Add("$time", Microsoft.Data.Sqlite.SqliteType.Integer);
                    var buyerParam = cmd.Parameters.Add("$buyer", Microsoft.Data.Sqlite.SqliteType.Text);

                    var now = DateTime.UtcNow.Ticks;

                    foreach (var (itemId, worldId, pricePerUnit, quantity, isHq, total, buyerName) in recordList)
                    {
                        iidParam.Value = itemId;
                        widParam.Value = worldId;
                        ppuParam.Value = pricePerUnit;
                        qtyParam.Value = quantity;
                        hqParam.Value = isHq ? 1 : 0;
                        totalParam.Value = total;
                        timeParam.Value = now;
                        buyerParam.Value = (object?)buyerName ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] SaveSaleRecordsBatch failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Gets the most recent sale price for a specific item, used for filtering price spikes.
    /// Returns the latest price_per_unit or 0 if no sales exist.
    /// </summary>
    public int GetMostRecentSalePrice(int itemId, bool isHq)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT price_per_unit
                    FROM sale_records
                    WHERE item_id = $iid AND is_hq = $hq
                    ORDER BY timestamp DESC
                    LIMIT 1";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$hq", isHq ? 1 : 0);

                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetMostRecentSalePrice failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Gets the most recent sale price for a specific item on a specific world.
    /// Returns the latest price_per_unit or 0 if no sales exist.
    /// </summary>
    public int GetMostRecentSalePriceForWorld(int itemId, int worldId, bool isHq)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT price_per_unit
                    FROM sale_records
                    WHERE item_id = $iid AND world_id = $wid AND is_hq = $hq
                    ORDER BY timestamp DESC
                    LIMIT 1";
                cmd.Parameters.AddWithValue("$iid", itemId);
                cmd.Parameters.AddWithValue("$wid", worldId);
                cmd.Parameters.AddWithValue("$hq", isHq ? 1 : 0);

                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetMostRecentSalePriceForWorld failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Gets the latest sale price for items, optionally filtering by excluded worlds.
    /// Returns item ID -> (LastSaleNq, LastSaleHq) based on the most recent sales.
    /// </summary>
    public Dictionary<int, (int LastSaleNq, int LastSaleHq)> GetLatestSalePrices(
        IEnumerable<int> itemIds, 
        IEnumerable<int>? excludedWorldIds = null,
        TimeSpan? maxAge = null)
    {
        var result = new Dictionary<int, (int, int)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                var itemIdList = itemIds.ToList();
                if (itemIdList.Count == 0) return result;

                var excludedList = excludedWorldIds?.ToList() ?? new List<int>();

                using var cmd = _connection.CreateCommand();
                
                var sql = new System.Text.StringBuilder();
                sql.Append(@"
                    WITH latest_sales AS (
                        SELECT item_id, is_hq, price_per_unit,
                               ROW_NUMBER() OVER (PARTITION BY item_id, is_hq ORDER BY timestamp DESC) as rn
                        FROM sale_records
                        WHERE item_id IN (");
                sql.Append(string.Join(",", itemIdList));
                sql.Append(")");

                if (excludedList.Count > 0)
                {
                    sql.Append(" AND world_id NOT IN (");
                    sql.Append(string.Join(",", excludedList));
                    sql.Append(")");
                }

                if (maxAge.HasValue)
                {
                    var cutoffTicks = (DateTime.UtcNow - maxAge.Value).Ticks;
                    sql.Append(" AND timestamp >= ");
                    sql.Append(cutoffTicks);
                }

                sql.Append(@"
                    )
                    SELECT item_id,
                           MAX(CASE WHEN is_hq = 0 AND rn = 1 THEN price_per_unit END) as sale_nq,
                           MAX(CASE WHEN is_hq = 1 AND rn = 1 THEN price_per_unit END) as sale_hq
                    FROM latest_sales
                    WHERE rn = 1
                    GROUP BY item_id");

                cmd.CommandText = sql.ToString();

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var iid = reader.GetInt32(0);
                    var snq = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    var shq = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    result[iid] = (snq, shq);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetLatestSalePrices failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets sale records for an item, optionally filtering by world and time range.
    /// </summary>
    public List<(long Id, int WorldId, int PricePerUnit, int Quantity, bool IsHq, int Total, DateTime Timestamp, string? BuyerName)> GetSaleRecords(
        int itemId,
        IEnumerable<int>? excludedWorldIds = null,
        DateTime? since = null,
        int? limit = null)
    {
        var result = new List<(long, int, int, int, bool, int, DateTime, string?)>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                var excludedList = excludedWorldIds?.ToList() ?? new List<int>();

                using var cmd = _connection.CreateCommand();
                var sql = new System.Text.StringBuilder();
                sql.Append("SELECT id, world_id, price_per_unit, quantity, is_hq, total, timestamp, buyer_name FROM sale_records WHERE item_id = $iid");
                cmd.Parameters.AddWithValue("$iid", itemId);

                if (excludedList.Count > 0)
                {
                    sql.Append(" AND world_id NOT IN (");
                    sql.Append(string.Join(",", excludedList));
                    sql.Append(")");
                }

                if (since.HasValue)
                {
                    sql.Append(" AND timestamp >= $since");
                    cmd.Parameters.AddWithValue("$since", since.Value.Ticks);
                }

                sql.Append(" ORDER BY timestamp DESC");

                if (limit.HasValue)
                {
                    sql.Append(" LIMIT $limit");
                    cmd.Parameters.AddWithValue("$limit", limit.Value);
                }

                cmd.CommandText = sql.ToString();

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add((
                        reader.GetInt64(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        reader.GetInt32(3),
                        reader.GetInt32(4) == 1,
                        reader.GetInt32(5),
                        new DateTime(reader.GetInt64(6), DateTimeKind.Utc),
                        reader.IsDBNull(7) ? null : reader.GetString(7)
                    ));
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetSaleRecords failed: {ex.Message}", ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Deletes a specific sale record by ID.
    /// </summary>
    /// <param name="id">The ID of the sale record to delete.</param>
    /// <returns>True if the record was deleted, false otherwise.</returns>
    public bool DeleteSaleRecord(long id)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM sale_records WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                var rowsAffected = cmd.ExecuteNonQuery();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] DeleteSaleRecord failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Deletes a specific sale record by ID and recalculates all inventory value history records
    /// that occurred after the sale's timestamp to ensure consistency.
    /// </summary>
    /// <param name="id">The ID of the sale record to delete.</param>
    /// <param name="saleTimestamp">The timestamp of the sale being deleted.</param>
    /// <returns>True if the record was deleted, false otherwise.</returns>
    public bool DeleteSaleRecordWithHistoryCleanup(long id, DateTime saleTimestamp)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var transaction = _connection.BeginTransaction();

                try
                {
                    // First, get the item_id from the sale record we're about to delete
                    int? saleItemId = null;
                    using (var getItemCmd = _connection.CreateCommand())
                    {
                        getItemCmd.Transaction = transaction;
                        getItemCmd.CommandText = "SELECT item_id FROM sale_records WHERE id = $id";
                        getItemCmd.Parameters.AddWithValue("$id", id);
                        var result = getItemCmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            saleItemId = Convert.ToInt32(result);
                        }
                    }

                    if (!saleItemId.HasValue)
                    {
                        transaction.Rollback();
                        return false;
                    }

                    // Delete the sale record
                    using var deleteCmd = _connection.CreateCommand();
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM sale_records WHERE id = $id";
                    deleteCmd.Parameters.AddWithValue("$id", id);
                    var rowsAffected = deleteCmd.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        // Get the new latest sale price for this item (after deletion)
                        int newPrice = 0;
                        using (var priceCmd = _connection.CreateCommand())
                        {
                            priceCmd.Transaction = transaction;
                            priceCmd.CommandText = @"
                                SELECT price_per_unit FROM sale_records 
                                WHERE item_id = $iid 
                                ORDER BY timestamp DESC 
                                LIMIT 1";
                            priceCmd.Parameters.AddWithValue("$iid", saleItemId.Value);
                            var priceResult = priceCmd.ExecuteScalar();
                            if (priceResult != null && priceResult != DBNull.Value)
                            {
                                newPrice = Convert.ToInt32(priceResult);
                            }
                        }

                        // Find all inventory_value_history records at or after the sale timestamp
                        // that have contributions for this item
                        var historyToUpdate = new List<(long HistoryId, long OldQuantity, int OldPrice)>();
                        using (var findCmd = _connection.CreateCommand())
                        {
                            findCmd.Transaction = transaction;
                            findCmd.CommandText = @"
                                SELECT h.id, i.quantity, i.unit_price
                                FROM inventory_value_history h
                                JOIN inventory_value_items i ON i.history_id = h.id
                                WHERE h.timestamp >= $timestamp AND i.item_id = $iid";
                            findCmd.Parameters.AddWithValue("$timestamp", saleTimestamp.Ticks);
                            findCmd.Parameters.AddWithValue("$iid", saleItemId.Value);
                            
                            using var reader = findCmd.ExecuteReader();
                            while (reader.Read())
                            {
                                historyToUpdate.Add((
                                    reader.GetInt64(0),
                                    reader.GetInt64(1),
                                    reader.GetInt32(2)
                                ));
                            }
                        }

                        // Update each affected history record
                        if (historyToUpdate.Count > 0)
                        {
                            using var updateHistoryCmd = _connection.CreateCommand();
                            updateHistoryCmd.Transaction = transaction;
                            updateHistoryCmd.CommandText = @"
                                UPDATE inventory_value_history 
                                SET item_value = item_value - $oldContrib + $newContrib,
                                    total_value = total_value - $oldContrib + $newContrib
                                WHERE id = $hid";
                            
                            var hidParam = updateHistoryCmd.Parameters.Add("$hid", Microsoft.Data.Sqlite.SqliteType.Integer);
                            var oldContribParam = updateHistoryCmd.Parameters.Add("$oldContrib", Microsoft.Data.Sqlite.SqliteType.Integer);
                            var newContribParam = updateHistoryCmd.Parameters.Add("$newContrib", Microsoft.Data.Sqlite.SqliteType.Integer);

                            using var updateItemCmd = _connection.CreateCommand();
                            updateItemCmd.Transaction = transaction;
                            updateItemCmd.CommandText = @"
                                UPDATE inventory_value_items 
                                SET unit_price = $newPrice
                                WHERE history_id = $hid AND item_id = $iid";
                            
                            var itemHidParam = updateItemCmd.Parameters.Add("$hid", Microsoft.Data.Sqlite.SqliteType.Integer);
                            var newPriceParam = updateItemCmd.Parameters.Add("$newPrice", Microsoft.Data.Sqlite.SqliteType.Integer);
                            var iidParam = updateItemCmd.Parameters.Add("$iid", Microsoft.Data.Sqlite.SqliteType.Integer);
                            iidParam.Value = saleItemId.Value;
                            newPriceParam.Value = newPrice;

                            foreach (var (historyId, quantity, oldPrice) in historyToUpdate)
                            {
                                var oldContribution = quantity * oldPrice;
                                var newContribution = quantity * newPrice;

                                hidParam.Value = historyId;
                                oldContribParam.Value = oldContribution;
                                newContribParam.Value = newContribution;
                                updateHistoryCmd.ExecuteNonQuery();

                                itemHidParam.Value = historyId;
                                updateItemCmd.ExecuteNonQuery();
                            }

                            LogService.Debug($"[KaleidoscopeDb] Recalculated {historyToUpdate.Count} inventory value history records for item {saleItemId.Value} (new price: {newPrice})");
                        }
                    }

                    transaction.Commit();
                    return rowsAffected > 0;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] DeleteSaleRecordWithHistoryCleanup failed: {ex.Message}", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the count of sale records in the database.
    /// </summary>
    public int GetSaleRecordCount()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sale_records";
                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetSaleRecordCount failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    #endregion

    /// <summary>
    /// Cleans up old price history data based on retention settings.
    /// </summary>
    public int CleanupOldPriceData(int retentionDays)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                var cutoffTicks = DateTime.UtcNow.AddDays(-retentionDays).Ticks;

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM price_history WHERE timestamp < $cutoff";
                cmd.Parameters.AddWithValue("$cutoff", cutoffTicks);
                var deleted = cmd.ExecuteNonQuery();

                // Also clean inventory value history
                cmd.CommandText = "DELETE FROM inventory_value_history WHERE timestamp < $cutoff";
                deleted += cmd.ExecuteNonQuery();

                // Also clean old sale records
                cmd.CommandText = "DELETE FROM sale_records WHERE timestamp < $cutoff";
                deleted += cmd.ExecuteNonQuery();

                if (deleted > 0)
                {
                    LogService.Debug($"[KaleidoscopeDb] Cleaned up {deleted} old price/value/sale records");
                }

                return deleted;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] CleanupOldPriceData failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Gets the approximate size of price data in bytes.
    /// </summary>
    public long GetPriceDataSize()
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT 
                        (SELECT COUNT(*) FROM item_prices) * 50 +
                        (SELECT COUNT(*) FROM price_history) * 30 +
                        (SELECT COUNT(*) FROM inventory_value_history) * 40 +
                        (SELECT COUNT(*) FROM sale_records) * 45";
                
                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] GetPriceDataSize failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// Cleans up price data to fit within size limit.
    /// </summary>
    public int CleanupPriceDataBySize(long maxSizeBytes)
    {
        var currentSize = GetPriceDataSize();
        if (currentSize <= maxSizeBytes) return 0;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return 0;

            try
            {
                // Delete oldest records until we're under the limit
                var totalDeleted = 0;
                while (currentSize > maxSizeBytes)
                {
                    using var cmd = _connection.CreateCommand();
                    
                    // Delete oldest price history first
                    cmd.CommandText = @"
                        DELETE FROM price_history 
                        WHERE id IN (SELECT id FROM price_history ORDER BY timestamp ASC LIMIT 1000)";
                    var deleted = cmd.ExecuteNonQuery();
                    
                    // Also delete oldest sale records
                    cmd.CommandText = @"
                        DELETE FROM sale_records 
                        WHERE id IN (SELECT id FROM sale_records ORDER BY timestamp ASC LIMIT 1000)";
                    deleted += cmd.ExecuteNonQuery();
                    
                    totalDeleted += deleted;

                    if (deleted == 0) break;
                    currentSize = GetPriceDataSize();
                }

                if (totalDeleted > 0)
                {
                    LogService.Debug($"[KaleidoscopeDb] Cleaned up {totalDeleted} price history/sale records to fit size limit");
                }

                return totalDeleted;
            }
            catch (Exception ex)
            {
                LogService.Error($"[KaleidoscopeDb] CleanupPriceDataBySize failed: {ex.Message}", ex);
                return 0;
            }
        }
    }

    #endregion

    public void Dispose()
    {
        lock (_writeLock)
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
        
        lock (_readLock)
        {
            _readConnection?.Close();
            _readConnection?.Dispose();
            _readConnection = null;
        }
        
        GC.SuppressFinalize(this);
    }
}
