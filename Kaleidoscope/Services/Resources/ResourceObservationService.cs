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

    public Resource ToResource() => new()
    {
        Key = Key, Quantity = Quantity, Flags = Flags,
        Spiritbond = Spiritbond, Collectability = Collectability,
        Condition = Condition, GlamourId = GlamourId, UpdatedAt = UpdatedAt,
    };
}

/// <summary>
/// Single entry point for the unified resources subsystem. Every capture source funnels
/// through RecordObservation, which atomically updates the in-memory store + aggregates
/// + time-series cache + DB write queue under one lock.
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

    /// <summary>
    /// Record one observation. Idempotent if (quantity, flags) are unchanged; in that case
    /// only the in-memory UpdatedAt refreshes and no DB row is queued.
    /// </summary>
    public void RecordObservation(ResourceObservation obs)
    {
        lock (_observationLock)
        {
            var resource = obs.ToResource();
            var before = _store.Get(obs.Key);
            var changed = _store.ApplyWithAggregate(resource);

            if (!changed) return;

            var changeAmount = obs.Quantity - (before?.Quantity ?? 0);
            var tag = _sink.ConsumeIfFresh();

            _store.AppendHistory(obs.Key, obs.UpdatedAt, obs.Quantity, changeAmount, tag?.Kind ?? SourceKind.Unknown);

            _writer.Enqueue(new ResourceWrite
            {
                Resource = resource,
                ChangeAmount = changeAmount,
                SourceKind = tag?.Kind ?? SourceKind.Unknown,
                SourceDetail = tag?.Detail,
            });
        }
    }
}
