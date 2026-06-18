using System.IO;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using Newtonsoft.Json.Linq;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>One free company's points as read from AutoRetainer's config.</summary>
public readonly record struct FcPointsEntry(ulong OwnerId, string FcName, long Points, DateTime UpdatedAt);

/// <summary>
/// Syncs Free Company points (FC Credits) from AutoRetainer's config file. AutoRetainer stores
/// per-FC points in DefaultConfig.json under "FCData" (keyed by FCID) but does not expose them
/// over IPC, unlike character/retainer gil. We read the file on load and once a minute and record
/// the values as FC-credit observations so FCs the player isn't currently in still report points
/// alongside live-captured data from <see cref="Resources.Capture.MemoryPoller"/>. The same read/apply
/// path also backs the manual "FC points" import on the Integrations config page.
/// </summary>
public sealed class AutoRetainerFcPointsSyncService : IDisposable, IRequiredService
{
    private readonly IFramework _framework;
    private readonly ResourceObservationService _obs;
    private readonly string _arConfigPath;
    private readonly Timer _timer;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(1);

    public AutoRetainerFcPointsSyncService(IDalamudPluginInterface pi, IFramework framework, ResourceObservationService obs)
    {
        _framework = framework;
        _obs = obs;

        // AutoRetainer's config lives next to ours under the shared pluginConfigs root.
        var pluginConfigsRoot = pi.ConfigFile.Directory?.FullName;
        _arConfigPath = pluginConfigsRoot != null
            ? Path.Combine(pluginConfigsRoot, "AutoRetainer", "DefaultConfig.json")
            : string.Empty;

        // On load, then every minute. The file read/parse runs on the timer's threadpool thread;
        // the store mutation is marshalled onto the framework thread in Apply.
        _timer = new Timer(_ => SyncSafe(), null, TimeSpan.Zero, SyncInterval);
    }

    private void SyncSafe()
    {
        try
        {
            var entries = ReadFcPoints();
            if (entries.Count == 0) return;
            _framework.RunOnFrameworkThread(() => Apply(entries));
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.AutoRetainer, $"[AutoRetainerFcPointsSync] Sync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads and parses AutoRetainer's FCData block, returning one entry per eligible FC.
    /// Pure file read + parse — safe to call off the framework thread. Returns an empty list
    /// if the file is missing or holds no usable FC data.
    /// </summary>
    public List<FcPointsEntry> ReadFcPoints()
    {
        var result = new List<FcPointsEntry>();
        if (string.IsNullOrEmpty(_arConfigPath) || !File.Exists(_arConfigPath)) return result;

        // FileShare.ReadWrite so we don't fail when AutoRetainer has the file open for writing.
        string json;
        using (var stream = new FileStream(_arConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            json = reader.ReadToEnd();
        }

        if (JObject.Parse(json)["FCData"] is not JObject fcData) return result;

        foreach (var (fcidStr, token) in fcData)
        {
            if (token is not JObject fc) continue;
            if (!ulong.TryParse(fcidStr, out var fcid) || fcid == 0) continue;

            var name = fc["Name"]?.Value<string>();
            if (string.IsNullOrEmpty(name)) continue;

            var holder = fc["HolderChara"]?.Value<ulong>() ?? 0UL;
            if (holder == 0) continue;

            var points = fc["FCPoints"]?.Value<long>() ?? 0L;
            if (points <= 0) continue;

            var lastUpdateMs = fc["FCPointsLastUpdate"]?.Value<long>() ?? 0L;
            var updatedAt = lastUpdateMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(lastUpdateMs).UtcDateTime
                : DateTime.UtcNow;

            result.Add(new FcPointsEntry(holder, name, points, updatedAt));
        }

        return result;
    }

    /// <summary>
    /// Records parsed FC points as observations. MUST run on the framework thread (mutates the
    /// single-threaded resource store). Returns the number of FCs whose value was recorded;
    /// entries whose stored value is already as fresh or fresher are skipped.
    /// </summary>
    public int Apply(IReadOnlyList<FcPointsEntry> entries)
    {
        var recorded = 0;
        foreach (var entry in entries)
        {
            var key = new ResourceKey
            {
                OwnerId   = entry.OwnerId,
                OwnerKind = OwnerKind.FreeCompany,
                Container = Container.SpecialFreeCompany,
                ItemId    = ResourceCatalog.FCCreditsItemId,
                Slot      = -1,
            };

            // Never let stale config data overwrite a fresher value (live capture, or an
            // already-applied newer read). Also makes the 1-minute re-read a no-op when nothing changed.
            var existing = _obs.Store.Get(key);
            if (existing != null && existing.Value.UpdatedAt >= entry.UpdatedAt) continue;

            _obs.RecordObservation(new ResourceObservation
            {
                Key           = key,
                Quantity      = entry.Points,
                UpdatedAt     = entry.UpdatedAt,
                ParentOwnerId = 0,
            });
            recorded++;
        }

        return recorded;
    }

    /// <summary>
    /// Reads and applies FC points immediately on the calling thread. Intended for the manual
    /// Integrations import, which already runs on the framework thread. Returns the number recorded.
    /// </summary>
    public int ImportNow() => Apply(ReadFcPoints());

    public void Dispose() => _timer.Dispose();
}
