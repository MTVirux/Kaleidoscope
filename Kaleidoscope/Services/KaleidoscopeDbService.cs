using Microsoft.Data.Sqlite;
using OtterGui.Services;
using System.Text;

namespace Kaleidoscope.Services;

/// <summary>
/// Centralized database service for Kaleidoscope plugin data persistence.
/// Provides thread-safe access to the SQLite database for storing time-series data
/// such as gil tracking, inventory snapshots, currency tracking, and other plugin data.
/// </summary>
/// <remarks>
/// Uses WAL mode with a separate read-only connection for better concurrent read performance.
/// The write connection uses a lock to ensure single-writer semantics.
/// The read connection can operate concurrently with writes due to WAL mode.
/// </remarks>
public sealed partial class KaleidoscopeDbService : IDisposable, IRequiredService
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
    /// Creates a new database service using configured settings.
    /// </summary>
    /// <param name="filenames">Service providing file paths.</param>
    /// <param name="configService">Configuration service for cache size settings.</param>
    public KaleidoscopeDbService(FilenameService filenames, ConfigurationService configService)
    {
        _dbPath = filenames.DatabasePath;
        var cacheSizeMb = configService.CurrencyTrackerConfig.DatabaseCacheSizeMb;
        // Clamp to reasonable range and convert to KB
        cacheSizeMb = Math.Clamp(cacheSizeMb, 1, 64);
        _cacheSizeKb = cacheSizeMb * 1000; // Convert MB to KB (approximate)
        EnsureConnection();
    }

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
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Failed to initialize database: {ex.Message}", ex);
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
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Failed to initialize read connection: {ex.Message}");
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
            LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Migration failed: {ex.Message}", ex);
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
            LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Migration: Added last_sale_nq column to item_prices");
        }

        if (!hasLastSaleHq)
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE item_prices ADD COLUMN last_sale_hq INTEGER NOT NULL DEFAULT 0";
            alterCmd.ExecuteNonQuery();
            LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Migration: Added last_sale_hq column to item_prices");
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
            LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Migration: Created inventory_value_items table");
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
            LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Migration: Added display_name column to character_names");
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
            LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Migration: Added time_series_color column to character_names");
        }
    }


    public void Dispose()
    {
        // Checkpoint before closing to merge WAL into main database
        try
        {
            Checkpoint();
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Checkpoint on dispose failed: {ex.Message}");
        }

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
