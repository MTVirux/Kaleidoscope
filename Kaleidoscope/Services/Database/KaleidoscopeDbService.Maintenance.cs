using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{

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
            catch { /* ignore */ }
            
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
