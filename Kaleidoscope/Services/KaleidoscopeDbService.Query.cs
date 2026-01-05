using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Result of a raw SQL query execution.
    /// </summary>
    public sealed class RawQueryResult
    {
        /// <summary>Whether the query executed successfully.</summary>
        public bool Success { get; init; }
        
        /// <summary>Error message if query failed.</summary>
        public string? ErrorMessage { get; init; }
        
        /// <summary>Column names returned by the query.</summary>
        public List<string> Columns { get; init; } = new();
        
        /// <summary>Result rows, each row is a list of string values.</summary>
        public List<List<string?>> Rows { get; init; } = new();
        
        /// <summary>Number of rows affected (for UPDATE/INSERT/DELETE).</summary>
        public int RowsAffected { get; init; }
        
        /// <summary>Time taken to execute the query in milliseconds.</summary>
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
        var isSelectQuery = trimmedSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                           trimmedSql.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase) ||
                           trimmedSql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase) ||
                           trimmedSql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase); // CTEs

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

    /// <summary>
    /// Gets a list of all tables in the database.
    /// </summary>
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
    /// Gets the schema (column info) for a specific table.
    /// </summary>
    public List<(string Name, string Type, bool NotNull, string? DefaultValue, bool IsPrimaryKey)> GetTableSchema(string tableName)
    {
        var columns = new List<(string Name, string Type, bool NotNull, string? DefaultValue, bool IsPrimaryKey)>();

        lock (_readLock)
        {
            var conn = _readConnection ?? _connection;
            if (conn == null) return columns;

            try
            {
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
