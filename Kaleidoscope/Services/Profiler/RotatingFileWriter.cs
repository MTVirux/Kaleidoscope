using System.Globalization;

namespace Kaleidoscope.Services.Profiler;

/// <summary>
/// Thread-safe append-only text writer with size-based rotation. On rotation the current
/// file is renamed with a UTC timestamp and a fresh file is opened; a configured header is
/// re-emitted on every fresh file. Dalamud-free so it can be unit tested.
/// </summary>
public sealed class RotatingFileWriter : IDisposable
{
    private readonly string _filePath;
    private readonly long _maxBytes;
    private readonly string? _headerLine;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private long _currentSize;

    public RotatingFileWriter(string filePath, int maxSizeMB, string? headerLine = null)
    {
        _filePath = filePath;
        _maxBytes = (long)Math.Max(1, maxSizeMB) * 1024 * 1024;
        _headerLine = headerLine;
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
                if (_currentSize > _maxBytes)
                    Rotate();

                _writer!.WriteLine(line);
                _currentSize += line.Length + Environment.NewLine.Length;
            }
            catch
            {
                // Swallow: profiler file output must never disrupt the caller.
            }
        }
    }

    private void Rotate()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            var dir = Path.GetDirectoryName(_filePath) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(_filePath);
            var ext = Path.GetExtension(_filePath);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var rotated = Path.Combine(dir, $"{baseName}_{stamp}{ext}");
            if (File.Exists(_filePath))
                File.Move(_filePath, rotated);
        }
        catch
        {
            // If rename fails, fall through and reopen the existing file.
        }

        Open();
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
