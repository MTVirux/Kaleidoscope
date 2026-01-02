using System.Collections.Concurrent;
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
/// Supports splitting logs by category and/or by character.
/// </remarks>
public static class LogService
{
    private static IPluginLog? _log;
    private static Configuration? _config;
    
    // Main file writer (when not splitting)
    private static StreamWriter? _fileWriter;
    private static readonly object _fileLock = new();
    private static string? _logFilePath;
    private static long _currentFileSize;

    // Category-specific file writers
    private static readonly ConcurrentDictionary<LogCategory, CategoryLogWriter> _categoryWriters = new();

    // Character-specific file writers (keyed by sanitized character name)
    private static readonly ConcurrentDictionary<string, CharacterLogWriter> _characterWriters = new();

    // Thread-local current character context for per-character logging
    [ThreadStatic]
    private static string? _currentCharacterName;

    /// <summary>
    /// Gets whether the log service has been initialized.
    /// </summary>
    public static bool IsInitialized => _log != null;

    /// <summary>
    /// Gets whether file logging is currently active (main file or split files).
    /// </summary>
    public static bool IsFileLoggingActive => _fileWriter != null || _categoryWriters.Count > 0 || _characterWriters.Count > 0;

    /// <summary>
    /// Gets the current main log file path, if file logging is enabled.
    /// </summary>
    public static string? LogFilePath => _logFilePath;

    /// <summary>
    /// Gets the number of active category log files.
    /// </summary>
    public static int ActiveCategoryWriters => _categoryWriters.Count;

    /// <summary>
    /// Gets the number of active character log files.
    /// </summary>
    public static int ActiveCharacterWriters => _characterWriters.Count;

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
    /// Sets the current character context for per-character logging.
    /// Call this when switching characters or when character context is known.
    /// </summary>
    /// <param name="characterName">The character name, or null to clear context.</param>
    public static void SetCurrentCharacter(string? characterName)
    {
        _currentCharacterName = characterName;
    }

    /// <summary>
    /// Gets the current character name for logging context.
    /// </summary>
    public static string? CurrentCharacterName => _currentCharacterName;

    /// <summary>
    /// Updates file logging state based on current configuration.
    /// Call this after changing file logging settings.
    /// </summary>
    public static void UpdateFileLogging()
    {
        if (_config == null) return;

        var shouldBeEnabled = _config.FileLoggingEnabled;
        var splitByCategory = _config.FileLoggingSplitByCategory;
        var splitByCharacter = _config.FileLoggingSplitByCharacter;

        lock (_fileLock)
        {
            if (!shouldBeEnabled)
            {
                // Disable all file logging
                DisableAllFileLogging();
                return;
            }

            // If splitting is enabled, close main writer
            if (splitByCategory || splitByCharacter)
            {
                if (_fileWriter != null)
                {
                    CloseMainWriter();
                }
                // Category/character writers are created on-demand
            }
            else
            {
                // Close any split writers and use main writer only
                CloseAllSplitWriters();
                
                var filePath = FilenameService.Instance?.LogFilePath;
                if (filePath != null)
                {
                    if (_fileWriter == null)
                    {
                        EnableFileLogging(filePath);
                    }
                    else if (_logFilePath != filePath)
                    {
                        CloseMainWriter();
                        EnableFileLogging(filePath);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Enables file logging to the specified path.
    /// </summary>
    private static void EnableFileLogging(string filePath)
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
            
            WriteToMainFile("INF", $"=== File logging started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }
        catch (Exception ex)
        {
            _log?.Error($"[LogService] Failed to enable file logging: {ex.Message}");
            _fileWriter = null;
            _logFilePath = null;
        }
    }

    /// <summary>
    /// Closes the main file writer.
    /// </summary>
    private static void CloseMainWriter()
    {
        try
        {
            if (_fileWriter != null)
            {
                WriteToMainFile("INF", $"=== File logging stopped at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _fileWriter.Flush();
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

    /// <summary>
    /// Closes all split writers (category and character).
    /// </summary>
    private static void CloseAllSplitWriters()
    {
        foreach (var writer in _categoryWriters.Values)
        {
            writer.Close();
        }
        _categoryWriters.Clear();

        foreach (var writer in _characterWriters.Values)
        {
            writer.Close();
        }
        _characterWriters.Clear();
    }

    /// <summary>
    /// Disables all file logging and closes all files.
    /// </summary>
    private static void DisableAllFileLogging()
    {
        CloseMainWriter();
        CloseAllSplitWriters();
    }

    /// <summary>
    /// Writes a message to the main log file if file logging is enabled.
    /// </summary>
    private static void WriteToMainFile(string level, string message)
    {
        if (_fileWriter == null || _config == null) return;

        try
        {
            // Check for rotation
            var maxBytes = _config.FileLoggingMaxSizeMB * 1024 * 1024;
            if (_currentFileSize > maxBytes && _logFilePath != null)
            {
                RotateMainLogFile();
            }

            var line = FormatLogLine(level, message);
            _fileWriter?.WriteLine(line);
            _currentFileSize += line.Length + Environment.NewLine.Length;
        }
        catch (Exception)
        {
            // Ignore write errors to avoid recursion
        }
    }

    /// <summary>
    /// Writes a message to the appropriate file(s) based on configuration.
    /// </summary>
    private static void WriteToFile(string level, string message, LogCategory category = LogCategory.None, string? characterName = null)
    {
        if (_config == null || !_config.FileLoggingEnabled) return;

        lock (_fileLock)
        {
            try
            {
                var splitByCategory = _config.FileLoggingSplitByCategory;
                var splitByCharacter = _config.FileLoggingSplitByCharacter;

                // Use provided character name or fall back to current context
                var charName = characterName ?? _currentCharacterName;

                if (splitByCategory && splitByCharacter && category != LogCategory.None && !string.IsNullOrEmpty(charName))
                {
                    // Write to character + category file
                    WriteToCharacterCategoryFile(level, message, charName, category);
                }
                else if (splitByCharacter && !string.IsNullOrEmpty(charName))
                {
                    // Write to character file only
                    WriteToCharacterFile(level, message, charName);
                }
                else if (splitByCategory && category != LogCategory.None)
                {
                    // Write to category file only
                    WriteToCategoryFile(level, message, category);
                }
                else
                {
                    // Write to main file
                    WriteToMainFile(level, message);
                }
            }
            catch (Exception)
            {
                // Ignore write errors to avoid recursion
            }
        }
    }

    /// <summary>
    /// Writes to a category-specific log file.
    /// </summary>
    private static void WriteToCategoryFile(string level, string message, LogCategory category)
    {
        var writer = GetOrCreateCategoryWriter(category);
        writer?.WriteLine(level, message, _config);
    }

    /// <summary>
    /// Writes to a character-specific log file.
    /// </summary>
    private static void WriteToCharacterFile(string level, string message, string characterName)
    {
        var writer = GetOrCreateCharacterWriter(characterName, null);
        writer?.WriteLine(level, message, _config);
    }

    /// <summary>
    /// Writes to a character + category specific log file.
    /// </summary>
    private static void WriteToCharacterCategoryFile(string level, string message, string characterName, LogCategory category)
    {
        var writer = GetOrCreateCharacterWriter(characterName, category);
        writer?.WriteLine(level, message, _config);
    }

    /// <summary>
    /// Gets or creates a category-specific log writer.
    /// </summary>
    private static CategoryLogWriter? GetOrCreateCategoryWriter(LogCategory category)
    {
        if (_categoryWriters.TryGetValue(category, out var existing))
            return existing;

        var filePath = FilenameService.Instance?.GetCategoryLogFilePath(category);
        if (filePath == null) return null;

        var writer = new CategoryLogWriter(category, filePath, _config?.FileLoggingMaxSizeMB ?? 10);
        if (_categoryWriters.TryAdd(category, writer))
            return writer;

        // Another thread created it first
        writer.Close();
        return _categoryWriters.TryGetValue(category, out existing) ? existing : null;
    }

    /// <summary>
    /// Gets or creates a character-specific log writer.
    /// </summary>
    private static CharacterLogWriter? GetOrCreateCharacterWriter(string characterName, LogCategory? category)
    {
        var key = category.HasValue ? $"{characterName}_{category.Value}" : characterName;
        
        if (_characterWriters.TryGetValue(key, out var existing))
            return existing;

        string? filePath;
        if (category.HasValue)
        {
            filePath = FilenameService.Instance?.GetCharacterCategoryLogFilePath(characterName, category.Value);
        }
        else
        {
            filePath = FilenameService.Instance?.GetCharacterLogFilePath(characterName);
        }
        
        if (filePath == null) return null;

        var writer = new CharacterLogWriter(characterName, filePath, _config?.FileLoggingMaxSizeMB ?? 10);
        if (_characterWriters.TryAdd(key, writer))
            return writer;

        // Another thread created it first
        writer.Close();
        return _characterWriters.TryGetValue(key, out existing) ? existing : null;
    }

    /// <summary>
    /// Formats a log line with optional timestamp.
    /// </summary>
    private static string FormatLogLine(string level, string message)
    {
        if (_config?.FileLoggingIncludeTimestamps == true)
        {
            return $"{DateTime.Now:HH:mm:ss.fff} | {level} | {message}";
        }
        return $"{level} | {message}";
    }

    /// <summary>
    /// Rotates the main log file by renaming the current file and starting a new one.
    /// </summary>
    private static void RotateMainLogFile()
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
            
            WriteToMainFile("INF", $"=== Log rotated, previous log: {Path.GetFileName(rotatedPath)} ===");
        }
        catch (Exception)
        {
            // Try to recover by just opening a new file
            try
            {
                if (_logFilePath != null)
                {
                    _fileWriter = new StreamWriter(_logFilePath, append: true) { AutoFlush = true };
                }
            }
            catch (Exception)
            {
                // Give up on file logging
                _fileWriter = null;
            }
        }
    }

    /// <summary>
    /// Flushes and closes all file writers. Call during plugin shutdown.
    /// </summary>
    public static void Shutdown()
    {
        lock (_fileLock)
        {
            DisableAllFileLogging();
        }
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
            WriteToFile("VRB", message, category);
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
            WriteToFile("INF", message, category);
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
            WriteToFile("DBG", message, category);
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
            WriteToFile("WRN", message, category);
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
            WriteToFile("ERR", message, category);
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
            WriteToFile("ERR", msg, category);
        }
    }

    // Character-aware logging methods

    /// <summary>
    /// Logs a debug message for a specific character.
    /// </summary>
    public static void Debug(LogCategory category, string characterName, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Debug(message);
            WriteToFile("DBG", message, category, characterName);
        }
    }

    /// <summary>
    /// Logs an info message for a specific character.
    /// </summary>
    public static void Info(LogCategory category, string characterName, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Information(message);
            WriteToFile("INF", message, category, characterName);
        }
    }

    #region Helper Classes

    /// <summary>
    /// Manages a log file for a specific category.
    /// </summary>
    private sealed class CategoryLogWriter
    {
        private StreamWriter? _writer;
        private readonly object _writerLock = new();
        private readonly string _filePath;
        private readonly int _maxSizeMB;
        private long _currentSize;

        public LogCategory Category { get; }

        public CategoryLogWriter(LogCategory category, string filePath, int maxSizeMB)
        {
            Category = category;
            _filePath = filePath;
            _maxSizeMB = maxSizeMB;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(_filePath))
                {
                    _currentSize = new FileInfo(_filePath).Length;
                }

                _writer = new StreamWriter(_filePath, append: true) { AutoFlush = true };
            }
            catch (Exception)
            {
                _writer = null;
            }
        }

        public void WriteLine(string level, string message, Configuration? config)
        {
            lock (_writerLock)
            {
                if (_writer == null) return;

                try
                {
                    var maxBytes = _maxSizeMB * 1024 * 1024;
                    if (_currentSize > maxBytes)
                    {
                        Rotate();
                    }

                    string line;
                    if (config?.FileLoggingIncludeTimestamps == true)
                    {
                        line = $"{DateTime.Now:HH:mm:ss.fff} | {level} | {message}";
                    }
                    else
                    {
                        line = $"{level} | {message}";
                    }

                    _writer.WriteLine(line);
                    _currentSize += line.Length + Environment.NewLine.Length;
                }
                catch (Exception)
                {
                    // Ignore write errors
                }
            }
        }

        private void Rotate()
        {
            try
            {
                _writer?.Flush();
                _writer?.Close();
                _writer?.Dispose();
                _writer = null;

                var baseName = Path.GetFileNameWithoutExtension(_filePath);
                var dir = Path.GetDirectoryName(_filePath) ?? "";
                var rotatedPath = Path.Combine(dir, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                
                if (File.Exists(_filePath))
                {
                    File.Move(_filePath, rotatedPath);
                }

                _writer = new StreamWriter(_filePath, append: false) { AutoFlush = true };
                _currentSize = 0;
            }
            catch (Exception)
            {
                try
                {
                    _writer = new StreamWriter(_filePath, append: true) { AutoFlush = true };
                }
                catch (Exception)
                {
                    _writer = null;
                }
            }
        }

        public void Close()
        {
            lock (_writerLock)
            {
                try
                {
                    _writer?.Flush();
                    _writer?.Close();
                    _writer?.Dispose();
                }
                catch (Exception)
                {
                    // Ignore
                }
                finally
                {
                    _writer = null;
                }
            }
        }
    }

    /// <summary>
    /// Manages a log file for a specific character.
    /// </summary>
    private sealed class CharacterLogWriter
    {
        private StreamWriter? _writer;
        private readonly object _writerLock = new();
        private readonly string _filePath;
        private readonly int _maxSizeMB;
        private long _currentSize;

        public string CharacterName { get; }

        public CharacterLogWriter(string characterName, string filePath, int maxSizeMB)
        {
            CharacterName = characterName;
            _filePath = filePath;
            _maxSizeMB = maxSizeMB;
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(_filePath))
                {
                    _currentSize = new FileInfo(_filePath).Length;
                }

                _writer = new StreamWriter(_filePath, append: true) { AutoFlush = true };
            }
            catch (Exception)
            {
                _writer = null;
            }
        }

        public void WriteLine(string level, string message, Configuration? config)
        {
            lock (_writerLock)
            {
                if (_writer == null) return;

                try
                {
                    var maxBytes = _maxSizeMB * 1024 * 1024;
                    if (_currentSize > maxBytes)
                    {
                        Rotate();
                    }

                    string line;
                    if (config?.FileLoggingIncludeTimestamps == true)
                    {
                        line = $"{DateTime.Now:HH:mm:ss.fff} | {level} | {message}";
                    }
                    else
                    {
                        line = $"{level} | {message}";
                    }

                    _writer.WriteLine(line);
                    _currentSize += line.Length + Environment.NewLine.Length;
                }
                catch (Exception)
                {
                    // Ignore write errors
                }
            }
        }

        private void Rotate()
        {
            try
            {
                _writer?.Flush();
                _writer?.Close();
                _writer?.Dispose();
                _writer = null;

                var baseName = Path.GetFileNameWithoutExtension(_filePath);
                var dir = Path.GetDirectoryName(_filePath) ?? "";
                var rotatedPath = Path.Combine(dir, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                
                if (File.Exists(_filePath))
                {
                    File.Move(_filePath, rotatedPath);
                }

                _writer = new StreamWriter(_filePath, append: false) { AutoFlush = true };
                _currentSize = 0;
            }
            catch (Exception)
            {
                try
                {
                    _writer = new StreamWriter(_filePath, append: true) { AutoFlush = true };
                }
                catch (Exception)
                {
                    _writer = null;
                }
            }
        }

        public void Close()
        {
            lock (_writerLock)
            {
                try
                {
                    _writer?.Flush();
                    _writer?.Close();
                    _writer?.Dispose();
                }
                catch (Exception)
                {
                    // Ignore
                }
                finally
                {
                    _writer = null;
                }
            }
        }
    }

    #endregion
}
