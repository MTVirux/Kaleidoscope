using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources;

/// <summary>One enqueued write. Carries the resource being persisted plus history metadata.</summary>
public readonly record struct ResourceWrite
{
    public Resource Resource { get; init; }
    public long ChangeAmount { get; init; }   // 0 means no history row appended
    public SourceKind SourceKind { get; init; }
    public string? SourceDetail { get; init; }
}

/// <summary>
/// Drains a pending-writes queue into batched SQLite transactions. Caller is responsible
/// for invoking FlushOnce() periodically (e.g., on framework tick) — this class owns no timer.
/// </summary>
public sealed class ResourceDbWriter : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _queueLock = new();
    private readonly List<ResourceWrite> _pending = new();

    private SqliteCommand? _upsert;
    private SqliteCommand? _historyInsert;

    public const int MaxBatchSize = 5000;
    public const int BackpressureCap = 50_000;

    public ResourceDbWriter(SqliteConnection openConnection)
    {
        _conn = openConnection;
        PreparedStatements();
    }

    public int PendingCount
    {
        get { lock (_queueLock) return _pending.Count; }
    }

    public void Enqueue(ResourceWrite write)
    {
        lock (_queueLock)
        {
            if (_pending.Count >= BackpressureCap) return;
            _pending.Add(write);
        }
    }

    /// <summary>Drain pending into a single transaction. Safe to call repeatedly with no pending writes (no-op).</summary>
    public void FlushOnce()
    {
        List<ResourceWrite> batch;
        lock (_queueLock)
        {
            if (_pending.Count == 0) return;
            var take = Math.Min(_pending.Count, MaxBatchSize);
            batch = _pending.GetRange(0, take);
            _pending.RemoveRange(0, take);
        }

        using var tx = _conn.BeginTransaction();
        _upsert!.Transaction = tx;
        _historyInsert!.Transaction = tx;

        foreach (var w in batch)
        {
            BindUpsert(w);
            _upsert.ExecuteNonQuery();

            if (w.ChangeAmount != 0)
            {
                BindHistory(w);
                _historyInsert.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    private void PreparedStatements()
    {
        _upsert = _conn.CreateCommand();
        _upsert.CommandText = @"
            INSERT INTO resources (owner_id, owner_kind, parent_owner_id, container, item_id, slot,
                                   quantity, flags, spiritbond, collectability, condition, glamour_id, updated_at)
            VALUES ($oid, $okind, $pid, $cont, $iid, $slot, $qty, $flags, $sb, $col, $cond, $glam, $ts)
            ON CONFLICT(owner_id, owner_kind, container, slot) DO UPDATE SET
                item_id        = excluded.item_id,
                quantity       = excluded.quantity,
                flags          = excluded.flags,
                spiritbond     = excluded.spiritbond,
                collectability = excluded.collectability,
                condition      = excluded.condition,
                glamour_id     = excluded.glamour_id,
                parent_owner_id= excluded.parent_owner_id,
                updated_at     = excluded.updated_at;";

        AddIntParam(_upsert, "$oid");
        AddIntParam(_upsert, "$okind");
        AddIntParam(_upsert, "$pid");
        AddIntParam(_upsert, "$cont");
        AddIntParam(_upsert, "$iid");
        AddIntParam(_upsert, "$slot");
        AddIntParam(_upsert, "$qty");
        AddIntParam(_upsert, "$flags");
        AddIntParam(_upsert, "$sb");
        AddIntParam(_upsert, "$col");
        AddIntParam(_upsert, "$cond");
        AddIntParam(_upsert, "$glam");
        AddIntParam(_upsert, "$ts");

        _historyInsert = _conn.CreateCommand();
        _historyInsert.CommandText = @"
            INSERT INTO resource_history (owner_id, owner_kind, container, item_id, timestamp,
                                          quantity, change_amount, source_kind, source_detail)
            VALUES ($oid, $okind, $cont, $iid, $ts, $qty, $chg, $sk, $sd);";

        AddIntParam(_historyInsert, "$oid");
        AddIntParam(_historyInsert, "$okind");
        AddIntParam(_historyInsert, "$cont");
        AddIntParam(_historyInsert, "$iid");
        AddIntParam(_historyInsert, "$ts");
        AddIntParam(_historyInsert, "$qty");
        AddIntParam(_historyInsert, "$chg");
        AddIntParam(_historyInsert, "$sk");
        _historyInsert.Parameters.Add("$sd", SqliteType.Text);
    }

    private static void AddIntParam(SqliteCommand cmd, string name) =>
        cmd.Parameters.Add(name, SqliteType.Integer);

    private void BindUpsert(ResourceWrite w)
    {
        var r = w.Resource;
        var k = r.Key;
        _upsert!.Parameters["$oid"].Value   = (long)k.OwnerId;
        _upsert.Parameters["$okind"].Value  = (int)k.OwnerKind;
        _upsert.Parameters["$pid"].Value    = 0L;
        _upsert.Parameters["$cont"].Value   = (int)k.Container;
        _upsert.Parameters["$iid"].Value    = (long)k.ItemId;
        _upsert.Parameters["$slot"].Value   = (int)k.Slot;
        _upsert.Parameters["$qty"].Value    = r.Quantity;
        _upsert.Parameters["$flags"].Value  = (int)r.Flags;
        _upsert.Parameters["$sb"].Value     = r.Spiritbond;
        _upsert.Parameters["$col"].Value    = r.Collectability;
        _upsert.Parameters["$cond"].Value   = r.Condition;
        _upsert.Parameters["$glam"].Value   = (long)r.GlamourId;
        _upsert.Parameters["$ts"].Value     = r.UpdatedAt.Ticks;
    }

    private void BindHistory(ResourceWrite w)
    {
        var r = w.Resource;
        var k = r.Key;
        _historyInsert!.Parameters["$oid"].Value   = (long)k.OwnerId;
        _historyInsert.Parameters["$okind"].Value  = (int)k.OwnerKind;
        _historyInsert.Parameters["$cont"].Value   = (int)k.Container;
        _historyInsert.Parameters["$iid"].Value    = (long)k.ItemId;
        _historyInsert.Parameters["$ts"].Value     = r.UpdatedAt.Ticks;
        _historyInsert.Parameters["$qty"].Value    = r.Quantity;
        _historyInsert.Parameters["$chg"].Value    = w.ChangeAmount;
        _historyInsert.Parameters["$sk"].Value     = (int)w.SourceKind;
        _historyInsert.Parameters["$sd"].Value     = (object?)w.SourceDetail ?? DBNull.Value;
    }

    public void Dispose()
    {
        _upsert?.Dispose();
        _historyInsert?.Dispose();
    }
}
