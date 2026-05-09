using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// TTL-bounded source-tag sink. Detectors stamp; RecordObservation consumes.
/// Last-writer-wins on stamp; expired tags self-clear on consume.
/// All access serialized externally by ResourceObservationService's lock.
/// </summary>
public sealed class SourceTagSink
{
    private readonly Func<DateTime> _now;
    private SourceTag? _tag;
    private DateTime _expiresAt;

    public SourceTagSink() : this(() => DateTime.UtcNow) { }

    public SourceTagSink(Func<DateTime> now) => _now = now;

    public DateTime Now() => _now();

    public void Stamp(SourceTag tag, TimeSpan ttl)
    {
        _tag = tag;
        _expiresAt = _now() + ttl;
    }

    public SourceTag? ConsumeIfFresh()
    {
        if (_tag is null) return null;
        if (_now() > _expiresAt)
        {
            _tag = null;
            return null;
        }
        var t = _tag;
        _tag = null;
        return t;
    }
}
