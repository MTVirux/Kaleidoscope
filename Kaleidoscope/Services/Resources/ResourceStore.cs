using System.Collections.Generic;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// In-memory current-state store for the unified resources subsystem.
/// All mutations are gated by a single lock owned by ResourceObservationService —
/// ResourceStore itself is unsynchronized and assumes single-threaded access.
/// </summary>
public sealed class ResourceStore
{
    private readonly Dictionary<ResourceKey, Resource> _state = new();
    private long _version;

    /// <summary>Monotonic counter incremented on every real change (idempotent updates do not bump it).</summary>
    public long Version => _version;

    /// <summary>Current resource for the given key, or null if not present.</summary>
    public Resource? Get(ResourceKey key)
        => _state.TryGetValue(key, out var r) ? r : null;

    /// <summary>
    /// Apply an observation. Returns true if the resource changed (qty or flags differ from current),
    /// false if idempotent. Exposes the previous quantity via out — used by aggregate maintenance.
    /// </summary>
    public bool Apply(Resource observation, out long previousQuantity)
    {
        if (_state.TryGetValue(observation.Key, out var existing))
        {
            previousQuantity = existing.Quantity;
            if (existing.Quantity == observation.Quantity && existing.Flags == observation.Flags)
            {
                _state[observation.Key] = observation;
                return false;
            }
        }
        else
        {
            previousQuantity = 0;
        }

        _state[observation.Key] = observation;
        _version++;
        return true;
    }

    /// <summary>Remove a resource (e.g. slot cleared). Returns true if a value was removed.</summary>
    public bool Remove(ResourceKey key, out long previousQuantity)
    {
        if (_state.TryGetValue(key, out var existing))
        {
            previousQuantity = existing.Quantity;
            _state.Remove(key);
            _version++;
            return true;
        }
        previousQuantity = 0;
        return false;
    }

    /// <summary>Snapshot copy — caller iterates without holding any lock.</summary>
    public IReadOnlyList<Resource> Snapshot()
    {
        var list = new List<Resource>(_state.Count);
        foreach (var r in _state.Values) list.Add(r);
        return list;
    }
}
