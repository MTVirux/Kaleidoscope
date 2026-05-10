using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// Drives ResourceDbWriter.FlushOnce() from the framework tick at ~1 s cadence.
/// Flush itself runs off-thread via Task.Run so the framework tick stays cheap.
/// On dispose, performs a final synchronous flush with a 3 s timeout.
/// </summary>
public sealed class ResourceFlushTicker : IDisposable, IRequiredService
{
    private readonly IFramework _framework;
    private readonly ResourceDbWriter _writer;
    private readonly IPluginLog _log;
    private DateTime _nextFlush;
    private const int FlushIntervalMs = 1000;
    private int _consecutiveFailures;

    public ResourceFlushTicker(IFramework framework, ResourceDbWriter writer, IPluginLog log)
    {
        _framework = framework;
        _writer = writer;
        _log = log;
        _framework.Update += OnTick;
    }

    private void OnTick(IFramework f)
    {
        var now = DateTime.UtcNow;
        if (now < _nextFlush) return;
        var interval = _consecutiveFailures >= 3 ? 10_000 : FlushIntervalMs;
        _nextFlush = now.AddMilliseconds(interval);

        if (_writer.PendingCount == 0) return;

        Task.Run(() =>
        {
            try
            {
                _writer.FlushOnce();
                _consecutiveFailures = 0;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _log.Warning($"[ResourceFlushTicker] Flush failed (#{_consecutiveFailures}): {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        _framework.Update -= OnTick;
        try
        {
            var final = Task.Run(() => _writer.FlushOnce());
            if (!final.Wait(TimeSpan.FromSeconds(3)))
                _log.Warning("[ResourceFlushTicker] Final flush timed out on dispose");
        }
        catch (Exception ex)
        {
            _log.Warning($"[ResourceFlushTicker] Final flush exception: {ex.Message}");
        }
    }
}
