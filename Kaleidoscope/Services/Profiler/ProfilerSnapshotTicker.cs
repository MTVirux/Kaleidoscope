using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services.Profiler;

/// <summary>
/// Appends a profiler snapshot to the CSV at a configurable interval while profiling and
/// snapshot export are both enabled. Capture runs on the framework thread (consistent with
/// sample recording); the file write is offloaded, mirroring ResourceFlushTicker.
/// </summary>
public sealed class ProfilerSnapshotTicker : IRequiredService, IDisposable
{
    private readonly IFramework _framework;
    private readonly ProfilerService _profiler;
    private readonly ProfilerExportService _export;
    private readonly ConfigurationService _configService;
    private DateTime _nextSnapshot;
    private int _consecutiveFailures;

    public ProfilerSnapshotTicker(
        IFramework framework, ProfilerService profiler, ProfilerExportService export, ConfigurationService configService)
    {
        _framework = framework;
        _profiler = profiler;
        _export = export;
        _configService = configService;
        _framework.Update += OnTick;
    }

    private void OnTick(IFramework f)
    {
        var config = _configService.Config;
        if (!config.ProfilerEnabled || !config.ProfilerWriteSnapshots) return;

        var now = DateTime.UtcNow;
        if (now < _nextSnapshot) return;

        var intervalSec = Math.Max(1.0, config.ProfilerSnapshotIntervalSeconds);
        var nextDelay = _consecutiveFailures >= 3 ? 10.0 : intervalSec;
        _nextSnapshot = now.AddSeconds(nextDelay);

        ProfilerSnapshot snapshot;
        try
        {
            snapshot = _profiler.CaptureSnapshot();
        }
        catch
        {
            _consecutiveFailures++;
            return;
        }

        Task.Run(() =>
        {
            try
            {
                _export.Write(snapshot);
                _consecutiveFailures = 0;
            }
            catch
            {
                _consecutiveFailures++;
            }
        });
    }

    public void Dispose() => _framework.Update -= OnTick;
}
