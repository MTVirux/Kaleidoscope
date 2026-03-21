using System;

namespace Kaleidoscope.Gui.Animation;

/// <summary>
/// Static easing functions mapping a normalized time t ∈ [0,1] to an eased value.
/// Used by <see cref="Tween"/> and <see cref="AnimationController"/> for smooth transitions.
/// </summary>
public static class Easing
{
    /// <summary>Linear interpolation (identity): f(t) = t.</summary>
    public static float Linear(float t) => t;

    /// <summary>Quadratic ease-in: f(t) = t².</summary>
    public static float QuadIn(float t) => t * t;

    /// <summary>Quadratic ease-out: f(t) = 1 − (1−t)².</summary>
    public static float QuadOut(float t)
    {
        var inv = 1f - t;
        return 1f - inv * inv;
    }

    /// <summary>Quadratic ease-in-out: smooth acceleration then deceleration.</summary>
    public static float QuadInOut(float t) =>
        t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);

    /// <summary>Cubic ease-in: f(t) = t³.</summary>
    public static float CubicIn(float t) => t * t * t;

    /// <summary>Cubic ease-out: f(t) = 1 − (1−t)³.</summary>
    public static float CubicOut(float t)
    {
        var inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    /// <summary>Cubic ease-in-out: smooth acceleration then deceleration.</summary>
    public static float CubicInOut(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;

    /// <summary>Hermite smooth step: f(t) = 3t² − 2t³.</summary>
    public static float SmoothStep(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// Spring overshoot with damped oscillation.
    /// Overshoots the target slightly then settles. Good for playful UI.
    /// </summary>
    public static float Spring(float t)
    {
        // Attempt a single overshoot with exponential decay
        // Approximation: 1 − cos(4.5π·t) · e^(−6t)
        return 1f - MathF.Cos(4.5f * MathF.PI * t) * MathF.Exp(-6f * t);
    }
}
