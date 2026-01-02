using Dalamud.Plugin.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Static logging facade for components without DI access (e.g., static methods, libraries).
/// This is a fallback mechanism for static contexts - prefer injecting IPluginLog directly.
/// </summary>
/// <remarks>
/// This pattern is used by InventoryTools and other Dalamud plugins for logging in static
/// contexts where dependency injection is not available. It should be initialized early
/// in the plugin lifecycle and used sparingly.
/// 
/// Supports category-based filtering to reduce log noise. Categories can be enabled/disabled
/// in the Developer section of the config window.
/// 
/// Supports optional file logging to write logs to an external file in the plugin directory.
/// </remarks>
public static class LogService
{
    private static IPluginLog? _log;
    private static Configuration? _config;
    private static StreamWriter? _fileWriter;
    private static readonly object _fileLock = new();
    private static string? _logFilePath;
    private static long _currentFileSize;

    /// <summary>
    /// Gets whether the log service has been initialized.
    /// </summary>
    public static bool IsInitialized => _log != null;

    /// <summary>
    /// Gets whether file logging is currently active.
    /// </summary>
    public static bool IsFileLoggingActive => _fileWriter != null;

    /// <summary>
    /// Gets the current log file path, if file logging is enabled.
    /// </summary>
    public static string? LogFilePath => _logFilePath;

    /// <summary>
    /// Initializes the static log service. Should be called once during plugin startup.
    /// </summary>
    public static void Initialize(IPluginLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Sets the configuration for category-based filtering.
    /// Should be called after ConfigurationService is available.
    /// </summary>
    public static void SetConfiguration(Configuration config)
    {
        _config = config;
        UpdateFileLogging();
    }

    /// <summary>
    /// Updates file logging state based on current configuration.
    /// Call this after changing file logging settings.
    /// </summary>
    public static void UpdateFileLogging()
    {
        if (_config == null) return;

        var shouldBeEnabled = _config.FileLoggingEnabled;
        var filePath = FilenameService.Instance?.LogFilePath;

        if (shouldBeEnabled && filePath != null && _fileWriter == null)
        {
            EnableFileLogging(filePath);
        }
        else if (!shouldBeEnabled && _fileWriter != null)
        {
            DisableFileLogging();
        }
    }

    /// <summary>
    /// Enables file logging to the specified path.
    /// </summary>
    private static void EnableFileLogging(string filePath)
    {
        lock (_fileLock)
        {
            try
            {
                // Ensure directory exists
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Check if we need to rotate
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    _currentFileSize = fileInfo.Length;
                }
                else
                {
                    _currentFileSize = 0;
                }

                _fileWriter = new StreamWriter(filePath, append: true) { AutoFlush = true };
                _logFilePath = filePath;
                
                WriteToFile("INF", $"=== File logging started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }
            catch (Exception ex)
            {
                _log?.Error($"[LogService] Failed to enable file logging: {ex.Message}");
                _fileWriter = null;
                _logFilePath = null;
            }
        }
    }

    /// <summary>
    /// Disables file logging and closes the file.
    /// </summary>
    private static void DisableFileLogging()
    {
        lock (_fileLock)
        {
            try
            {
                if (_fileWriter != null)
                {
                    WriteToFile("INF", $"=== File logging stopped at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    _fileWriter.Close();
                    _fileWriter.Dispose();
                }
            }
            catch (Exception)
            {
                // Ignore errors during close
            }
            finally
            {
                _fileWriter = null;
                _logFilePath = null;
            }
        }
    }

    /// <summary>
    /// Writes a message to the log file if file logging is enabled.
    /// </summary>
    private static void WriteToFile(string level, string message)
    {
        if (_fileWriter == null || _config == null) return;

        lock (_fileLock)
        {
            try
            {
                // Check for rotation
                var maxBytes = _config.FileLoggingMaxSizeMB * 1024 * 1024;
                if (_currentFileSize > maxBytes && _logFilePath != null)
                {
                    RotateLogFile();
                }

                string line;
                if (_config.FileLoggingIncludeTimestamps)
                {
                    line = $"{DateTime.Now:HH:mm:ss.fff} | {level} | {message}";
                }
                else
                {
                    line = $"{level} | {message}";
                }

                _fileWriter?.WriteLine(line);
                _currentFileSize += line.Length + Environment.NewLine.Length;
            }
            catch (Exception)
            {
                // Ignore write errors to avoid recursion
            }
        }
    }

    /// <summary>
    /// Rotates the log file by renaming the current file and starting a new one.
    /// </summary>
    private static void RotateLogFile()
    {
        if (_logFilePath == null || _fileWriter == null) return;

        try
        {
            _fileWriter.Close();
            _fileWriter.Dispose();
            _fileWriter = null;

            // Rename current file with timestamp
            var rotatedPath = Path.Combine(
                Path.GetDirectoryName(_logFilePath) ?? "",
                $"kaleidoscope_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            
            if (File.Exists(_logFilePath))
            {
                File.Move(_logFilePath, rotatedPath);
            }

            // Start new file
            _fileWriter = new StreamWriter(_logFilePath, append: false) { AutoFlush = true };
            _currentFileSize = 0;
            
            WriteToFile("INF", $"=== Log rotated, previous log: {Path.GetFileName(rotatedPath)} ===");
        }
        catch (Exception)
        {
            // Try to recover by just opening a new file
            try
            {
                _fileWriter = new StreamWriter(_logFilePath, append: true) { AutoFlush = true };
            }
            catch (Exception)
            {
                // Give up on file logging
                _fileWriter = null;
            }
        }
    }

    /// <summary>
    /// Flushes and closes the file writer. Call during plugin shutdown.
    /// </summary>
    public static void Shutdown()
    {
        DisableFileLogging();
    }

    /// <summary>
    /// Checks if logging is enabled for the specified category.
    /// </summary>
    public static bool IsCategoryEnabled(LogCategory category)
    {
        if (_config == null || !_config.LogCategoryFilteringEnabled)
            return true; // If filtering disabled, all categories pass through
        
        return (_config.EnabledLogCategories & category) != 0;
    }

    // Original methods (no category filtering - for backwards compatibility)
    public static void Verbose(string message)
    {
        _log?.Verbose(message);
        WriteToFile("VRB", message);
    }
    
    public static void Info(string message)
    {
        _log?.Information(message);
        WriteToFile("INF", message);
    }
    
    public static void Debug(string message)
    {
        _log?.Debug(message);
        WriteToFile("DBG", message);
    }
    
    public static void Warning(string message)
    {
        _log?.Warning(message);
        WriteToFile("WRN", message);
    }
    
    public static void Error(string message)
    {
        _log?.Error(message);
        WriteToFile("ERR", message);
    }
    
    public static void Error(string message, Exception ex)
    {
        var msg = $"{message}: {ex.Message}";
        _log?.Error(msg);
        WriteToFile("ERR", msg);
    }
    
    public static void Fatal(string message, Exception ex)
    {
        var msg = $"{message}: {ex}";
        _log?.Fatal(msg);
        WriteToFile("FTL", msg);
    }

    // Category-aware logging methods
    
    /// <summary>
    /// Logs a verbose message if the specified category is enabled.
    /// </summary>
    public static void Verbose(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Verbose(message);
            WriteToFile("VRB", message);
        }
    }

    /// <summary>
    /// Logs an info message if the specified category is enabled.
    /// </summary>
    public static void Info(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Information(message);
            WriteToFile("INF", message);
        }
    }

    /// <summary>
    /// Logs a debug message if the specified category is enabled.
    /// </summary>
    public static void Debug(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Debug(message);
            WriteToFile("DBG", message);
        }
    }

    /// <summary>
    /// Logs a warning message if the specified category is enabled.
    /// </summary>
    public static void Warning(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Warning(message);
            WriteToFile("WRN", message);
        }
    }

    /// <summary>
    /// Logs an error message if the specified category is enabled.
    /// Note: Errors are typically always logged, but this allows filtering if desired.
    /// </summary>
    public static void Error(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Error(message);
            WriteToFile("ERR", message);
        }
    }

    /// <summary>
    /// Logs an error message with exception if the specified category is enabled.
    /// </summary>
    public static void Error(LogCategory category, string message, Exception ex)
    {
        if (IsCategoryEnabled(category))
        {
            var msg = $"{message}: {ex.Message}";
            _log?.Error(msg);
            WriteToFile("ERR", msg);
        }
    }
}
