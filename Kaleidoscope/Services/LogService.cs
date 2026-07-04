using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using Kaleidoscope.Services.Profiler;

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
    private static FilenameService? _filenames;
    
    // Main file writer (when not splitting)
    private static RotatingFileWriter? _mainWriter;
    private static readonly object _fileLock = new();
    private static string? _logFilePath;

    // Category-specific file writers
    private static readonly ConcurrentDictionary<LogCategory, CategoryLogWriter> _categoryWriters = new();

    // Character-specific file writers (keyed by sanitized character name)
    private static readonly ConcurrentDictionary<string, CharacterLogWriter> _characterWriters = new();

    // Async-local current character context for per-character logging.
    // AsyncLocal flows across await continuations, unlike ThreadStatic which is lost
    // when async code resumes on a different thread pool thread.
    private static readonly AsyncLocal<string?> _currentCharacterContext = new();

    public static bool IsInitialized => _log != null;

    public static bool IsFileLoggingActive => _mainWriter != null || _categoryWriters.Count > 0 || _characterWriters.Count > 0;

    public static string? LogFilePath => _logFilePath;

    public static int ActiveCategoryWriters => _categoryWriters.Count;

    public static int ActiveCharacterWriters => _characterWriters.Count;

    /// <summary>
    /// Initializes the static log service. Should be called once during plugin startup.
    /// </summary>
    public static void Initialize(IPluginLog log, FilenameService filenames)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _filenames = filenames ?? throw new ArgumentNullException(nameof(filenames));
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
        _currentCharacterContext.Value = characterName;
    }

    public static string? CurrentCharacterName => _currentCharacterContext.Value;

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
                if (_mainWriter != null)
                {
                    CloseMainWriter();
                }
                // Category/character writers are created on-demand
            }
            else
            {
                // Close any split writers and use main writer only
                CloseAllSplitWriters();
                
                var filePath = _filenames?.LogFilePath;
                if (filePath != null)
                {
                    if (_mainWriter == null)
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

    private static void EnableFileLogging(string filePath)
    {
        try
        {
            _mainWriter = new RotatingFileWriter(
                filePath,
                _config?.FileLoggingMaxSizeMB ?? 10,
                headerLine: null,
                rotatedPathProvider: LogRotatedPath,
                onRotated: OnMainRotated,
                // Re-read the configured max size each write so mid-session changes apply
                // without re-enabling file logging. Split writers keep the fixed capture.
                maxBytesProvider: () => (long)Math.Max(1, _config?.FileLoggingMaxSizeMB ?? 10) * 1024 * 1024);
            _logFilePath = filePath;

            WriteToMainFile("INF", $"=== File logging started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }
        catch (Exception ex)
        {
            _log?.Error($"[LogService] Failed to enable file logging: {ex.Message}");
            _mainWriter = null;
            _logFilePath = null;
        }
    }

    private static void CloseMainWriter()
    {
        try
        {
            if (_mainWriter != null)
            {
                WriteToMainFile("INF", $"=== File logging stopped at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _mainWriter.Close();
            }
        }
        catch (Exception)
        {
            // Ignore errors during close
        }
        finally
        {
            _mainWriter = null;
            _logFilePath = null;
        }
    }

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

    private static void DisableAllFileLogging()
    {
        CloseMainWriter();
        CloseAllSplitWriters();
    }

    private static void WriteToMainFile(string level, string message)
    {
        _mainWriter?.WriteLine(FormatLogLine(level, message, _config));
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
                var charName = characterName ?? _currentCharacterContext.Value;

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

    private static void WriteToCategoryFile(string level, string message, LogCategory category)
    {
        var writer = GetOrCreateCategoryWriter(category);
        writer?.WriteLine(level, message, _config);
    }

    private static void WriteToCharacterFile(string level, string message, string characterName)
    {
        var writer = GetOrCreateCharacterWriter(characterName, null);
        writer?.WriteLine(level, message, _config);
    }

    private static void WriteToCharacterCategoryFile(string level, string message, string characterName, LogCategory category)
    {
        var writer = GetOrCreateCharacterWriter(characterName, category);
        writer?.WriteLine(level, message, _config);
    }

    private static CategoryLogWriter? GetOrCreateCategoryWriter(LogCategory category)
    {
        if (_categoryWriters.TryGetValue(category, out var existing))
            return existing;

        var filePath = _filenames?.GetCategoryLogFilePath(category);
        if (filePath == null) return null;

        var writer = new CategoryLogWriter(category, filePath, _config?.FileLoggingMaxSizeMB ?? 10);
        if (_categoryWriters.TryAdd(category, writer))
            return writer;

        // Another thread created it first
        writer.Close();
        return _categoryWriters.TryGetValue(category, out existing) ? existing : null;
    }

    private static CharacterLogWriter? GetOrCreateCharacterWriter(string characterName, LogCategory? category)
    {
        var key = category.HasValue ? $"{characterName}_{category.Value}" : characterName;
        
        if (_characterWriters.TryGetValue(key, out var existing))
            return existing;

        string? filePath;
        if (category.HasValue)
        {
            filePath = _filenames?.GetCharacterCategoryLogFilePath(characterName, category.Value);
        }
        else
        {
            filePath = _filenames?.GetCharacterLogFilePath(characterName);
        }
        
        if (filePath == null) return null;

        var writer = new CharacterLogWriter(characterName, filePath, _config?.FileLoggingMaxSizeMB ?? 10);
        if (_characterWriters.TryAdd(key, writer))
            return writer;

        // Another thread created it first
        writer.Close();
        return _characterWriters.TryGetValue(key, out existing) ? existing : null;
    }

    private static string FormatLogLine(string level, string message, Configuration? config)
    {
        if (config?.FileLoggingIncludeTimestamps == true)
        {
            return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {level} | {message}";
        }
        return $"{level} | {message}";
    }

    private static void OnMainRotated(string rotatedPath)
    {
        WriteToMainFile("INF", $"=== Log rotated, previous log: {Path.GetFileName(rotatedPath)} ===");
    }

    /// <summary>
    /// Computes the rotated file path for LogService files: the base name plus a local-time
    /// timestamp with the .log extension. The main log file is kaleidoscope.log, so this
    /// reproduces the previous "kaleidoscope_{timestamp}.log" naming for it as well.
    /// </summary>
    private static string LogRotatedPath(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(dir, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
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
    
    public static void Verbose(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Verbose(message);
            WriteToFile("VRB", message, category);
        }
    }

    /// <summary>
    /// Logs a verbose message if the specified category is enabled.
    /// Uses deferred evaluation to avoid string interpolation cost when the category is disabled.
    /// Usage: LogService.Verbose(LogCategory.Database, () => $"Expensive {data} to format");
    /// </summary>
    public static void Verbose(LogCategory category, Func<string> messageFactory)
    {
        if (IsCategoryEnabled(category))
        {
            var message = messageFactory();
            _log?.Verbose(message);
            WriteToFile("VRB", message, category);
        }
    }

    public static void Info(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Information(message);
            WriteToFile("INF", message, category);
        }
    }

    /// <summary>
    /// Uses deferred evaluation to avoid string interpolation cost when the category is disabled.
    /// </summary>
    public static void Info(LogCategory category, Func<string> messageFactory)
    {
        if (IsCategoryEnabled(category))
        {
            var message = messageFactory();
            _log?.Information(message);
            WriteToFile("INF", message, category);
        }
    }

    public static void Debug(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Debug(message);
            WriteToFile("DBG", message, category);
        }
    }

    /// <summary>
    /// Uses deferred evaluation to avoid string interpolation cost when the category is disabled.
    /// </summary>
    public static void Debug(LogCategory category, Func<string> messageFactory)
    {
        if (IsCategoryEnabled(category))
        {
            var message = messageFactory();
            _log?.Debug(message);
            WriteToFile("DBG", message, category);
        }
    }

    public static void Warning(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Warning(message);
            WriteToFile("WRN", message, category);
        }
    }

    public static void Error(LogCategory category, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Error(message);
            WriteToFile("ERR", message, category);
        }
    }

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

    public static void Debug(LogCategory category, string characterName, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Debug(message);
            WriteToFile("DBG", message, category, characterName);
        }
    }

    public static void Info(LogCategory category, string characterName, string message)
    {
        if (IsCategoryEnabled(category))
        {
            _log?.Information(message);
            WriteToFile("INF", message, category, characterName);
        }
    }

    /// <summary>
    /// Rotating file log writer for split (per-category / per-character) logs. Delegates file
    /// creation, size-based rotation and IO to <see cref="RotatingFileWriter"/> while keeping
    /// line formatting local via <see cref="FormatLogLine"/>.
    /// CategoryLogWriter and CharacterLogWriter are thin wrappers with identity metadata.
    /// </summary>
    private class RotatingLogWriter
    {
        private readonly RotatingFileWriter _writer;

        public RotatingLogWriter(string filePath, int maxSizeMB)
        {
            _writer = new RotatingFileWriter(
                filePath,
                maxSizeMB,
                headerLine: null,
                rotatedPathProvider: LogRotatedPath);
        }

        public void WriteLine(string level, string message, Configuration? config)
        {
            _writer.WriteLine(FormatLogLine(level, message, config));
        }

        public void Close() => _writer.Close();
    }

    private sealed class CategoryLogWriter : RotatingLogWriter
    {
        public LogCategory Category { get; }

        public CategoryLogWriter(LogCategory category, string filePath, int maxSizeMB)
            : base(filePath, maxSizeMB)
        {
            Category = category;
        }
    }

    private sealed class CharacterLogWriter : RotatingLogWriter
    {
        public string CharacterName { get; }

        public CharacterLogWriter(string characterName, string filePath, int maxSizeMB)
            : base(filePath, maxSizeMB)
        {
            CharacterName = characterName;
        }
    }
}
