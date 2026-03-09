namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// Shared UI color constants for combo widget styling.
/// Provides semantic color names for consistent appearance.
/// </summary>
/// <remarks>
/// ABGR format: 0xAABBGGRR where AA=Alpha, BB=Blue, GG=Green, RR=Red.
/// This is the native format used by ImGui's uint color parameters.
/// </remarks>
public static class ComboStyles
{
    // === Favorite Star Colors (uint ABGR format for ImGui native) ===
    
    /// <summary>Active favorite star color (yellow-gold) - ABGR format.</summary>
    public const uint FavoriteStarOn = 0xFF00CFFF;
    
    /// <summary>Inactive favorite star color (dim white) - ABGR format.</summary>
    public const uint FavoriteStarOff = 0x40FFFFFF;
    
    /// <summary>Hovered favorite star color (bright gold) - ABGR format.</summary>
    public const uint FavoriteStarHovered = 0xFF40DFFF;
    
    // === Selection Colors (uint ABGR format) ===
    
    /// <summary>Selected item background color (dim green) - ABGR format.</summary>
    public const uint SelectedBackground = 0x40008000;
    
    /// <summary>Partial selection checkmark color (gray) - ABGR format.</summary>
    public const uint PartialCheckmark = 0xFF888888;
    
    /// <summary>Full selection checkmark color (white) - ABGR format.</summary>
    public const uint FullCheckmark = 0xFFFFFFFF;
    
    // === Text Colors (uint ABGR format) ===
    
    /// <summary>Dimmed/secondary text color (gray) - ABGR format.</summary>
    public const uint SecondaryText = 0xFF808080;
    
    // === Helper Methods ===
    
    /// <summary>
    /// Gets the favorite star color based on state.
    /// </summary>
    /// <param name="isFavorite">Whether the item is a favorite.</param>
    /// <param name="isHovered">Whether the star is being hovered.</param>
    /// <returns>The appropriate color.</returns>
    public static uint GetFavoriteStarColor(bool isFavorite, bool isHovered)
    {
        if (isHovered) return FavoriteStarHovered;
        return isFavorite ? FavoriteStarOn : FavoriteStarOff;
    }
}
