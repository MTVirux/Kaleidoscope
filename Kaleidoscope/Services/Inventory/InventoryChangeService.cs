using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;
using System.Linq;

namespace Kaleidoscope.Services.Inventory;

/// <summary>
/// Service that detects inventory and currency changes using a hybrid approach:
/// - IGameInventory events for item/crystal changes (immediate notification)
/// - Periodic value comparison on IFramework.Update (catches all changes reliably)
/// </summary>
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

    // Player inventory state tracking - waits for data to stabilize after inventory changes
    private DateTime _playerInventoryChangeTime = DateTime.MinValue;
    private readonly TimeSpan _playerInventoryStabilizationDelay = TimeSpan.FromMilliseconds(ConfigStatic.PlayerInventoryStabilizationDelayMs);
    private bool _isPlayerInventoryStabilizing = false;

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
    /// Event fired when player inventory data has stabilized (data is ready to read).
    /// </summary>
    public event Action? OnPlayerInventoryReady;

    /// <summary>
    /// Event fired when a retainer's inventory has stabilized (data is ready to read).
    /// </summary>
    public event Action? OnRetainerInventoryReady;

    public event Action? OnRetainerClosed;

    public InventoryChangeService(IPluginLog log, IClientState clientState, IGameInventory gameInventory, IFramework framework, TrackedDataRegistry registry, ConfigurationService configService)
    {
        _log = log;
        _clientState = clientState;
        _gameInventory = gameInventory;
        _framework = framework;
        _registry = registry;
        _configService = configService;

        _gameInventory.InventoryChanged += OnDalamudInventoryChanged;
        _framework.Update += OnFrameworkUpdate;

        LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Initialized with IGameInventory events + currency polling");
    }

    private void OnDalamudInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        // Skip processing if not logged in to prevent invalid data
        if (!_clientState.IsLoggedIn)
            return;
        if (_configService.Config.UseUnifiedResources)
            return;

        // Dalamud's inventory change event fired
        // This covers player inventory, armory, crystals, retainer inventories, etc.
        var hasCrystalChange = false;
        var hasPlayerInventoryChange = false;

        try
        {
            var containerList = string.Join(',', events.Select(e => e.Item.ContainerType.ToString()));
            LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] Dalamud InventoryChanged fired: {events.Count} events; containers={containerList}");
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
                
                // Player crystals trigger player inventory stabilization
                if (containerType == GameInventoryType.Crystals)
                {
                    hasPlayerInventoryChange = true;
                }
            }
            // Regular inventory (player or retainer)
            else if (IsTrackedContainerType(containerType))
            {
                _pendingInventoryUpdate = true;
                
                // Check if this is a player inventory container (not retainer)
                if (IsPlayerInventoryContainer(containerType))
                {
                    hasPlayerInventoryChange = true;
                }
            }
        }

        // Start player inventory stabilization if player inventory changed
        if (hasPlayerInventoryChange && !_isRetainerStabilizing)
        {
            _playerInventoryChangeTime = DateTime.UtcNow;
            _isPlayerInventoryStabilizing = true;
            LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] Player inventory changed, waiting {ConfigStatic.PlayerInventoryStabilizationDelayMs}ms for data stabilization");
        }

        if (hasCrystalChange)
        {
            LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Crystal container change detected");
            try
            {
                OnCrystalsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnCrystalsChanged callback error: {ex.Message}");
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

    private bool IsPlayerInventoryContainer(GameInventoryType type)
    {
        return type switch
        {
            // Player crystals
            GameInventoryType.Crystals => true,

            // Main player inventory
            GameInventoryType.Inventory1 or GameInventoryType.Inventory2 or
            GameInventoryType.Inventory3 or GameInventoryType.Inventory4 => true,

            // Key items
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

            LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] Debounced inventory event processed at {now:o}");
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

        // Handle player inventory stabilization
        if (_isPlayerInventoryStabilizing && now - _playerInventoryChangeTime >= _playerInventoryStabilizationDelay)
        {
            _isPlayerInventoryStabilizing = false;
            LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Player inventory data stabilized, resuming value checks");
            ClearValueCache();
            try { OnPlayerInventoryReady?.Invoke(); }
            catch (Exception ex) { LogService.Debug(LogCategory.Inventory, $"[InventoryChangeService] OnPlayerInventoryReady callback error: {ex.Message}"); }
        }

        // Skip value checks while retainer or player inventory data is stabilizing
        if (_isRetainerStabilizing || _isPlayerInventoryStabilizing)
        {
            return;
        }

        // Value-polling is redundant with MemoryPoller when unified resources are active
        if (_configService.Config.UseUnifiedResources)
            return;

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
    /// Manually trigger an inventory/currency check (useful for initialization).
    /// </summary>
    public void TriggerUpdate()
    {
        _pendingInventoryUpdate = true;
        _lastValueCheck = DateTime.MinValue; // Force immediate value check
        LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] TriggerUpdate called; forcing immediate value check");
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

        LogService.Debug(LogCategory.Inventory, "[InventoryChangeService] Disposed");
    }
}
