using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;

namespace Kaleidoscope.Services.Inventory;

/// <summary>
/// Service that drives the retainer state machine and fires stabilization events.
/// Also performs periodic value-change polling and fires OnValuesChanged for subscribers.
/// </summary>
public sealed class InventoryChangeService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IFramework _framework;
    private readonly TrackedDataRegistry _registry;
    private readonly ConfigurationService _configService;

    // Value tracking - caches last known values to detect changes
    private readonly Dictionary<TrackedDataType, long> _lastKnownValues = new();
    private DateTime _lastValueCheck = DateTime.MinValue;
    private readonly TimeSpan _valueCheckInterval = TimeSpan.FromMilliseconds(ConfigStatic.ValueCheckIntervalMs);

    // Retainer state tracking - waits for data to stabilize after opening a retainer
    private bool _wasRetainerActive = false;
    private DateTime _retainerOpenedTime = DateTime.MinValue;
    private readonly TimeSpan _retainerStabilizationDelay = TimeSpan.FromMilliseconds(ConfigStatic.RetainerStabilizationDelayMs);
    private bool _isRetainerStabilizing = false;

    /// <summary>
    /// Event fired when any tracked inventory/currency value may have changed.
    /// Passes the already-captured values to avoid re-reading game memory.
    /// </summary>
    public event Action<IReadOnlyDictionary<TrackedDataType, long>>? OnValuesChanged;

    /// <summary>
    /// Event fired when a retainer's inventory has stabilized (data is ready to read).
    /// </summary>
    public event Action? OnRetainerInventoryReady;

    public event Action? OnRetainerClosed;

    public InventoryChangeService(IPluginLog log, IClientState clientState, IFramework framework, TrackedDataRegistry registry, ConfigurationService configService)
    {
        _log = log;
        _clientState = clientState;
        _framework = framework;
        _registry = registry;
        _configService = configService;

        _framework.Update += OnFrameworkUpdate;

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
                // Retainer closed - stop stabilizing and clear cache
                _isRetainerStabilizing = false;
                LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Retainer closed, clearing value cache");
                ClearValueCache();
                try { OnRetainerClosed?.Invoke(); }
                catch (Exception ex) { LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnRetainerClosed callback error: {ex.Message}"); }
            }
        }

        if (_isRetainerStabilizing && now - _retainerOpenedTime >= _retainerStabilizationDelay)
        {
            _isRetainerStabilizing = false;
            LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Retainer data stabilized, resuming value checks");
            ClearValueCache();
            try { OnRetainerInventoryReady?.Invoke(); }
            catch (Exception ex) { LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnRetainerInventoryReady callback error: {ex.Message}"); }
        }

        // Skip value checks while retainer data is stabilizing
        if (_isRetainerStabilizing)
        {
            return;
        }

        if (now - _lastValueCheck >= _valueCheckInterval)
        {
            _lastValueCheck = now;
            CheckForValueChanges();
        }
    }

    /// <summary>
    /// Checks enabled data types for value changes using direct InventoryManager reads.
    /// Uses GetCurrentValuesSnapshot() to batch expensive retainer lookups into a single pass.
    /// </summary>
    private void CheckForValueChanges()
    {
        try
        {
            // Only check enabled types to avoid unnecessary game memory reads
            var enabledTypes = _configService.Config.EnabledTrackedDataTypes;
            if (enabledTypes == null || enabledTypes.Count == 0)
            {
                enabledTypes = new HashSet<TrackedDataType> { TrackedDataType.Gil };
            }

            // Use snapshot method to fetch all values in one pass, caching expensive retainer lookups
            var currentValues = _registry.GetCurrentValuesSnapshot(enabledTypes);
            var changedValues = new Dictionary<TrackedDataType, long>();

            foreach (var kvp in currentValues)
            {
                var dataType = kvp.Key;
                var currentValue = kvp.Value;

                if (_lastKnownValues.TryGetValue(dataType, out var lastValue))
                {
                    if (currentValue != lastValue)
                    {
                        _lastKnownValues[dataType] = currentValue;
                        changedValues[dataType] = currentValue;
                    }
                }
                else
                {
                    // First time seeing this value, cache it but also treat as "change" for initial sampling
                    _lastKnownValues[dataType] = currentValue;
                    changedValues[dataType] = currentValue;
                }
            }

            if (changedValues.Count > 0)
            {
                try
                {
                    try
                    {
                        var characterName = GameStateService.LocalPlayerName ?? "Unknown";
                        var changesSummary = string.Join(", ", changedValues.Select(kv => $"{kv.Key}={kv.Value}"));
                        LogService.Debug(LogCategory.Inventory, characterName, $"[InventoryChangeService] Detected value changes: {changesSummary}");
                    }
                    catch
                    {
                        // ignore logging failure
                    }

                    // Pass the already-captured values to avoid re-reading game memory
                    OnValuesChanged?.Invoke(changedValues);
                }
                catch (Exception ex)
                {
                    LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnValuesChanged callback error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] CheckForValueChanges error: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears cached values to force fresh detection on next check.
    /// </summary>
    public void ClearValueCache()
    {
        _lastKnownValues.Clear();
        _lastValueCheck = DateTime.MinValue;
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;

        LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Disposed");
    }
}
