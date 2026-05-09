namespace Kaleidoscope.Models.Resources;

/// <summary>
/// Bitfield of per-resource flags. Spiritbond and collectability live in dedicated fields,
/// not here, because they're numeric and demuxed from the game's combined SpiritbondOrCollectability.
/// </summary>
[Flags]
public enum ResourceFlags
{
    None = 0,
    HQ = 1,
    Collectable = 2,
}
