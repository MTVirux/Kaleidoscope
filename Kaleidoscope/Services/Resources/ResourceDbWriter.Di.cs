using Kaleidoscope.Services.Database;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// DI-facing partial: adds IRequiredService so OtterGui auto-discovers this type,
/// and provides the KaleidoscopeDbService constructor overload for production DI.
/// The test project compiles only ResourceDbWriter.cs (not this file), so it never
/// sees the KaleidoscopeDbService dependency.
/// </summary>
public sealed partial class ResourceDbWriter : IRequiredService
{
    /// <summary>
    /// Production DI constructor. Resolves the writer connection from the db service;
    /// throws if the connection is not yet open (forces correct service init ordering).
    /// </summary>
    public ResourceDbWriter(KaleidoscopeDbService db)
        : this(db.GetWriterConnection() ?? throw new InvalidOperationException("KaleidoscopeDb writer connection not open"))
    {
    }
}
