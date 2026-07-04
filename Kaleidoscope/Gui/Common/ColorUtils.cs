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
    /// Inverts the RGB channels of a color while preserving its alpha.
    /// </summary>
    /// <param name="color">The color to invert (RGBA, 0-1).</param>
    /// <returns>A color with each RGB channel replaced by (1 - channel), alpha unchanged.</returns>
    public static Vector4 Invert(Vector4 color)
        => new(1f - color.X, 1f - color.Y, 1f - color.Z, color.W);

    /// <summary>
    /// Converts HSV color values to an RGB Vector4.
    /// </summary>
    /// <param name="h">Hue (0-1).</param>
    /// <param name="s">Saturation (0-1).</param>
    /// <param name="v">Value/Brightness (0-1).</param>
    /// <returns>RGB color as Vector4 with alpha = 1.</returns>
    public static Vector4 HsvToRgb(float h, float s, float v)
    {
        float r, g, b;

        int i = (int)(h * 6);
        float f = h * 6 - i;
        float p = v * (1 - s);
        float q = v * (1 - f * s);
        float t = v * (1 - (1 - f) * s);

        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }

        return new Vector4(r, g, b, 1f);
    }
}
