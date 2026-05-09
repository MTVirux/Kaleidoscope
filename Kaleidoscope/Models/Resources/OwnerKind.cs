namespace Kaleidoscope.Models.Resources;

/// <summary>
/// What kind of entity owns a Resource. Combined with OwnerId to form a unique owner identity.
/// </summary>
public enum OwnerKind
{
    Player = 0,
    Retainer = 1,
    FreeCompany = 2,
}
