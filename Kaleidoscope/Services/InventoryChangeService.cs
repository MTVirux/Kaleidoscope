using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;
using System.Linq;

namespace Kaleidoscope.Services;

/// <summary>
/// Service that detects inventory and currency changes using a hybrid approach:
/// - IGameInventory events for item/crystal changes (immediate notification)
/// - Periodic value comparison on IFramework.Update (catches all changes reliably)
/// </summary>
/// <remarks>
/// This follows the pattern used by popular Dalamud plugins: direct InventoryManager reads
/// with value caching to detect changes, supplemented by Dalamud's inventory events for
/// immediate notification of item changes.
/// </remarks>
public sealed class InventoryChangeService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IGameInventory _gameInventory;
    private readonly IFramework _framework;
    private readonly TrackedDataRegistry _registry;
    private readonly ConfigurationService _configService;

    // Debounce tracking for inventory events
    private volatile bool _pendingInventoryUpdate;
    private DateTime _lastEventTime = DateTime.MinValue;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(ConfigStatic.InventoryDebounceMs);

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
    /// Event fired when crystals specifically change (for crystal tracking).
    /// </summary>
    public event Action? OnCrystalsChanged;

    /// <summary>
    /// Event fired when a retainer's inventory has stabilized (data is ready to read).
    /// </summary>
    public event Action? OnRetainerInventoryReady;

    /// <summary>
    /// Event fired when the retainer is closed.
    /// </summary>
    public event Action? OnRetainerClosed;

    public InventoryChangeService(IPluginLog log, IClientState clientState, IGameInventory gameInventory, IFramework framework, TrackedDataRegistry registry, ConfigurationService configService)
    {
        _log = log;
        _clientState = clientState;
        _gameInventory = gameInventory;
        _framework = framework;
        _registry = registry;
        _configService = configService;

        // Subscribe to Dalamud's inventory events (covers items/crystals)
        _gameInventory.InventoryChanged += OnDalamudInventoryChanged;

        // Subscribe to framework update for debounced processing and currency checks
        _framework.Update += OnFrameworkUpdate;

        _log.Debug("[InventoryChangeService] Initialized with IGameInventory events + currency polling");
    }

    private void OnDalamudInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        // Skip processing if not logged in to prevent invalid data
        if (!_clientState.IsLoggedIn)
            return;

        // Dalamud's inventory change event fired
        // This covers player inventory, armory, crystals, retainer inventories, etc.
        var hasCrystalChange = false;

        try
        {
            var containerList = string.Join(',', events.Select(e => e.Item.ContainerType.ToString()));
            _log.Debug($"[InventoryChangeService] Dalamud InventoryChanged fired: {events.Count} events; containers={containerList}");
        }
        catch
        {
            // Ignore logging failures to avoid disrupting the event flow
        }

        foreach (var evt in events)
        {
            // Check container type from the item
            var containerType = evt.Item.ContainerType;

            // Crystals container (player or retainer)
            if (containerType == GameInventoryType.Crystals || containerType == GameInventoryType.RetainerCrystals)
            {
                hasCrystalChange = true;
                _pendingInventoryUpdate = true;
            }
            // Regular inventory (player or retainer)
            else if (IsTrackedContainerType(containerType))
            {
                _pendingInventoryUpdate = true;
            }
        }

        if (hasCrystalChange)
        {
            _log.Debug("[InventoryChangeService] Crystal container change detected");
            try
            {
                OnCrystalsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Debug($"[InventoryChangeService] OnCrystalsChanged callback error: {ex.Message}");
            }
        }
    }

    private bool IsTrackedContainerType(GameInventoryType type)
    {
        return type switch
        {
            // Crystals container (player and retainer)
            GameInventoryType.Crystals => true,
            GameInventoryType.RetainerCrystals => true,

            // Main inventory
            GameInventoryType.Inventory1 or GameInventoryType.Inventory2 or
            GameInventoryType.Inventory3 or GameInventoryType.Inventory4 => true,

            // Retainer inventory pages
            GameInventoryType.RetainerPage1 or GameInventoryType.RetainerPage2 or
            GameInventoryType.RetainerPage3 or GameInventoryType.RetainerPage4 or
            GameInventoryType.RetainerPage5 or GameInventoryType.RetainerPage6 or
            GameInventoryType.RetainerPage7 => true,

            // Key items (contains things like Ventures)
            GameInventoryType.KeyItems => true,

            _ => false
        };
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Skip processing if not logged in to prevent invalid data
        if (!_clientState.IsLoggedIn)
            return;

        var now = DateTime.UtcNow;

        // Process pending inventory events (debounced)
        if (_pendingInventoryUpdate && now - _lastEventTime >= _debounceInterval)
        {
            _pendingInventoryUpdate = false;
            _lastEventTime = now;

            _log.Debug($"[InventoryChangeService] Debounced inventory event processed at {now:o}");

            // Note: The debounced inventory update is now handled via CheckForValueChanges
            // which reads all values and fires OnValuesChanged with the complete set.
        }

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
                _log.Debug($"[InventoryChangeService] Retainer opened, waiting {ConfigStatic.RetainerStabilizationDelayMs}ms for data stabilization");
            }
            else
            {
                // Retainer closed - stop stabilizing and clear cache
                _isRetainerStabilizing = false;
                _log.Debug("[InventoryChangeService] Retainer closed, clearing value cache");
                ClearValueCache();
                try { OnRetainerClosed?.Invoke(); }
                catch (Exception ex) { _log.Debug($"[InventoryChangeService] OnRetainerClosed callback error: {ex.Message}"); }
            }
        }

        // Check if retainer data has stabilized
        if (_isRetainerStabilizing && now - _retainerOpenedTime >= _retainerStabilizationDelay)
        {
            _isRetainerStabilizing = false;
            _log.Debug("[InventoryChangeService] Retainer data stabilized, resuming value checks");
            // Clear cached values to force fresh reads with stabilized retainer data
            ClearValueCache();
            try { OnRetainerInventoryReady?.Invoke(); }
            catch (Exception ex) { _log.Debug($"[InventoryChangeService] OnRetainerInventoryReady callback error: {ex.Message}"); }
        }

        // Skip value checks while retainer data is stabilizing
        if (_isRetainerStabilizing)
        {
            return;
        }

        // Check all tracked values periodically via direct InventoryManager reads
        // This is the reliable fallback that catches all changes
        if (now - _lastValueCheck >= _valueCheckInterval)
        {
            _lastValueCheck = now;
            CheckForValueChanges();
        }
    }

    /// <summary>
    /// Checks enabled data types for value changes using direct InventoryManager reads.
    /// Only reads values for types that are actually being tracked to minimize game memory access.
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

            var changedValues = new Dictionary<TrackedDataType, long>();

            // Check only enabled data types
            foreach (var dataType in enabledTypes)
            {
                var currentValue = _registry.GetCurrentValue(dataType);
                if (!currentValue.HasValue) continue;

                if (_lastKnownValues.TryGetValue(dataType, out var lastValue))
                {
                    if (currentValue.Value != lastValue)
                    {
                        _lastKnownValues[dataType] = currentValue.Value;
                        changedValues[dataType] = currentValue.Value;
                    }
                }
                else
                {
                    // First time seeing this value, cache it but also treat as "change" for initial sampling
                    _lastKnownValues[dataType] = currentValue.Value;
                    changedValues[dataType] = currentValue.Value;
                }
            }

            if (changedValues.Count > 0)
            {
                try
                {
                    try
                    {
                        var changesSummary = string.Join(", ", changedValues.Select(kv => $"{kv.Key}={kv.Value}"));
                        _log.Debug($"[InventoryChangeService] Detected value changes: {changesSummary}");
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
                    _log.Debug($"[InventoryChangeService] OnValuesChanged callback error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[InventoryChangeService] CheckForValueChanges error: {ex.Message}");
        }
    }

    /// <summary>
    /// Manually trigger an inventory/currency check (useful for initialization).
    /// </summary>
    public void TriggerUpdate()
    {
        _pendingInventoryUpdate = true;
        _lastValueCheck = DateTime.MinValue; // Force immediate value check
        _log.Debug("[InventoryChangeService] TriggerUpdate called; forcing immediate value check");
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
        _gameInventory.InventoryChanged -= OnDalamudInventoryChanged;

        _log.Debug("[InventoryChangeService] Disposed");
    }
}
