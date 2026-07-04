using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources.Sources;

/// <summary>
/// Base for source-attribution detectors. Each concrete detector watches one independent signal
/// (duty completion, retainer summon, …) and stamps the next observation(s) with a SourceTag through
/// the shared sink. The base owns the sink handle and the stamp helper; a subclass wires its event in
/// its constructor and unwires it in Dispose. Concrete detectors declare IRequiredService directly so
/// DI discovers them; this abstract base is not itself a service.
/// </summary>
public abstract class ObservationSourceBase : IDisposable
{
    private readonly SourceTagSink _sink;

    protected ObservationSourceBase(ResourceObservationService obsSvc)
    {
        _sink = obsSvc.Sink;
    }

    /// <summary>Stamp the next observation window with a source tag. Blank details normalize to null.</summary>
    protected void Stamp(SourceKind kind, string? detail, TimeSpan ttl)
    {
        _sink.Stamp(new SourceTag
        {
            Kind      = kind,
            Detail    = string.IsNullOrWhiteSpace(detail) ? null : detail,
            StampedAt = DateTime.UtcNow,
        }, ttl);
    }

    public abstract void Dispose();
}
