using System.Globalization;

namespace Kaleidoscope.Services.Profiler;

/// <summary>
/// Thread-safe append-only text writer with size-based rotation. On rotation the current file
/// is renamed (by default with a UTC timestamp) and a fresh file is opened; a configured header
/// is re-emitted on every fresh file. The rotation naming scheme and an optional post-rotation
/// callback are configurable so callers (e.g. LogService) can preserve their own conventions.
/// The max size can optionally be supplied by a provider so callers can re-read a live config
/// value each write instead of capturing it at construction. Dalamud-free so it can be unit tested.
/// </summary>
public sealed class RotatingFileWriter : IDisposable
{
    private readonly string _filePath;
    private readonly Func<long> _maxBytesProvider;
    private readonly string? _headerLine;
    private readonly Func<string, string> _rotatedPathProvider;
    private readonly Action<string>? _onRotated;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private long _currentSize;

    public RotatingFileWriter(
        string filePath,
        int maxSizeMB,
        string? headerLine = null,
        Func<string, string>? rotatedPathProvider = null,
        Action<string>? onRotated = null,
        Func<long>? maxBytesProvider = null)
    {
        _filePath = filePath;
        var fixedMaxBytes = (long)Math.Max(1, maxSizeMB) * 1024 * 1024;
        _maxBytesProvider = maxBytesProvider ?? (() => fixedMaxBytes);
        _headerLine = headerLine;
        _rotatedPathProvider = rotatedPathProvider ?? DefaultRotatedPath;
        _onRotated = onRotated;
        Open();
    }

    private void Open()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _currentSize = File.Exists(_filePath) ? new FileInfo(_filePath).Length : 0;
            _writer = new StreamWriter(_filePath, append: true) { AutoFlush = true };

            if (_headerLine != null && _currentSize == 0)
            {
                _writer.WriteLine(_headerLine);
                _currentSize += _headerLine.Length + Environment.NewLine.Length;
            }
        }
        catch
        {
            _writer = null;
        }
    }

    public void WriteLine(string line)
    {
        lock (_lock)
        {
            if (_writer == null) return;
            try
            {
                if (_currentSize > _maxBytesProvider())
                    Rotate();

                _writer!.WriteLine(line);
                _currentSize += line.Length + Environment.NewLine.Length;
            }
            catch
            {
                // Swallow: file output must never disrupt the caller.
            }
        }
    }

    private void Rotate()
    {
        string? rotated = null;
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            rotated = _rotatedPathProvider(_filePath);
            if (File.Exists(_filePath))
                File.Move(_filePath, rotated);
        }
        catch
        {
            // If rename fails, fall through and reopen the existing file.
            rotated = null;
        }

        Open();

        if (rotated != null)
            _onRotated?.Invoke(rotated);
    }

    private static string DefaultRotatedPath(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(dir, $"{baseName}_{stamp}{ext}");
    }

    public void Close()
    {
        lock (_lock)
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // Ignore.
            }
            finally
            {
                _writer = null;
            }
        }
    }

    public void Dispose() => Close();
}
