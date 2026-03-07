using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services.Database;

public sealed partial class KaleidoscopeDbService
{

    public sealed class RawQueryResult
    {
        public bool Success { get; init; }
        
        public string? ErrorMessage { get; init; }
        
        public List<string> Columns { get; init; } = new();
        
        public List<List<string?>> Rows { get; init; } = new();
        
        public int RowsAffected { get; init; }
        
        public double ExecutionTimeMs { get; init; }
        
        /// <summary>Whether this was a SELECT query (has result set) or a modification query.</summary>
        public bool IsSelectQuery { get; init; }
    }

    /// <summary>
    /// Executes a raw SQL query for developer debugging purposes.
    /// Supports both SELECT queries (returning rows) and modification queries (INSERT/UPDATE/DELETE).
    /// </summary>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="maxRows">Maximum number of rows to return (default 1000, max 10000).</param>
    /// <returns>Query result with columns, rows, and execution info.</returns>
    /// <remarks>
    /// WARNING: This method is intended for developer debugging only.
    /// It provides direct database access without parameter sanitization.
    /// Use with caution - malformed queries can corrupt data.
    /// </remarks>
    public RawQueryResult ExecuteRawQuery(string sql, int maxRows = 1000)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        maxRows = Math.Clamp(maxRows, 1, 10000);

        if (string.IsNullOrWhiteSpace(sql))
        {
            return new RawQueryResult
            {
                Success = false,
                ErrorMessage = "Query cannot be empty."
            };
        }

        // Determine if this is a SELECT query (read) or a modification query (write)
        var trimmedSql = sql.TrimStart();
        
        // For CTEs (WITH clauses), check the final statement after the CTE definition
        // to determine if it's a read or write operation
        bool isSelectQuery;
        if (trimmedSql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            // Find the final statement after the last closing paren of the CTE
            // Look for INSERT/UPDATE/DELETE/REPLACE after the CTE body
            var upperSql = trimmedSql.ToUpperInvariant();
            // Search for write keywords that appear outside the CTE definition
            // A CTE body ends before the final SELECT/INSERT/UPDATE/DELETE
            var hasWriteKeyword = false;
            var parenDepth = 0;
            for (int i = 4; i < upperSql.Length - 5; i++) // Start after "WITH"
            {
                if (upperSql[i] == '(') parenDepth++;
                else if (upperSql[i] == ')') { parenDepth--; if (parenDepth < 0) parenDepth = 0; }
                
                // Only check keywords at paren depth 0 (outside CTE bodies)
                if (parenDepth == 0 && (i == 4 || char.IsWhiteSpace(upperSql[i - 1]) || upperSql[i - 1] == ')'))
                {
                    var remaining = upperSql.AsSpan(i);
                    if (remaining.StartsWith("INSERT") || remaining.StartsWith("UPDATE") || 
                        remaining.StartsWith("DELETE") || remaining.StartsWith("REPLACE"))
                    {
                        hasWriteKeyword = true;
                        break;
                    }
                }
            }
            isSelectQuery = !hasWriteKeyword;
        }
        else
        {
            isSelectQuery = trimmedSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                           trimmedSql.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase) ||
                           trimmedSql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            if (isSelectQuery)
            {
                return ExecuteSelectQuery(sql, maxRows, stopwatch);
            }
            else
            {
                return ExecuteModificationQuery(sql, stopwatch);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogService.Warning(LogCategory.Database, $"[KaleidoscopeDb] Raw query failed: {ex.Message}");
            return new RawQueryResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                IsSelectQuery = isSelectQuery
            };
        }
    }

    private RawQueryResult ExecuteSelectQuery(string sql, int maxRows, System.Diagnostics.Stopwatch stopwatch)
    {
        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null)
            {
                return new RawQueryResult
                {
                    Success = false,
                    ErrorMessage = "Database connection not available."
                };
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var columns = new List<string>();
            var rows = new List<List<string?>>();

            using var reader = cmd.ExecuteReader();

            // Get column names
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            // Read rows (up to maxRows)
            int rowCount = 0;
            while (reader.Read() && rowCount < maxRows)
            {
                var row = new List<string?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        row.Add(null);
                    }
                    else
                    {
                        var value = reader.GetValue(i);
                        row.Add(value?.ToString());
                    }
                }
                rows.Add(row);
                rowCount++;
            }

            stopwatch.Stop();

            return new RawQueryResult
            {
                Success = true,
                Columns = columns,
                Rows = rows,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                IsSelectQuery = true
            };
        }
    }

    private RawQueryResult ExecuteModificationQuery(string sql, System.Diagnostics.Stopwatch stopwatch)
    {
        lock (_writeLock)
        {
            if (_connection == null)
            {
                return new RawQueryResult
                {
                    Success = false,
                    ErrorMessage = "Database connection not available."
                };
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            var rowsAffected = cmd.ExecuteNonQuery();

            stopwatch.Stop();

            return new RawQueryResult
            {
                Success = true,
                RowsAffected = rowsAffected,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                IsSelectQuery = false
            };
        }
    }

    public List<string> GetTableNames()
    {
        var tables = new List<string>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return tables;

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetTableNames failed: {ex.Message}");
            }
        }

        return tables;
    }

    /// <summary>
    /// Per-table size breakdown entry.
    /// </summary>
    /// <param name="TableName">Name of the database table.</param>
    /// <param name="RowCount">Number of rows in the table.</param>
    /// <param name="SizeBytes">Estimated size in bytes (proportional to row count vs total DB).</param>
    public sealed record TableSizeInfo(string TableName, long RowCount, long SizeBytes);

    /// <summary>
    /// Gets a per-table size breakdown for all user tables.
    /// Row counts are exact; byte sizes are estimated proportionally from total DB file size.
    /// </summary>
    /// <returns>List of per-table size info, sorted by size descending.</returns>
    public List<TableSizeInfo> GetTableSizes()
    {
        var results = new List<TableSizeInfo>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return results;

            try
            {
                // Get total database page count and page size
                long totalPages = 0;
                long pageSize = 0;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT page_count, page_size FROM pragma_page_count(), pragma_page_size()";
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        totalPages = reader.GetInt64(0);
                        pageSize = reader.GetInt64(1);
                    }
                }

                var totalDbSize = totalPages * pageSize;

                // Get all user table names (exclude internal sqlite tables)
                var tableNames = new List<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        tableNames.Add(reader.GetString(0));
                }

                if (tableNames.Count == 0) return results;

                // Get row counts for each table
                long totalRows = 0;
                var rowCounts = new List<(string name, long count)>();

                foreach (var table in tableNames)
                {
                    // Table names are from sqlite_master, safe to interpolate
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
                    var count = (long)(cmd.ExecuteScalar() ?? 0);
                    rowCounts.Add((table, count));
                    totalRows += count;
                }

                // Estimate per-table size proportionally from total DB file size
                // If we have the actual file, use file size (includes WAL); otherwise use page-based size
                long fileSizeTotal = totalDbSize;
                if (!string.IsNullOrEmpty(_dbPath))
                {
                    try
                    {
                        if (File.Exists(_dbPath))
                        {
                            fileSizeTotal = new FileInfo(_dbPath).Length;
                            var walPath = _dbPath + "-wal";
                            if (File.Exists(walPath))
                                fileSizeTotal += new FileInfo(walPath).Length;
                        }
                    }
                    catch (Exception) { /* fall back to page-based size */ }
                }

                foreach (var (name, count) in rowCounts)
                {
                    var estimatedSize = totalRows > 0
                        ? (long)((double)count / totalRows * fileSizeTotal)
                        : 0;
                    results.Add(new TableSizeInfo(name, count, estimatedSize));
                }

                // Sort by size descending
                results.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetTableSizes failed: {ex.Message}");
            }
        }

        return results;
    }

    public List<(string Name, string Type, bool NotNull, string? DefaultValue, bool IsPrimaryKey)> GetTableSchema(string tableName)
    {
        var columns = new List<(string Name, string Type, bool NotNull, string? DefaultValue, bool IsPrimaryKey)>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return columns;

            try
            {
                // Sanitize table name — only allow alphanumeric and underscores to prevent injection
                if (!System.Text.RegularExpressions.Regex.IsMatch(tableName, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                {
                    return columns;
                }
                
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({tableName})";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.GetString(1);
                    var type = reader.GetString(2);
                    var notNull = reader.GetInt32(3) == 1;
                    var defaultValue = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var isPk = reader.GetInt32(5) == 1;
                    columns.Add((name, type, notNull, defaultValue, isPk));
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetTableSchema failed: {ex.Message}");
            }
        }

        return columns;
    }

}
