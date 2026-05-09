namespace Kaleidoscope.Services.Resources;

/// <summary>
/// Static catalog of resource identity helpers — synthetic item IDs for game-memory-only
/// counters, GameInventoryType ↔ Container mapping, and legacy variable-name parsing
/// for the schema 1→2 migration.
/// </summary>
public static class ResourceCatalog
{
    // Synthetic item IDs for values that don't have a real game item ID.
    // Chosen well above any real item ID (game's range is < 100,000) so no collision possible.
    public const uint GilItemId         = 1_000_001;
    public const uint MGPItemId         = 1_000_002;
    public const uint WolfMarksItemId   = 1_000_003;
    public const uint AlliedSealsItemId = 1_000_004;
    public const uint FCCreditsItemId   = 1_000_005;
}
