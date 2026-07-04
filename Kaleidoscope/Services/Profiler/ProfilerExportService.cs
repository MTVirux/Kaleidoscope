using System.Globalization;
using Kaleidoscope.Services.Common;
using OtterGui.Services;

namespace Kaleidoscope.Services.Profiler;

/// <summary>
/// Owns Kaleidoscope's profiler output files: a slow-op events log and a snapshot CSV.
/// Both rotate by size and are independent of the global FileLoggingEnabled toggle, so they
/// keep recording even when dalamud.log has stopped accepting writes. Writers are created
/// lazily on first use.
/// </summary>
public sealed class ProfilerExportService : IService, IDisposable
{
    private readonly FilenameService _filenames;
    private readonly ConfigurationService _configService;
    private readonly object _lock = new();
    private RotatingFileWriter? _eventsWriter;
    private RotatingFileWriter? _snapshotWriter;

    public ProfilerExportService(FilenameService filenames, ConfigurationService configService)
    {
        _filenames = filenames;
        _configService = configService;
    }

    public string SlowOpFilePath => _filenames.ProfilerEventsLogFilePath;
    public string SnapshotFilePath => _filenames.ProfilerSnapshotCsvFilePath;

    private int MaxSizeMB => _configService.Config.FileLoggingMaxSizeMB;

    public void WriteSlowOp(string message)
    {
        lock (_lock)
        {
            _eventsWriter ??= new RotatingFileWriter(_filenames.ProfilerEventsLogFilePath, MaxSizeMB);
            var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            _eventsWriter.WriteLine($"{ts} {message}");
        }
    }

    public void Write(ProfilerSnapshot snapshot)
    {
        lock (_lock)
        {
            _snapshotWriter ??= new RotatingFileWriter(
                _filenames.ProfilerSnapshotCsvFilePath, MaxSizeMB, ProfilerCsvFormatter.Header);
            foreach (var line in ProfilerCsvFormatter.Format(snapshot))
                _snapshotWriter.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _eventsWriter?.Close();
            _snapshotWriter?.Close();
            _eventsWriter = null;
            _snapshotWriter = null;
        }
    }
}
