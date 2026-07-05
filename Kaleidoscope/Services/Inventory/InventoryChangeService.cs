using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Inventory;

/// <summary>
/// Drives the retainer state machine (fires stabilization events) and projects the unified
/// resource pipeline's change signal onto the legacy per-variable time-series.
/// </summary>
/// <remarks>
/// Change detection now happens exactly once, in ResourceObservationService.RecordObservation.
/// When it reports a real change, this service maps the changed key to the affected tracked-data
/// types, recomputes each one's active-character aggregate, and raises OnValuesChanged — the same
/// contract the old poll used, so CurrencyTrackerService and TopInventoryValueTool are unchanged.
/// Deduplication is handled downstream (TimeSeriesCache last-point + SaveSampleIfChanged), so no
/// second change-detector is kept here.
///
/// The projection is coalesced per framework tick: committed keys only accumulate their affected
/// types into a pending set, and aggregates are recomputed ONCE per framework update. Without
/// this, a batch of observations feeding one aggregate in the same tick (MemoryPoller's
/// per-retainer gil loop, a reconcile scan touching several crystal slots) would emit one
/// transient partial sum per row. Batches run synchronously on the framework thread, so by the
/// time the flush runs they are always fully applied and only settled totals are sampled —
/// matching the old poll, which read settled aggregates.
/// </remarks>
public sealed class InventoryChangeService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IFramework _framework;
    private readonly TrackedDataRegistry _registry;
    private readonly ConfigurationService _configService;
    private readonly ResourceObservationService _observations;
    private readonly GameStateService _gameState;

    private static readonly HashSet<TrackedDataType> DefaultEnabledTypes = new() { TrackedDataType.Gil };

    // Per-tick projection coalescing: ObservationCommitted handlers only add affected types here;
    // FlushProjection drains it once per framework update. Locked because the FC-points import
    // path reaches RecordObservation via RunOnFrameworkThread — framework-thread today, but the
    // lock keeps the structure safe if any future caller commits off-thread.
    private readonly object _pendingLock = new();
    private readonly HashSet<TrackedDataType> _pendingTypes = new();

    // Retainer readiness tracking. On open we begin waiting; each tick OnRetainerInventoryReady fires
    // as soon as GameStateService.AreRetainerContainersLoaded reports every retainer container loaded,
    // or when RetainerStabilizationDelay elapses as a max-wait fallback (ReconcileScanner safely skips
    // any still-unloaded container). Fired exactly once per retainer-open; cleared on close.
    private bool _wasRetainerActive = false;
    private DateTime _retainerOpenedTime = DateTime.MinValue;
    private readonly TimeSpan _retainerStabilizationDelay = TimeSpan.FromMilliseconds(ConfigStatic.RetainerStabilizationDelayMs);
    private bool _awaitingRetainerReady = false;

    /// <summary>
    /// Event fired when any tracked inventory/currency value may have changed.
    /// Carries the recomputed per-type values so subscribers avoid re-reading game memory.
    /// </summary>
    public event Action<IReadOnlyDictionary<TrackedDataType, long>>? OnValuesChanged;

    /// <summary>
    /// Event fired when a retainer's inventory has stabilized (data is ready to read).
    /// </summary>
    public event Action? OnRetainerInventoryReady;

    public event Action? OnRetainerClosed;

    public InventoryChangeService(IPluginLog log, IClientState clientState, IFramework framework, TrackedDataRegistry registry, ConfigurationService configService, ResourceObservationService observations, GameStateService gameState)
    {
        _log = log;
        _clientState = clientState;
        _framework = framework;
        _registry = registry;
        _configService = configService;
        _observations = observations;
        _gameState = gameState;

        _framework.Update += OnFrameworkUpdate;
        _observations.ObservationCommitted += OnObservationCommitted;

        LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Initialized");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Skip processing if not logged in to prevent invalid data
        if (!_clientState.IsLoggedIn)
            return;

        var now = DateTime.UtcNow;

        // Track retainer state changes for stabilization
        // Use IsRetainerActive() which properly checks if a retainer inventory is open
        var isRetainerActive = _gameState.IsRetainerActive();

        if (isRetainerActive != _wasRetainerActive)
        {
            _wasRetainerActive = isRetainerActive;
            if (isRetainerActive)
            {
                // Retainer just opened - begin waiting for its containers to load
                _retainerOpenedTime = now;
                _awaitingRetainerReady = true;
                LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] Retainer opened, waiting for containers to load (max {ConfigStatic.RetainerStabilizationDelayMs}ms)");
            }
            else
            {
                // Retainer closed - stop waiting
                _awaitingRetainerReady = false;
                LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Retainer closed");
                try { OnRetainerClosed?.Invoke(); }
                catch (Exception ex) { LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnRetainerClosed callback error: {ex.Message}"); }
            }
        }

        // Fire readiness as soon as all retainer containers report loaded; otherwise fall back to the
        // fixed delay as a max-wait so a slow/partial load still gets one reconcile pass (the scanner
        // skips any container still unloaded). Fires exactly once per open — the flag guards re-entry.
        if (_awaitingRetainerReady)
        {
            var loaded = _gameState.AreRetainerContainersLoaded();
            var timedOut = now - _retainerOpenedTime >= _retainerStabilizationDelay;
            if (loaded || timedOut)
            {
                _awaitingRetainerReady = false;
                LogService.Debug(LogCategory.Inventory, loaded
                    ? "[InventoryChangeService] Retainer containers loaded"
                    : "[InventoryChangeService] Retainer readiness timed out; scanning loaded containers only");
                try { OnRetainerInventoryReady?.Invoke(); }
                catch (Exception ex) { LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnRetainerInventoryReady callback error: {ex.Message}"); }
            }
        }

        // Flush last, after the retainer events above: the reconcile scans they trigger commit
        // synchronously inside this handler, so their whole batch is projected this same tick.
        FlushProjection();
    }

    /// <summary>
    /// Accumulates the tracked-data types a committed resource change can affect. Recompute is
    /// deferred to <see cref="FlushProjection"/> so multi-row batches coalesce to settled totals.
    /// Cheap pure mapping — safe to run mid-batch under any commit cadence.
    /// </summary>
    private void OnObservationCommitted(ResourceKey key)
    {
        try
        {
            lock (_pendingLock)
            {
                foreach (var type in _registry.GetAffectedTypes(key))
                    _pendingTypes.Add(type);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] Legacy projection error: {ex.Message}");
        }
    }

    /// <summary>
    /// Projects the accumulated changes onto the legacy per-variable series: recomputes each
    /// affected enabled type's active-character aggregate once and raises OnValuesChanged with the
    /// settled values. Runs on the framework thread (so GetCurrentValue's game-memory reads stay on
    /// the main thread, as the old poll did). While no character is active (logged out or the brief
    /// login window where PlayerContentId is still 0), pending types are retained — not dropped —
    /// so changes from offline imports are sampled on the first update after the id is available.
    /// </summary>
    private void FlushProjection()
    {
        if (_gameState.PlayerContentId == 0) return;

        TrackedDataType[] pending;
        lock (_pendingLock)
        {
            if (_pendingTypes.Count == 0) return;
            pending = new TrackedDataType[_pendingTypes.Count];
            _pendingTypes.CopyTo(pending);
            _pendingTypes.Clear();
        }

        try
        {
            var enabledTypes = _configService.Config.EnabledTrackedDataTypes;
            if (enabledTypes == null || enabledTypes.Count == 0)
                enabledTypes = DefaultEnabledTypes;

            Dictionary<TrackedDataType, long>? changed = null;
            foreach (var type in pending)
            {
                if (!enabledTypes.Contains(type)) continue;
                var value = _registry.GetCurrentValue(type);
                if (!value.HasValue) continue;
                (changed ??= new Dictionary<TrackedDataType, long>())[type] = value.Value;
            }

            if (changed is { Count: > 0 })
                OnValuesChanged?.Invoke(changed);
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] Legacy projection error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _observations.ObservationCommitted -= OnObservationCommitted;

        // Drain changes committed since the last tick. This only persists anything while a
        // downstream OnValuesChanged subscriber is still attached: under the container's
        // reverse-creation-order disposal, CurrencyTrackerService (created after this service) is
        // disposed first and has already unsubscribed, so at shutdown this flush is typically a no-op.
        FlushProjection();

        LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Disposed");
    }
}
