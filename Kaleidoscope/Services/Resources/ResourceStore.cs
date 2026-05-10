using System.Collections.Generic;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// In-memory current-state store for the unified resources subsystem.
/// All mutations are gated by a single lock owned by ResourceObservationService —
/// ResourceStore itself is unsynchronized and assumes single-threaded access.
/// </summary>
public sealed class ResourceStore : IRequiredService
{
    private readonly Dictionary<ResourceKey, Resource> _state = new();
    private long _version;

    private readonly Dictionary<(ulong OwnerId, uint ItemId), Queue<TimeSeriesPoint>> _history = new();
    private int _historyCapacityPerSeries = 256;

    public ResourceStore() { }

    /// <summary>Test-only override of the time-series cache capacity. Must be called before any AppendHistory.</summary>
    internal void SetHistoryCapacityForTests(int capacity)
    {
        _historyCapacityPerSeries = capacity;
    }

    /// <summary>Append an observation to the in-memory time-series cache. Evicts oldest if capacity reached.</summary>
    public void AppendHistory(ResourceKey key, DateTime timestamp, long quantity, long changeAmount, SourceKind source)
    {
        var seriesKey = (key.OwnerId, key.ItemId);
        if (!_history.TryGetValue(seriesKey, out var queue))
        {
            queue = new Queue<TimeSeriesPoint>(_historyCapacityPerSeries);
            _history[seriesKey] = queue;
        }
        while (queue.Count >= _historyCapacityPerSeries)
            queue.Dequeue();
        queue.Enqueue(new TimeSeriesPoint
        {
            Timestamp    = timestamp,
            Quantity     = quantity,
            ChangeAmount = changeAmount,
            Source       = source,
        });
    }

    /// <summary>Recent in-memory history for an item-owner pair. Returns oldest-first.</summary>
    public IReadOnlyList<TimeSeriesPoint> GetRecentHistory(ulong ownerId, uint itemId)
    {
        if (!_history.TryGetValue((ownerId, itemId), out var queue))
            return Array.Empty<TimeSeriesPoint>();
        return queue.ToArray();
    }

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

    // Aggregates: per (item_id, owner_kind) totals — recomputed incrementally on every change.
    // "All kinds" aggregate is the sum of all per-kind buckets.
    private readonly Dictionary<(uint ItemId, OwnerKind Kind), long> _byItemAndKind = new();
    private readonly Dictionary<uint, long> _byItem = new();

    /// <summary>Apply an observation and update aggregates incrementally. Returns true if changed.</summary>
    public bool ApplyWithAggregate(Resource observation)
    {
        var changed = Apply(observation, out var previous);
        var delta = observation.Quantity - previous;
        if (delta != 0)
        {
            var key = (observation.Key.ItemId, observation.Key.OwnerKind);
            _byItemAndKind.TryGetValue(key, out var current);
            _byItemAndKind[key] = current + delta;

            _byItem.TryGetValue(observation.Key.ItemId, out var total);
            _byItem[observation.Key.ItemId] = total + delta;
        }
        return changed;
    }

    /// <summary>Total quantity of an item across all owners (or filtered to one owner kind).</summary>
    public long GetAggregate(uint itemId, OwnerKind? scope = null)
    {
        if (scope is null)
            return _byItem.TryGetValue(itemId, out var total) ? total : 0;

        return _byItemAndKind.TryGetValue((itemId, scope.Value), out var v) ? v : 0;
    }
}
