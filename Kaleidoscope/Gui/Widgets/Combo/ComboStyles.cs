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
    public const uint TextDimmed = 0xFF808080;
    
    /// <summary>Secondary info color (e.g., world name, category) - ABGR format.</summary>
    public const uint SecondaryText = 0xFF808080;
    
    // === Helper Methods ===
    
    /// <summary>
    /// Converts ABGR uint to Vector4 (RGBA float format for ImGui.ColorEdit).
    /// </summary>
    public static Vector4 ToVector4(uint abgr)
    {
        var r = (abgr & 0xFF) / 255f;
        var g = ((abgr >> 8) & 0xFF) / 255f;
        var b = ((abgr >> 16) & 0xFF) / 255f;
        var a = ((abgr >> 24) & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }
    
    /// <summary>
    /// Converts Vector4 (RGBA float) to ABGR uint format.
    /// </summary>
    public static uint FromVector4(Vector4 rgba)
    {
        var r = (uint)(rgba.X * 255) & 0xFF;
        var g = (uint)(rgba.Y * 255) & 0xFF;
        var b = (uint)(rgba.Z * 255) & 0xFF;
        var a = (uint)(rgba.W * 255) & 0xFF;
        return r | (g << 8) | (b << 16) | (a << 24);
    }
    
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
    
    // === Vector4 versions for ImGui styling ===
    
    /// <summary>Favorite star on color as Vector4.</summary>
    public static readonly Vector4 FavoriteStarOnVec4 = ToVector4(FavoriteStarOn);
    
    /// <summary>Favorite star off color as Vector4.</summary>
    public static readonly Vector4 FavoriteStarOffVec4 = ToVector4(FavoriteStarOff);
    
    /// <summary>Favorite star hovered color as Vector4.</summary>
    public static readonly Vector4 FavoriteStarHoveredVec4 = ToVector4(FavoriteStarHovered);
    
    /// <summary>Secondary text color as Vector4.</summary>
    public static readonly Vector4 SecondaryTextVec4 = ToVector4(SecondaryText);
}
