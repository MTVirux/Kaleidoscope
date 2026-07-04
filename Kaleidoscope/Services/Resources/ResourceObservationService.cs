using System;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// Input to RecordObservation. Differs from Resource only in that it carries the *new* values,
/// not necessarily the stored values — same shape minus invariant fields.
/// </summary>
public readonly record struct ResourceObservation
{
    public ResourceKey Key { get; init; }
    public long Quantity { get; init; }
    public ResourceFlags Flags { get; init; }
    public ushort Spiritbond { get; init; }
    public ushort Collectability { get; init; }
    public ushort Condition { get; init; }
    public uint GlamourId { get; init; }
    public DateTime UpdatedAt { get; init; }
    public ulong ParentOwnerId { get; init; }   // 0 for player owners; owning character's ContentId for retainer/FC owners

    public Resource ToResource() => new()
    {
        Key = Key, Quantity = Quantity, Flags = Flags,
        Spiritbond = Spiritbond, Collectability = Collectability,
        Condition = Condition, GlamourId = GlamourId, UpdatedAt = UpdatedAt,
        // ParentOwnerId intentionally NOT propagated to Resource — in-memory model stays minimal.
    };
}

/// <summary>
/// Single entry point for the unified resources subsystem. Every capture source funnels
/// through RecordObservation, which atomically updates the in-memory store + aggregates
/// + DB write queue under one lock, then signals ObservationCommitted so the legacy
/// per-variable time-series is projected from the same change.
/// </summary>
public sealed class ResourceObservationService : IRequiredService
{
    private readonly ResourceStore _store;
    private readonly ResourceDbWriter _writer;
    private readonly SourceTagSink _sink;
    private readonly object _observationLock = new();

    public ResourceObservationService(ResourceStore store, ResourceDbWriter writer, SourceTagSink sink)
    {
        _store = store;
        _writer = writer;
        _sink = sink;
    }

    public ResourceStore Store => _store;
    public SourceTagSink Sink => _sink;
    public long Version => _store.Version;
    public long DbVersion => _writer.DbFlushedVersion;

    /// <summary>
    /// Raised after an observation produces a real change (quantity or flags), once the store,
    /// aggregates and DB write-queue are all updated. Fired OUTSIDE the observation lock so
    /// subscribers may read back the store (which takes its own lock) without deadlocking.
    /// Drives the legacy time-series projection in InventoryChangeService.
    /// </summary>
    public event Action<ResourceKey>? ObservationCommitted;

    /// <summary>
    /// Record one observation. Idempotent if (quantity, flags) are unchanged; in that case
    /// only the in-memory UpdatedAt refreshes and no DB row is queued.
    /// </summary>
    public void RecordObservation(ResourceObservation obs)
    {
        lock (_observationLock)
        {
            var resource = obs.ToResource();
            if (!_store.ApplyWithAggregate(resource, out var previousQuantity))
                return;   // idempotent — no change, no DB row, no projection

            var changeAmount = obs.Quantity - previousQuantity;
            var tag = _sink.ConsumeIfFresh();

            _writer.Enqueue(new ResourceWrite
            {
                Resource = resource,
                ChangeAmount = changeAmount,
                SourceKind = tag?.Kind ?? SourceKind.Unknown,
                SourceDetail = tag?.Detail,
                ParentOwnerId = obs.ParentOwnerId,
            });
        }

        // Notify the legacy projection outside the lock so its store read-backs don't reenter it.
        ObservationCommitted?.Invoke(obs.Key);
    }
}
