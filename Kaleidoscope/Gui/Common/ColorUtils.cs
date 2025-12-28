using System.Numerics;

namespace Kaleidoscope.Gui.Common;

/// <summary>
/// Utility methods for color conversions between Vector4 (RGBA) and uint (ABGR) formats.
/// ImGui uses ABGR uint format internally, while Vector4 uses RGBA float format.
/// </summary>
public static class ColorUtils
{
    /// <summary>
    /// Converts a uint color (ABGR format from ImGui) to Vector4 (RGBA).
    /// </summary>
    /// <param name="color">The uint color in ABGR format.</param>
    /// <returns>A Vector4 with components in RGBA order, values 0-1.</returns>
    public static Vector4 UintToVector4(uint color)
    {
        var r = (color & 0xFF) / 255f;
        var g = ((color >> 8) & 0xFF) / 255f;
        var b = ((color >> 16) & 0xFF) / 255f;
        var a = ((color >> 24) & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }

    /// <summary>
    /// Converts a Vector4 color (RGBA) to uint (ABGR format for ImGui).
    /// </summary>
    /// <param name="color">A Vector4 with components in RGBA order, values 0-1.</param>
    /// <returns>The uint color in ABGR format.</returns>
    public static uint Vector4ToUint(Vector4 color)
    {
        var r = (uint)(Math.Clamp(color.X, 0f, 1f) * 255f);
        var g = (uint)(Math.Clamp(color.Y, 0f, 1f) * 255f);
        var b = (uint)(Math.Clamp(color.Z, 0f, 1f) * 255f);
        var a = (uint)(Math.Clamp(color.W, 0f, 1f) * 255f);
        return r | (g << 8) | (b << 16) | (a << 24);
    }

    /// <summary>
    /// Creates a Vector4 color from RGB byte values with full opacity.
    /// </summary>
    /// <param name="r">Red component (0-255).</param>
    /// <param name="g">Green component (0-255).</param>
    /// <param name="b">Blue component (0-255).</param>
    /// <returns>A Vector4 with the specified color and alpha = 1.</returns>
    public static Vector4 FromRgb(byte r, byte g, byte b)
    {
        return new Vector4(r / 255f, g / 255f, b / 255f, 1f);
    }

    /// <summary>
    /// Creates a Vector4 color from RGBA byte values.
    /// </summary>
    /// <param name="r">Red component (0-255).</param>
    /// <param name="g">Green component (0-255).</param>
    /// <param name="b">Blue component (0-255).</param>
    /// <param name="a">Alpha component (0-255).</param>
    /// <returns>A Vector4 with the specified color.</returns>
    public static Vector4 FromRgba(byte r, byte g, byte b, byte a)
    {
        return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
    }
}
