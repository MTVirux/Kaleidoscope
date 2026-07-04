using System.Collections.Generic;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// In-memory current-state store for the unified resources subsystem.
/// The store owns a single lock guarding every read and write, so writers (via
/// ResourceObservationService) and lock-free-looking readers (InventoryCacheService,
/// ResourceToLegacyAdapter, TrackedDataRegistry, TestsCategory) are all safe to call
/// from any thread without "collection modified during enumeration" hazards.
/// </summary>
public sealed class ResourceStore : IRequiredService
{
    private readonly object _lock = new();

    private readonly Dictionary<ResourceKey, Resource> _state = new();
    private long _version;

    // Aggregates: per (item_id, owner_kind) totals and cross-kind item totals — recomputed
    // incrementally on every change. "All kinds" aggregate is the sum of all per-kind buckets.
    private readonly Dictionary<(uint ItemId, OwnerKind Kind), long> _byItemAndKind = new();
    private readonly Dictionary<uint, long> _byItem = new();

    // Secondary index: per (owner, kind, item) total — makes GetSumForOwner O(1) instead of a
    // full-store scan (hot path: TrackedDataRegistry currency/crystal reads on every change).
    private readonly Dictionary<(ulong OwnerId, OwnerKind Kind, uint ItemId), long> _byOwnerItem = new();

    // Secondary index: (owner, kind, container, slot) → current occupant item id. Makes
    // GetItemIdForSlot O(1) (hot path: empty-slot translation in InventoryEventCapture /
    // TradeReconcileCapture). Tracks the most-recently-applied item for each physical slot.
    private readonly Dictionary<(ulong OwnerId, OwnerKind Kind, Container Container, short Slot), uint> _bySlot = new();

    public ResourceStore() { }

    /// <summary>Monotonic counter incremented on every real change (idempotent updates do not bump it).</summary>
    public long Version
    {
        get { lock (_lock) return _version; }
    }

    /// <summary>Current resource for the given key, or null if not present.</summary>
    public Resource? Get(ResourceKey key)
    {
        lock (_lock)
            return _state.TryGetValue(key, out var r) ? r : null;
    }

    /// <summary>
    /// Apply an observation. Returns true if the resource changed (qty or flags differ from current),
    /// false if idempotent. Exposes the previous quantity via out — used by aggregate maintenance.
    /// Does NOT maintain aggregates or secondary indexes; production paths use ApplyWithAggregate.
    /// </summary>
    public bool Apply(Resource observation, out long previousQuantity)
    {
        lock (_lock)
            return ApplyLocked(observation, out previousQuantity);
    }

    private bool ApplyLocked(Resource observation, out long previousQuantity)
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

    /// <summary>Apply an observation and update aggregates + secondary indexes incrementally. Returns true if changed.</summary>
    public bool ApplyWithAggregate(Resource observation)
        => ApplyWithAggregate(observation, out _);

    /// <summary>
    /// Apply an observation and update aggregates + secondary indexes incrementally. Returns true if
    /// changed and exposes the previous quantity, letting callers compute the change amount atomically
    /// without a separate Get.
    /// </summary>
    public bool ApplyWithAggregate(Resource observation, out long previousQuantity)
    {
        lock (_lock)
        {
            var changed = ApplyLocked(observation, out previousQuantity);

            // Slot occupancy tracks the most-recently-applied item regardless of quantity delta.
            var k = observation.Key;
            _bySlot[(k.OwnerId, k.OwnerKind, k.Container, k.Slot)] = k.ItemId;

            var delta = observation.Quantity - previousQuantity;
            if (delta != 0)
            {
                var kindKey = (k.ItemId, k.OwnerKind);
                _byItemAndKind.TryGetValue(kindKey, out var current);
                _byItemAndKind[kindKey] = current + delta;

                _byItem.TryGetValue(k.ItemId, out var total);
                _byItem[k.ItemId] = total + delta;

                var ownerKey = (k.OwnerId, k.OwnerKind, k.ItemId);
                _byOwnerItem.TryGetValue(ownerKey, out var ownerTotal);
                _byOwnerItem[ownerKey] = ownerTotal + delta;
            }
            return changed;
        }
    }

    /// <summary>
    /// Clear all in-memory state — current resources, aggregates, and secondary indexes.
    /// Used when the underlying DB is wiped (e.g., the Clear DB button) so the in-memory store
    /// matches the now-empty DB.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _state.Clear();
            _byItemAndKind.Clear();
            _byItem.Clear();
            _byOwnerItem.Clear();
            _bySlot.Clear();
            _version++;   // bump so consumers detect the reset
        }
    }

    /// <summary>
    /// Returns the ItemId of whichever real item currently occupies a given slot, or null
    /// if the slot is empty or not tracked. Used to translate empty-slot inventory events
    /// (ItemId=0) back to the item that was cleared so its quantity can be zeroed.
    /// </summary>
    public uint? GetItemIdForSlot(ulong ownerId, OwnerKind ownerKind, Container container, short slot)
    {
        lock (_lock)
        {
            if (_bySlot.TryGetValue((ownerId, ownerKind, container, slot), out var itemId) && itemId != 0)
                return itemId;
            return null;
        }
    }

    /// <summary>Snapshot copy — caller iterates without holding the store lock.</summary>
    public IReadOnlyList<Resource> Snapshot()
    {
        lock (_lock)
        {
            var list = new List<Resource>(_state.Count);
            foreach (var r in _state.Values) list.Add(r);
            return list;
        }
    }

    /// <summary>
    /// Sum the quantity of an item across all entries for a specific owner. Used when the
    /// caller knows the owner (e.g., active character) and wants a scoped total — distinct
    /// from the cross-character GetAggregate. O(1) via the (owner, kind, item) index.
    /// </summary>
    public long GetSumForOwner(ulong ownerId, OwnerKind ownerKind, uint itemId)
    {
        lock (_lock)
            return _byOwnerItem.TryGetValue((ownerId, ownerKind, itemId), out var total) ? total : 0;
    }

    /// <summary>Total quantity of an item across all owners (or filtered to one owner kind).</summary>
    public long GetAggregate(uint itemId, OwnerKind? scope = null)
    {
        lock (_lock)
        {
            if (scope is null)
                return _byItem.TryGetValue(itemId, out var total) ? total : 0;

            return _byItemAndKind.TryGetValue((itemId, scope.Value), out var v) ? v : 0;
        }
    }

    /// <summary>
    /// Per-character sum of an item across player + their retainers, given a set of item IDs.
    /// Player rows are matched by (OwnerKind=Player, OwnerId=charId). Retainer rows are matched
    /// by (OwnerKind=Retainer) — but in-memory Resource doesn't carry parent_owner_id, so this
    /// helper does NOT attribute retainer-owned rows to a parent. Callers that need retainer
    /// inclusion under a specific parent character should either fall back to the DB query OR
    /// the future ParentOwnerId-aware version of this method.
    ///
    /// For the common case of Player-only items (Gil, MGP, etc.) this is exactly right.
    /// </summary>
    public Dictionary<ulong, long> GetPerOwnerSum(uint itemId, OwnerKind ownerKind)
    {
        var result = new Dictionary<ulong, long>();
        lock (_lock)
        {
            foreach (var r in _state.Values)
            {
                if (r.Key.OwnerKind != ownerKind) continue;
                if (r.Key.ItemId != itemId) continue;
                if (!result.TryGetValue(r.Key.OwnerId, out var current))
                    current = 0;
                result[r.Key.OwnerId] = current + r.Quantity;
            }
        }
        return result;
    }
}
