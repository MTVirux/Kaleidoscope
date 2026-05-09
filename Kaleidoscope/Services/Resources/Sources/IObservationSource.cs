namespace Kaleidoscope.Services.Resources.Sources;

/// <summary>
/// Marker for source-attribution detectors. Concrete implementations watch an independent
/// signal (duty completion, addon lifecycle, etc.) and stamp the next observation with
/// a SourceTag via the shared SourceTagSink. They have no methods — DI registration
/// drives instantiation; lifecycle is event subscription in the constructor and
/// unsubscription in Dispose.
/// </summary>
public interface IObservationSource : IDisposable, OtterGui.Services.IRequiredService { }
