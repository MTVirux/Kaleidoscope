using System.Collections.Generic;
using System.Linq;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// Tracks which (owner, container) pairs are currently visible to the game client.
/// The poller and reconcile-scan never observe an owner+container that isn't in this set.
/// Events from IGameInventory implicitly respect this — the game only fires for loaded containers.
/// </summary>
public sealed class LoadedContainerSet
{
    private readonly object _lock = new();
    private readonly HashSet<(ulong OwnerId, Container Container)> _set = new();

    public void Add(ulong ownerId, Container container)
    {
        lock (_lock) _set.Add((ownerId, container));
    }

    public void Remove(ulong ownerId, Container container)
    {
        lock (_lock) _set.Remove((ownerId, container));
    }

    public bool Contains(ulong ownerId, Container container)
    {
        lock (_lock) return _set.Contains((ownerId, container));
    }

    public void Clear()
    {
        lock (_lock) _set.Clear();
    }

    public IReadOnlyCollection<(ulong OwnerId, Container Container)> Snapshot()
    {
        lock (_lock) return _set.ToArray();
    }
}
