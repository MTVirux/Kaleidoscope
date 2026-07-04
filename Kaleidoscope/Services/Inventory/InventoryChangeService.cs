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
/// </remarks>
public sealed class InventoryChangeService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IFramework _framework;
    private readonly TrackedDataRegistry _registry;
    private readonly ConfigurationService _configService;
    private readonly ResourceObservationService _observations;

    private static readonly HashSet<TrackedDataType> DefaultEnabledTypes = new() { TrackedDataType.Gil };

    // Retainer state tracking - waits for data to stabilize after opening a retainer
    private bool _wasRetainerActive = false;
    private DateTime _retainerOpenedTime = DateTime.MinValue;
    private readonly TimeSpan _retainerStabilizationDelay = TimeSpan.FromMilliseconds(ConfigStatic.RetainerStabilizationDelayMs);
    private bool _isRetainerStabilizing = false;

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

    public InventoryChangeService(IPluginLog log, IClientState clientState, IFramework framework, TrackedDataRegistry registry, ConfigurationService configService, ResourceObservationService observations)
    {
        _log = log;
        _clientState = clientState;
        _framework = framework;
        _registry = registry;
        _configService = configService;
        _observations = observations;

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
        var isRetainerActive = GameStateService.IsRetainerActive();

        if (isRetainerActive != _wasRetainerActive)
        {
            _wasRetainerActive = isRetainerActive;
            if (isRetainerActive)
            {
                // Retainer just opened - start stabilization period
                _retainerOpenedTime = now;
                _isRetainerStabilizing = true;
                LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] Retainer opened, waiting {ConfigStatic.RetainerStabilizationDelayMs}ms for data stabilization");
            }
            else
            {
                // Retainer closed - stop stabilizing
                _isRetainerStabilizing = false;
                LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Retainer closed");
                try { OnRetainerClosed?.Invoke(); }
                catch (Exception ex) { LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnRetainerClosed callback error: {ex.Message}"); }
            }
        }

        if (_isRetainerStabilizing && now - _retainerOpenedTime >= _retainerStabilizationDelay)
        {
            _isRetainerStabilizing = false;
            LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Retainer data stabilized");
            try { OnRetainerInventoryReady?.Invoke(); }
            catch (Exception ex) { LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnRetainerInventoryReady callback error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Projects a committed resource change onto the legacy per-variable series. Recomputes only the
    /// active-character aggregate(s) the changed key can affect, then raises OnValuesChanged for the
    /// enabled ones. Runs on the framework thread (all RecordObservation callers marshal there), so
    /// the game-memory reads in GetCurrentValue stay on the main thread as before.
    /// </summary>
    private void OnObservationCommitted(ResourceKey key)
    {
        if (!_clientState.IsLoggedIn) return;

        try
        {
            var enabledTypes = _configService.Config.EnabledTrackedDataTypes;
            if (enabledTypes == null || enabledTypes.Count == 0)
                enabledTypes = DefaultEnabledTypes;

            Dictionary<TrackedDataType, long>? changed = null;
            foreach (var type in _registry.GetAffectedTypes(key))
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

        LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Disposed");
    }
}
