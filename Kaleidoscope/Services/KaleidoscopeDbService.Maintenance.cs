using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Performs a WAL checkpoint to merge the WAL file back into the main database.
    /// This temporarily closes the read connection to allow a full checkpoint.
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
            // Get WAL size before checkpoint
            if (File.Exists(walPath))
                walSizeBefore = new FileInfo(walPath).Length;

            // Close the read connection to allow full checkpoint
            lock (_readLock)
            {
                _readConnection?.Close();
                _readConnection?.Dispose();
                _readConnection = null;
            }

            // Perform TRUNCATE checkpoint - this merges WAL and resets it to zero bytes
            lock (_writeLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                cmd.ExecuteNonQuery();
            }

            // Reopen the read connection
            EnsureReadConnection();

            // Get WAL size after checkpoint to calculate reclaimed space
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
            try { EnsureReadConnection(); } catch { /* ignore */ }
            
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
            // First checkpoint to merge WAL
            var (checkpointSuccess, walReclaimed) = Checkpoint();
            if (!checkpointSuccess)
                return (false, 0);

            // Get database size before VACUUM
            long sizeBefore = 0;
            if (File.Exists(_dbPath))
                sizeBefore = new FileInfo(_dbPath).Length;

            // Perform VACUUM - this rewrites the entire database
            lock (_writeLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "VACUUM";
                cmd.ExecuteNonQuery();
            }

            // Get size after VACUUM
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
