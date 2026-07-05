using Microsoft.Data.Sqlite;
using OtterGui.Services;
using System.Text;

namespace Kaleidoscope.Services.Database;

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
    /// <summary>
    /// Lock for write operations on the write connection.
    /// LOCK ORDERING INVARIANT: When both locks are needed, always acquire _writeLock BEFORE _readLock.
    /// Never acquire _writeLock while already holding _readLock (would violate ordering and risk deadlock).
    /// </summary>
    private readonly object _writeLock = new();
    
    /// <summary>
    /// Lock for read operations on the read connection.
    /// When both locks are needed, _writeLock must be acquired first. See _writeLock for invariant.
    /// </summary>
    private readonly object _readLock = new();
    private readonly string? _dbPath;
    private SqliteConnection? _connection;
    private SqliteConnection? _readConnection;
    
    private readonly object _inventoryValueStatsLock = new();
    private long _cachedInventoryValueRecordCount;
    private long? _cachedInventoryValueMaxTimestamp;
    private bool _inventoryValueStatsCacheValid;
    
    private readonly int _cacheSizeKb;
    
    /// <summary>
    /// Timer for periodic PASSIVE WAL checkpoints to keep the WAL file small.
    /// Runs every 5 minutes during normal operation so there's minimal work at dispose.
    /// </summary>
    private Timer? _checkpointTimer;
    
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Indicates that database corruption has been detected (SQLite error 11/26 or malformed disk image).
    /// Set by the startup integrity check or by runtime corruption detection in catch blocks.
    /// Remains true until a successful <see cref="RepairDatabase"/> call resets it.
    /// </summary>
    private volatile bool _isCorrupt;

    /// <summary>
    /// Set to true during RepairDatabase() to prevent concurrent threads from
    /// reopening connections via EnsureConnection/EnsureReadConnection while file
    /// operations (move/delete) are in progress. Without this guard, background
    /// workers (price writes, currency samples) race to reopen the DB file the
    /// instant the locks are released after closing connections.
    /// </summary>
    private volatile bool _repairing;

    /// <summary>
    /// Whether the database is currently in a corrupt state. Check this property to
    /// show persistent warnings in the UI or to gracefully degrade functionality.
    /// </summary>
    public bool IsCorrupt => _isCorrupt;

    /// <summary>
    /// Raised when database corruption is first detected. Fires at most once per corruption episode.
    /// Subscribers can use this to surface a persistent warning in the UI.
    /// </summary>
    public event Action? OnCorruptionDetected;

    public string? DbPath => _dbPath;

    /// <summary>Public accessor for the writer connection — used by services that need direct access (e.g., ResourceDbWriter).</summary>
    public Microsoft.Data.Sqlite.SqliteConnection? GetWriterConnection() => _connection;

    /// <summary>Public accessor for the write lock — used by ResourceDbWriter so FlushOnce() serializes with all other write operations on the same connection.</summary>
    public object WriteLock => _writeLock;

    /// <summary>
    /// Generates parameterized IN clause placeholders ($p0, $p1, ...) and adds corresponding parameters to the command.
    /// Returns the placeholder string for use in SQL (e.g., "$p0, $p1, $p2").
    /// This enables SQLite prepared statement caching and follows parameterization best practices.
    /// </summary>
    private static string AddParameterizedInClause<T>(SqliteCommand cmd, IList<T> values, string prefix = "$p")
    {
        var sb = new StringBuilder();
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var paramName = $"{prefix}{i}";
            sb.Append(paramName);
            cmd.Parameters.AddWithValue(paramName, values[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Executes an action within a database transaction, automatically handling commit/rollback.
    /// Caller must already hold _writeLock and ensure _connection is not null.
    /// </summary>
    /// <param name="action">Action to execute within the transaction. Receives the transaction object.</param>
    /// <returns>True if the transaction committed successfully, false otherwise.</returns>
    private bool RunInTransaction(Action<SqliteTransaction> action)
    {
        if (_connection == null) return false;

        using var transaction = _connection.BeginTransaction();
        try
        {
            action(transaction);
            transaction.Commit();
            return true;
        }
        catch
        {
            try { transaction.Rollback(); }
            catch (Exception rollbackEx)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Transaction rollback failed: {rollbackEx.Message}");
            }
            throw;
        }
    }

    /// <summary>
    /// Executes a function within a database transaction, returning a result.
    /// Caller must already hold _writeLock and ensure _connection is not null.
    /// </summary>
    /// <typeparam name="T">Return type.</typeparam>
    /// <param name="func">Function to execute within the transaction. Receives the transaction object.</param>
    /// <returns>The result of the function.</returns>
    private T RunInTransaction<T>(Func<SqliteTransaction, T> func)
    {
        if (_connection == null) throw new InvalidOperationException("Database connection is not available.");

        using var transaction = _connection.BeginTransaction();
        try
        {
            var result = func(transaction);
            transaction.Commit();
            return result;
        }
        catch
        {
            try { transaction.Rollback(); }
            catch (Exception rollbackEx)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Transaction rollback failed: {rollbackEx.Message}");
            }
            throw;
        }
    }

    /// <summary>
    /// Checks if an exception indicates SQLite database corruption and, if so, sets the
    /// <see cref="IsCorrupt"/> flag and raises <see cref="OnCorruptionDetected"/>.
    /// Call from catch blocks in query/write methods to enable runtime corruption detection.
    /// </summary>
    private void NotifyIfCorruption(Exception ex)
    {
        if (_isCorrupt) return; // Already flagged, don't re-fire event

        bool isCorruption = ex is SqliteException sqliteEx &&
            sqliteEx.SqliteErrorCode is 11 or 26; // SQLITE_CORRUPT or SQLITE_NOTADB

        if (!isCorruption && ex.Message.Contains("database disk image is malformed", StringComparison.OrdinalIgnoreCase))
            isCorruption = true;

        if (isCorruption)
        {
            _isCorrupt = true;
            LogService.Error(LogCategory.Database,
                "[KaleidoscopeDb] DATABASE CORRUPTION DETECTED. " +
                "Use Settings > Storage > Database Health > Repair to attempt recovery.");
            try { OnCorruptionDetected?.Invoke(); }
            catch { /* Don't let subscriber exceptions propagate */ }
        }
    }

    /// <summary>
    /// Logs a database error at Error level and checks for corruption.
    /// Replaces direct LogService.Error calls in catch blocks for uniform corruption detection.
    /// </summary>
    private void LogDbError(string method, Exception ex)
    {
        NotifyIfCorruption(ex);
        LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] {method} failed: {ex.Message}", ex);
    }

    /// <summary>
    /// Logs a database error at Debug level and checks for corruption.
    /// Replaces direct LogService.Debug calls in catch blocks for uniform corruption detection.
    /// </summary>
    private void LogDbDebug(string method, Exception ex)
    {
        NotifyIfCorruption(ex);
        LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] {method} failed: {ex.Message}");
    }

    /// <summary>
    /// Runs a read query against the dedicated read connection, encapsulating lock acquisition,
    /// connection resolution, and uniform error handling. On any failure the <paramref name="fallback"/>
    /// is returned; pass a mutable accumulator (e.g. the result list/dictionary) as the fallback to
    /// preserve any rows gathered before an exception.
    /// </summary>
    /// <remarks>
    /// Thread-safety: the read connection is only ever touched under <c>_readLock</c>. If the dedicated
    /// read connection is unavailable (a prior <see cref="EnsureReadConnection"/> failed), the query is
    /// retried and, as a last resort, run against the writer connection under <c>_writeLock</c> — never
    /// against <c>_connection</c> while only holding <c>_readLock</c>, which would race with writers.
    /// </remarks>
    private T ExecuteRead<T>(string caller, T fallback, Func<SqliteConnection, T> body, bool debugLog = false)
    {
        lock (_readLock)
        {
            var conn = _readConnection;
            if (conn != null)
            {
                try
                {
                    return body(conn);
                }
                catch (Exception ex)
                {
                    if (debugLog) LogDbDebug(caller, ex); else LogDbError(caller, ex);
                    return fallback;
                }
            }
        }

        // Dedicated read connection unavailable — retry opening it, then run this call against the
        // writer connection under _writeLock. Acquiring _writeLock only after releasing _readLock keeps
        // the lock-ordering invariant (write before read) intact.
        lock (_writeLock)
        {
            EnsureConnection();
            if (_readConnection == null)
                EnsureReadConnection();
            if (_connection == null) return fallback;

            try
            {
                return body(_connection);
            }
            catch (Exception ex)
            {
                if (debugLog) LogDbDebug(caller, ex); else LogDbError(caller, ex);
                return fallback;
            }
        }
    }

    /// <summary>
    /// Lock-correct read execution that PROPAGATES body exceptions to the caller, unlike
    /// <see cref="ExecuteRead{T}"/> which swallows them and returns a fallback. Shares the same
    /// connection-resolution and lock discipline: the dedicated read connection is used under
    /// <c>_readLock</c>, and if it is unavailable the query runs against the writer connection under
    /// <c>_writeLock</c> — never against <c>_connection</c> while only holding <c>_readLock</c>, which
    /// would race with writers. Intended for diagnostic/dev readers that build custom result objects or
    /// need exceptions to surface to their own handlers.
    /// </summary>
    private T ExecuteReadThrowing<T>(Func<SqliteConnection, T> body)
    {
        lock (_readLock)
        {
            var conn = _readConnection;
            if (conn != null)
                return body(conn);
        }

        // Dedicated read connection unavailable — retry opening it, then run this call against the
        // writer connection under _writeLock. Acquiring _writeLock only after releasing _readLock keeps
        // the lock-ordering invariant (write before read) intact.
        lock (_writeLock)
        {
            EnsureConnection();
            if (_readConnection == null)
                EnsureReadConnection();
            if (_connection == null)
                throw new InvalidOperationException("Database connection is not available.");

            return body(_connection);
        }
    }

    /// <summary>
    /// Runs a write operation against the writer connection, encapsulating <c>_writeLock</c> acquisition,
    /// <see cref="EnsureConnection"/>, and uniform error handling. Returns <paramref name="fallback"/> on failure.
    /// </summary>
    private T ExecuteWrite<T>(string caller, T fallback, Func<SqliteConnection, T> body, bool debugLog = false)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return fallback;

            try
            {
                return body(_connection);
            }
            catch (Exception ex)
            {
                if (debugLog) LogDbDebug(caller, ex); else LogDbError(caller, ex);
                return fallback;
            }
        }
    }

    /// <summary>
    /// Runs a write operation with the same lock discipline as <see cref="ExecuteWrite{T}"/> but lets
    /// exceptions propagate to the caller instead of swallowing them — the writer-side counterpart of
    /// <see cref="ExecuteReadThrowing{T}"/>. Intended for diagnostic/dev writers that surface errors
    /// through their own result objects.
    /// </summary>
    private T ExecuteWriteThrowing<T>(Func<SqliteConnection, T> body)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null)
                throw new InvalidOperationException("Database connection is not available.");

            return body(_connection);
        }
    }

    /// <summary>
    /// Void overload of <see cref="ExecuteWrite{T}"/> for write operations that don't return a value.
    /// </summary>
    private void ExecuteWrite(string caller, Action<SqliteConnection> body, bool debugLog = false)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return;

            try
            {
                body(_connection);
            }
            catch (Exception ex)
            {
                if (debugLog) LogDbDebug(caller, ex); else LogDbError(caller, ex);
            }
        }
    }

    public KaleidoscopeDbService(FilenameService filenames, ConfigurationService configService)
    {
        _dbPath = filenames.DatabasePath;
        var cacheSizeMb = configService.Config.DatabaseCacheSizeMb;
        cacheSizeMb = Math.Clamp(cacheSizeMb, 1, 64);
        _cacheSizeKb = cacheSizeMb * 1024;
        EnsureConnection();

        // Startup integrity check — detect corruption but do NOT auto-repair.
        // Repair involves ClearAllPools + file moves which is unsafe during DI construction
        // (can nuke connection pools for other services mid-resolution and cascade into
        // ObjectDisposedException). Instead, just set the flag so the UI can direct the user
        // to Settings > Storage > Database Health > Repair.
        if (_connection != null)
        {
            try
            {
                var check = QuickCheck();
                if (!check.IsHealthy)
                {
                    _isCorrupt = true;
                    LogService.Error(LogCategory.Database,
                        $"[KaleidoscopeDb] Startup integrity check FAILED ({check.Errors.Count} error(s)). " +
                        "Use Settings > Storage > Database Health > Repair to attempt recovery.");
                }
                else
                {
                    LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Startup integrity check passed");
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.Database,
                    $"[KaleidoscopeDb] Startup integrity check threw: {ex.Message}", ex);
            }
        }
    }

    private void EnsureConnection()
    {
        if (string.IsNullOrEmpty(_dbPath)) return;
        if (_repairing) return; // Don't reopen while RepairDatabase is doing file operations

        lock (_writeLock)
        {
            if (_connection != null) return;
            if (_repairing) return; // Re-check after acquiring lock

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

                // Cap the WAL file: after each successful checkpoint/reset SQLite truncates
                // it back to this size instead of leaving the high-water mark on disk.
                using (var walLimitCmd = _connection.CreateCommand())
                {
                    walLimitCmd.CommandText = "PRAGMA journal_size_limit = 33554432";
                    walLimitCmd.ExecuteNonQuery();
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

                // Keep temp tables/indexes in memory — benefits CTE/window-function-heavy queries
                using (var tempCmd = _connection.CreateCommand())
                {
                    tempCmd.CommandText = "PRAGMA temp_store = MEMORY";
                    tempCmd.ExecuteNonQuery();
                }

                // Safety net: if SQLite-level lock contention occurs despite C# locks, wait instead of failing
                using (var busyCmd = _connection.CreateCommand())
                {
                    busyCmd.CommandText = "PRAGMA busy_timeout = 5000";
                    busyCmd.ExecuteNonQuery();
                }

                EnsureSchema();

                StartupStorageMaintenance();

                EnsureReadConnection();

                // Start periodic checkpoints to keep WAL small
                _checkpointTimer = new Timer(
                    _ => CheckpointWithTruncateFallback(),
                    null,
                    CheckpointInterval,
                    CheckpointInterval);
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
        if (_repairing) return; // Don't reopen while RepairDatabase is doing file operations
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

            using (var cacheCmd = _readConnection.CreateCommand())
            {
                cacheCmd.CommandText = $"PRAGMA cache_size = -{_cacheSizeKb}";
                cacheCmd.ExecuteNonQuery();
            }

            // Keep temp tables/indexes in memory for CTE/window-function queries
            using (var tempCmd = _readConnection.CreateCommand())
            {
                tempCmd.CommandText = "PRAGMA temp_store = MEMORY";
                tempCmd.ExecuteNonQuery();
            }

            // Safety net for SQLite-level lock contention
            using (var busyCmd = _readConnection.CreateCommand())
            {
                busyCmd.CommandText = "PRAGMA busy_timeout = 5000";
                busyCmd.ExecuteNonQuery();
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

        // TIMESTAMP CONVENTION: every `timestamp`/`updated_at`/`last_updated` column below stores
        // DateTime.UtcNow.Ticks (.NET ticks, 100ns since 0001-01-01 UTC) — NOT unix epoch seconds.
        // Read back via `new DateTime(ticks, DateTimeKind.Utc)`.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS character_names (
    character_id INTEGER PRIMARY KEY,
    name TEXT,
    display_name TEXT,
    time_series_color INTEGER
);

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

CREATE TABLE IF NOT EXISTS inventory_value_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    character_id INTEGER NOT NULL,
    timestamp INTEGER NOT NULL,
    total_value INTEGER NOT NULL DEFAULT 0,
    gil_value INTEGER NOT NULL DEFAULT 0,
    item_value INTEGER NOT NULL DEFAULT 0
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
CREATE INDEX IF NOT EXISTS idx_inventory_value_char ON inventory_value_history(character_id);
CREATE INDEX IF NOT EXISTS idx_inventory_value_timestamp ON inventory_value_history(timestamp);
CREATE INDEX IF NOT EXISTS idx_sale_records_ring ON sale_records(item_id, world_id, is_hq, timestamp DESC);
";
        cmd.ExecuteNonQuery();

        ApplyResourcesSchema();

        RunMigrations();
    }

    /// <summary>
    /// Current schema version. Increment this whenever a new migration is added.
    /// </summary>
    private const int CurrentSchemaVersion = 8; // 1=base, 2=last_sale, 3=value_items, 4=display_name, 5=color, 6=unified_resources, 7=drop_legacy_tables, 8=storage_optimization

    /// <summary>
    /// Runs database migrations for schema updates.
    /// Uses a schema_version table to skip already-applied migrations on startup.
    /// </summary>
    private void RunMigrations()
    {
        if (_connection == null) return;

        try
        {
            using (var createCmd = _connection.CreateCommand())
            {
                createCmd.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)";
                createCmd.ExecuteNonQuery();
            }

            int currentVersion = 0;
            using (var readCmd = _connection.CreateCommand())
            {
                readCmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
                var result = readCmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    currentVersion = Convert.ToInt32(result);
            }

            if (currentVersion >= CurrentSchemaVersion)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Schema is up to date (version {currentVersion})");
                return;
            }

            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Running migrations from version {currentVersion} to {CurrentSchemaVersion}");

            if (currentVersion < 2)
            {
                AddColumnIfMissing("item_prices", "last_sale_nq", "last_sale_nq INTEGER NOT NULL DEFAULT 0");
                AddColumnIfMissing("item_prices", "last_sale_hq", "last_sale_hq INTEGER NOT NULL DEFAULT 0");
            }
            if (currentVersion < 3) MigrateAddInventoryValueItemsTable();
            if (currentVersion < 4) AddColumnIfMissing("character_names", "display_name", "display_name TEXT");
            if (currentVersion < 5) AddColumnIfMissing("character_names", "time_series_color", "time_series_color INTEGER");
            if (currentVersion < 6) MigrateAddUnifiedResources();
            if (currentVersion < 7) MigrateDropLegacyTables();
            if (currentVersion < 8) MigrateStorageOptimization();

            using (var updateCmd = _connection.CreateCommand())
            {
                updateCmd.CommandText = currentVersion == 0
                    ? $"INSERT INTO schema_version (version) VALUES ({CurrentSchemaVersion})"
                    : $"UPDATE schema_version SET version = {CurrentSchemaVersion}";
                updateCmd.ExecuteNonQuery();
            }

            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Migrations complete, schema now at version {CurrentSchemaVersion}");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Migration failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Adds <paramref name="column"/> to <paramref name="table"/> via ALTER TABLE if it is not
    /// already present. Idempotent — reused by the column-adding migrations.
    /// </summary>
    private void AddColumnIfMissing(string table, string column, string columnDdl)
    {
        if (_connection == null) return;

        bool hasColumn = false;
        using (var checkCmd = _connection.CreateCommand())
        {
            checkCmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == column) { hasColumn = true; break; }
            }
        }

        if (!hasColumn)
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {columnDdl}";
            alterCmd.ExecuteNonQuery();
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Migration: Added {column} column to {table}");
        }
    }

    private void MigrateAddInventoryValueItemsTable()
    {
        // v8 drops inventory_value_items; creating it for v3 upgraders is pointless.
    }

    /// <summary>
    /// Migration v6: backfill from legacy inventory_cache/inventory_items and series/points
    /// into the new resources/resource_history tables. The DDL itself runs unconditionally
    /// via ApplyResourcesSchema() before this method (CREATE IF NOT EXISTS) — this method
    /// only handles the data backfill. Tasks 13-15 add the actual backfill SQL; this stub
    /// is a no-op so the version bump can happen while migration content is built up.
    /// </summary>
    private void MigrateAddUnifiedResources()
    {
        BackfillResourcesFromInventoryItems();
        BackfillGilRowsFromInventoryCache();
        BackfillResourceHistoryFromSeries();
        LogService.Debug(LogCategory.Database, "[Migration v6] Unified resources backfill complete");
    }


    public void Dispose()
    {
        _checkpointTimer?.Dispose();
        _checkpointTimer = null;

        // Use PASSIVE checkpoint on dispose — non-blocking, only checkpoints pages not in use.
        // Any remaining WAL is harmless and will be recovered automatically on next startup.
        // Previously used TRUNCATE which forced writing the entire WAL to disk synchronously,
        // causing 30MB/s+ disk I/O spikes and UI freezes during plugin reload.
        try
        {
            CheckpointPassive();
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Passive checkpoint on dispose failed: {ex.Message}");
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
    }
}
