using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{
    /// <summary>
    /// Result of a database integrity check.
    /// </summary>
    public sealed class IntegrityCheckResult
    {
        public bool IsHealthy { get; init; }
        public List<string> Errors { get; init; } = new();
        public string Summary => IsHealthy ? "ok" : $"{Errors.Count} error(s) found";
    }

    /// <summary>
    /// Runs PRAGMA quick_check on the database to detect corruption.
    /// quick_check is faster than integrity_check — it skips verifying that table content
    /// matches indexes, but still catches most forms of corruption.
    /// </summary>
    /// <returns>An IntegrityCheckResult indicating whether the database is healthy.</returns>
    public IntegrityCheckResult QuickCheck()
    {
        if (_connection == null || string.IsNullOrEmpty(_dbPath))
            return new IntegrityCheckResult { IsHealthy = false, Errors = { "No database connection" } };

        try
        {
            lock (_readLock)
            {
                var conn = _readConnection ?? _connection;
                var errors = new List<string>();

                using var cmd = conn!.CreateCommand();
                cmd.CommandText = "PRAGMA quick_check";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var result = reader.GetString(0);
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                        errors.Add(result);
                }

                var isHealthy = errors.Count == 0;
                if (isHealthy)
                    LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Quick integrity check passed");
                else
                    LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Quick integrity check FAILED with {errors.Count} error(s)");

                return new IntegrityCheckResult { IsHealthy = isHealthy, Errors = errors };
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Quick integrity check threw: {ex.Message}", ex);
            return new IntegrityCheckResult { IsHealthy = false, Errors = { $"Exception: {ex.Message}" } };
        }
    }

    /// <summary>
    /// Runs PRAGMA integrity_check on the database. This is thorough but can be slow
    /// on large databases as it verifies every page, B-tree, index, and constraint.
    /// Prefer QuickCheck() for startup/periodic checks.
    /// </summary>
    /// <param name="maxErrors">Maximum number of errors to report (default 100).</param>
    /// <returns>An IntegrityCheckResult indicating whether the database is healthy.</returns>
    public IntegrityCheckResult FullIntegrityCheck(int maxErrors = 100)
    {
        if (_connection == null || string.IsNullOrEmpty(_dbPath))
            return new IntegrityCheckResult { IsHealthy = false, Errors = { "No database connection" } };

        try
        {
            lock (_readLock)
            {
                var conn = _readConnection ?? _connection;
                var errors = new List<string>();

                using var cmd = conn!.CreateCommand();
                cmd.CommandText = $"PRAGMA integrity_check({maxErrors})";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var result = reader.GetString(0);
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                        errors.Add(result);
                }

                var isHealthy = errors.Count == 0;
                if (isHealthy)
                    LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Full integrity check passed");
                else
                    LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Full integrity check FAILED with {errors.Count} error(s)");

                return new IntegrityCheckResult { IsHealthy = isHealthy, Errors = errors };
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Full integrity check threw: {ex.Message}", ex);
            return new IntegrityCheckResult { IsHealthy = false, Errors = { $"Exception: {ex.Message}" } };
        }
    }

    /// <summary>
    /// Creates a backup copy of the database file. Closes the read connection and performs
    /// a WAL checkpoint first to ensure the backup contains all committed data.
    /// </summary>
    /// <param name="backupPath">Destination path. If null, appends a timestamp to the DB filename.</param>
    /// <returns>A tuple of (success, backupPath, sizeBytes).</returns>
    public (bool Success, string? Path, long SizeBytes) BackupDatabase(string? backupPath = null)
    {
        if (string.IsNullOrEmpty(_dbPath) || !File.Exists(_dbPath))
            return (false, null, 0);

        try
        {
            backupPath ??= $"{_dbPath}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";

            // Checkpoint WAL so the backup is a single self-contained file
            Checkpoint();

            File.Copy(_dbPath, backupPath, overwrite: false);

            var size = new FileInfo(backupPath).Length;
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Database backed up to {backupPath} ({size:N0} bytes)");
            return (true, backupPath, size);
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Backup failed: {ex.Message}", ex);
            return (false, backupPath, 0);
        }
    }

    /// <summary>
    /// Attempts to recover a corrupt database by dumping salvageable data into a new database file.
    /// Uses ".recover" command semantics: opens a new DB, copies all readable rows from the old DB.
    /// The old database is renamed to ".corrupt.bak" and the recovered database takes its place.
    /// Both connections are closed and reopened against the recovered database.
    /// </summary>
    /// <returns>A tuple of (success, message) describing the outcome.</returns>
    public (bool Success, string Message) RepairDatabase()
    {
        if (string.IsNullOrEmpty(_dbPath))
            return (false, "No database path configured");

        if (!File.Exists(_dbPath))
            return (false, "Database file does not exist");

        var corruptBackupPath = $"{_dbPath}.{DateTime.Now:yyyyMMdd_HHmmss}.corrupt.bak";
        var recoveredPath = _dbPath + ".recovery";

        try
        {
            // 1. Stop checkpoint timer
            _checkpointTimer?.Dispose();
            _checkpointTimer = null;

            // 2. Prevent concurrent threads from reopening connections during repair.
            //    Background workers (UniversalisService, CurrencyTrackerService) will
            //    acquire _writeLock and call EnsureConnection the instant we release it.
            //    The _repairing flag makes EnsureConnection/EnsureReadConnection return
            //    immediately, keeping the file unlocked for File.Move operations.
            _repairing = true;

            // 3. Close both connections, THEN clear their pool entries.
            //    Order matters: Close() returns the handle to the pool, then ClearPool()
            //    evicts it. If ClearPool is called first, Close() just puts it back.
            lock (_writeLock)
            {
                lock (_readLock)
                {
                    if (_readConnection != null)
                    {
                        _readConnection.Close();
                        _readConnection.Dispose();
                        SqliteConnection.ClearPool(_readConnection);
                        _readConnection = null;
                    }

                    if (_connection != null)
                    {
                        _connection.Close();
                        _connection.Dispose();
                        SqliteConnection.ClearPool(_connection);
                        _connection = null;
                    }
                }
            }

            // Force GC to finalize any leaked native sqlite3 handles that might
            // keep the file locked even after pool clearing (especially on large DBs).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // 3. Open old DB in read-only mode with ignore-corruption workarounds.
            //    Pooling=false ensures Close()/Dispose() immediately releases OS file handles
            //    instead of returning them to the connection pool. This is critical —
            //    without it, File.Move/Delete below will fail with "file in use" on Windows.
            var oldCsb = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            var newCsb = new SqliteConnectionStringBuilder
            {
                DataSource = recoveredPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            int tablesRecovered = 0;
            int tablesFailed = 0;
            var details = new StringBuilder();

            using (var oldConn = new SqliteConnection(oldCsb.ToString()))
            using (var newConn = new SqliteConnection(newCsb.ToString()))
            {
                oldConn.Open();
                newConn.Open();

                // Set PRAGMAs on the new database
                ExecutePragma(newConn, "PRAGMA journal_mode = WAL");
                ExecutePragma(newConn, "PRAGMA foreign_keys = OFF"); // OFF during recovery to avoid FK ordering issues

                // Get all table schemas from the old database
                var tables = new List<(string Name, string Sql)>();
                using (var cmd = oldConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var name = reader.GetString(0);
                        var sql = reader.IsDBNull(1) ? null : reader.GetString(1);
                        if (sql != null)
                            tables.Add((name, sql));
                    }
                }

                // Get all index schemas
                var indexes = new List<string>();
                using (var cmd = oldConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        indexes.Add(reader.GetString(0));
                }

                // Create tables in new database
                foreach (var (name, sql) in tables)
                {
                    try
                    {
                        using var createCmd = newConn.CreateCommand();
                        createCmd.CommandText = sql;
                        createCmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        details.AppendLine($"Failed to create table {name}: {ex.Message}");
                    }
                }

                // Copy data table by table
                foreach (var (name, _) in tables)
                {
                    try
                    {
                        long rowsCopied = 0;

                        using var selectCmd = oldConn.CreateCommand();
                        selectCmd.CommandText = $"SELECT * FROM [{name}]";
                        using var reader = selectCmd.ExecuteReader();

                        // Build INSERT statement from column metadata
                        var columns = new List<string>();
                        for (int i = 0; i < reader.FieldCount; i++)
                            columns.Add(reader.GetName(i));

                        var columnList = string.Join(", ", columns.Select(c => $"[{c}]"));
                        var paramList = string.Join(", ", columns.Select((_, i) => $"$p{i}"));

                        using var transaction = newConn.BeginTransaction();
                        while (reader.Read())
                        {
                            try
                            {
                                using var insertCmd = newConn.CreateCommand();
                                insertCmd.Transaction = transaction;
                                insertCmd.CommandText = $"INSERT OR IGNORE INTO [{name}] ({columnList}) VALUES ({paramList})";

                                for (int i = 0; i < reader.FieldCount; i++)
                                    insertCmd.Parameters.AddWithValue($"$p{i}", reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i));

                                insertCmd.ExecuteNonQuery();
                                rowsCopied++;
                            }
                            catch
                            {
                                // Skip individual corrupt rows
                            }
                        }
                        transaction.Commit();

                        details.AppendLine($"  {name}: {rowsCopied} rows recovered");
                        tablesRecovered++;
                    }
                    catch (Exception ex)
                    {
                        details.AppendLine($"  {name}: FAILED — {ex.Message}");
                        tablesFailed++;
                    }
                }

                // Recreate indexes
                foreach (var indexSql in indexes)
                {
                    try
                    {
                        using var idxCmd = newConn.CreateCommand();
                        idxCmd.CommandText = indexSql;
                        idxCmd.ExecuteNonQuery();
                    }
                    catch { /* best effort */ }
                }

                // Re-enable foreign keys
                ExecutePragma(newConn, "PRAGMA foreign_keys = ON");

                // Checkpoint WAL into the recovery file so it's a single self-contained DB.
                // This avoids needing to move WAL/SHM files during the swap.
                ExecutePragma(newConn, "PRAGMA wal_checkpoint(TRUNCATE)");

                // Explicitly close connections. With Pooling=false (set above), this
                // immediately releases the native SQLite handles and OS file locks.
                newConn.Close();
                oldConn.Close();
            }

            // 4. Swap files: rename corrupt → .corrupt.bak, rename recovery → original.
            //    Use retry loop because Windows may briefly hold file handles after close.
            MoveFileWithRetry(_dbPath, corruptBackupPath);

            // Also move WAL/SHM files if they exist
            var walPath = _dbPath + "-wal";
            var shmPath = _dbPath + "-shm";
            if (File.Exists(walPath))
                MoveFileWithRetry(walPath, corruptBackupPath + "-wal");
            if (File.Exists(shmPath))
                MoveFileWithRetry(shmPath, corruptBackupPath + "-shm");

            // Delete leftover recovery WAL/SHM (should be empty after TRUNCATE checkpoint above)
            DeleteFileQuiet(recoveredPath + "-wal");
            DeleteFileQuiet(recoveredPath + "-shm");

            MoveFileWithRetry(recoveredPath, _dbPath);

            // 5. Allow connections to be reopened again, then reopen.
            _repairing = false;
            EnsureConnection();

            // 6. Run migrations on the recovered database
            if (_connection != null)
                RunMigrations();

            // 7. Reset corruption flag after successful repair
            _isCorrupt = false;

            var message = $"Recovery complete: {tablesRecovered} tables recovered, {tablesFailed} failed. Corrupt DB saved to {Path.GetFileName(corruptBackupPath)}\n{details}";
            LogService.Warning(message);
            return (true, message);
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Database repair failed: {ex.Message}", ex);

            // Try to reopen the original file if still available
            try
            {
                // Clean up partial recovery file and its WAL/SHM siblings.
                // With Pooling=false on repair connections, handles are already released.
                DeleteFileQuiet(recoveredPath);
                DeleteFileQuiet(recoveredPath + "-wal");
                DeleteFileQuiet(recoveredPath + "-shm");

                // If original was moved, move it back
                if (!File.Exists(_dbPath) && File.Exists(corruptBackupPath))
                    File.Move(corruptBackupPath, _dbPath);

                _repairing = false;
                EnsureConnection();
            }
            catch (Exception reopenEx)
            {
                LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Failed to restore connection after repair failure: {reopenEx.Message}", reopenEx);
            }
            finally
            {
                // Ensure _repairing is always cleared so the DB isn't permanently locked out
                _repairing = false;
            }

            return (false, $"Repair failed: {ex.Message}");
        }
    }

    private static void ExecutePragma(SqliteConnection conn, string pragma)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = pragma;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Moves a file with retry logic. Windows may briefly hold file handles after
    /// SQLite connections are closed, even with Pooling=false. Retries up to 5 times
    /// with increasing delays (100ms, 200ms, 400ms, 800ms, 1600ms) before giving up.
    /// </summary>
    private static void MoveFileWithRetry(string source, string destination, int maxRetries = 5)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                File.Move(source, destination);
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                Thread.Sleep(100 * (1 << attempt)); // 100, 200, 400, 800, 1600ms
            }
        }
    }

    /// <summary>
    /// Deletes a file if it exists, silently ignoring any errors.
    /// </summary>
    private static void DeleteFileQuiet(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Performs a non-blocking WAL checkpoint using PASSIVE mode.
    /// Does not block readers or writers — only checkpoints pages that are not in use.
    /// Suitable for periodic maintenance during normal operation.
    /// Use Checkpoint() (TRUNCATE mode) only during Dispose for a full checkpoint.
    /// </summary>
    /// <returns>True if the checkpoint was executed successfully.</returns>
    public bool CheckpointPassive()
    {
        if (_connection == null || string.IsNullOrEmpty(_dbPath))
            return false;

        try
        {
            lock (_writeLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE)";
                cmd.ExecuteNonQuery();
            }

            LogService.Debug(LogCategory.Database, "[KaleidoscopeDb] Passive WAL checkpoint complete");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Passive WAL checkpoint failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Performs a WAL checkpoint to merge the WAL file back into the main database.
    /// This temporarily closes the read connection to allow a full checkpoint.
    /// Acquires both locks (write → read) and holds them for the entire operation
    /// to prevent concurrent readers from falling back to _connection during the checkpoint.
    /// </summary>
    /// <returns>A tuple containing (success, bytesReclaimed) where bytesReclaimed is the approximate WAL size before checkpoint.</returns>
    public (bool Success, long BytesReclaimed) Checkpoint()
    {
        if (_connection == null || string.IsNullOrEmpty(_dbPath))
            return (false, 0);

        long walSizeBefore = 0;
        var walPath = _dbPath + "-wal";

        try
        {
            if (File.Exists(walPath))
                walSizeBefore = new FileInfo(walPath).Length;

            // Lock ordering: always acquire _writeLock before _readLock to prevent deadlocks.
            // Hold both locks for the entire duration to prevent concurrent readers from seeing
            // _readConnection == null and falling back to _connection during the checkpoint.
            lock (_writeLock)
            {
                lock (_readLock)
                {
                    _readConnection?.Close();
                    _readConnection?.Dispose();
                    _readConnection = null;

                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                    cmd.ExecuteNonQuery();

                    EnsureReadConnection();
                }
            }

            long walSizeAfter = 0;
            if (File.Exists(walPath))
                walSizeAfter = new FileInfo(walPath).Length;

            var bytesReclaimed = walSizeBefore - walSizeAfter;
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] Checkpoint complete: reclaimed {bytesReclaimed:N0} bytes from WAL");
            
            return (true, bytesReclaimed);
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] Checkpoint failed: {ex.Message}", ex);
            
            // Try to reopen read connection even on failure
            try
            {
                lock (_readLock)
                {
                    EnsureReadConnection();
                }
            }
            catch (Exception) { /* ignore read connection recovery failure */ }
            
            return (false, 0);
        }
    }

    /// <summary>
    /// Performs a full database optimization: checkpoint followed by VACUUM.
    /// VACUUM rebuilds the database file, reclaiming space from deleted records.
    /// This operation can take several seconds for large databases.
    /// </summary>
    /// <returns>A tuple containing (success, bytesReclaimed) where bytesReclaimed is the approximate space saved.</returns>
    public (bool Success, long BytesReclaimed) VacuumWithStats()
    {
        if (_connection == null || string.IsNullOrEmpty(_dbPath))
            return (false, 0);

        try
        {
            var (checkpointSuccess, walReclaimed) = Checkpoint();
            if (!checkpointSuccess)
                return (false, 0);

            long sizeBefore = 0;
            if (File.Exists(_dbPath))
                sizeBefore = new FileInfo(_dbPath).Length;

            lock (_writeLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "VACUUM";
                cmd.ExecuteNonQuery();
            }

            long sizeAfter = 0;
            if (File.Exists(_dbPath))
                sizeAfter = new FileInfo(_dbPath).Length;

            var dbReclaimed = sizeBefore - sizeAfter;
            var totalReclaimed = walReclaimed + dbReclaimed;
            
            LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] VacuumWithStats complete: reclaimed {dbReclaimed:N0} bytes from DB, {walReclaimed:N0} bytes from WAL");
            
            return (true, totalReclaimed);
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Database, $"[KaleidoscopeDb] VacuumWithStats failed: {ex.Message}", ex);
            return (false, 0);
        }
    }

}
