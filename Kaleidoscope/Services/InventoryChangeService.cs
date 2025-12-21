using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;

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
    private readonly IGameInventory _gameInventory;
    private readonly IFramework _framework;
    private readonly TrackedDataRegistry _registry;
    private readonly ConfigurationService _configService;

    // Debounce tracking for inventory events
    private volatile bool _pendingInventoryUpdate;
    private DateTime _lastEventTime = DateTime.MinValue;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(100);

    // Value tracking - caches last known values to detect changes
    private readonly Dictionary<TrackedDataType, long> _lastKnownValues = new();
    private DateTime _lastValueCheck = DateTime.MinValue;
    private readonly TimeSpan _valueCheckInterval = TimeSpan.FromMilliseconds(1000); // Reduced from 500ms to 1s

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
    /// Event fired when currency values change (Gil, Tomestones, etc.).
    /// </summary>
    public event Action? OnCurrencyChanged;

    public InventoryChangeService(IPluginLog log, IGameInventory gameInventory, IFramework framework, TrackedDataRegistry registry, ConfigurationService configService)
    {
        _log = log;
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
        // Dalamud's inventory change event fired
        // This covers player inventory, armory, crystals, retainer inventories, etc.
        var hasCrystalChange = false;

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
        var now = DateTime.UtcNow;

        // Process pending inventory events (debounced)
        if (_pendingInventoryUpdate && now - _lastEventTime >= _debounceInterval)
        {
            _pendingInventoryUpdate = false;
            _lastEventTime = now;

            // Note: The debounced inventory update is now handled via CheckForValueChanges
            // which reads all values and fires OnValuesChanged with the complete set.
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
            var hasCurrencyChange = false;

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

                        // Track if this is a currency-type change
                        if (_registry.Definitions.TryGetValue(dataType, out var def) &&
                            def.Category is TrackedDataCategory.Currency or
                            TrackedDataCategory.Tomestone or
                            TrackedDataCategory.Scrip or
                            TrackedDataCategory.GrandCompany or
                            TrackedDataCategory.PvP or
                            TrackedDataCategory.Hunt or
                            TrackedDataCategory.GoldSaucer or
                            TrackedDataCategory.Tribal or
                            TrackedDataCategory.FreeCompanyRetainer)
                        {
                            hasCurrencyChange = true;
                        }
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
                    if (hasCurrencyChange)
                    {
                        OnCurrencyChanged?.Invoke();
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
