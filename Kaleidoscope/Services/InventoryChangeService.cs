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

    // Debounce tracking for inventory events
    private volatile bool _pendingInventoryUpdate;
    private DateTime _lastEventTime = DateTime.MinValue;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(100);

    // Value tracking - caches last known values to detect changes
    private readonly Dictionary<TrackedDataType, long> _lastKnownValues = new();
    private DateTime _lastValueCheck = DateTime.MinValue;
    private readonly TimeSpan _valueCheckInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Event fired when any tracked inventory/currency value may have changed.
    /// The event is debounced to avoid excessive updates.
    /// </summary>
    public event Action? OnInventoryChanged;

    /// <summary>
    /// Event fired when crystals specifically change (for crystal tracking).
    /// </summary>
    public event Action? OnCrystalsChanged;

    /// <summary>
    /// Event fired when currency values change (Gil, Tomestones, etc.).
    /// </summary>
    public event Action? OnCurrencyChanged;

    public InventoryChangeService(IPluginLog log, IGameInventory gameInventory, IFramework framework, TrackedDataRegistry registry)
    {
        _log = log;
        _gameInventory = gameInventory;
        _framework = framework;
        _registry = registry;

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

            try
            {
                OnInventoryChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Debug($"[InventoryChangeService] OnInventoryChanged callback error: {ex.Message}");
            }
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
    /// Checks all tracked data types for value changes using direct InventoryManager reads.
    /// This follows the pattern used by popular plugins: cache last known values and compare.
    /// </summary>
    private void CheckForValueChanges()
    {
        try
        {
            var anyChange = false;
            var hasCurrencyChange = false;

            // Check all tracked data types
            foreach (var def in _registry.Definitions.Values)
            {
                var currentValue = _registry.GetCurrentValue(def.Type);
                if (!currentValue.HasValue) continue;

                if (_lastKnownValues.TryGetValue(def.Type, out var lastValue))
                {
                    if (currentValue.Value != lastValue)
                    {
                        _lastKnownValues[def.Type] = currentValue.Value;
                        anyChange = true;

                        // Track if this is a currency-type change
                        if (def.Category is TrackedDataCategory.Currency or
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
                    // First time seeing this value, cache it
                    _lastKnownValues[def.Type] = currentValue.Value;
                    // Don't treat initial population as a "change" to avoid spam on startup
                }
            }

            if (anyChange)
            {
                try
                {
                    if (hasCurrencyChange)
                    {
                        OnCurrencyChanged?.Invoke();
                    }
                    OnInventoryChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    _log.Debug($"[InventoryChangeService] OnInventoryChanged callback error: {ex.Message}");
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
