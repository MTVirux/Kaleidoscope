using System;

namespace Kaleidoscope.Gui.Animation;

/// <summary>
/// A single-value tween that interpolates from one float to another over a duration,
/// using an easing function. Call <see cref="Update"/> each frame with the delta time.
/// </summary>
public sealed class Tween
{
    private float _from;
    private float _to;
    private float _duration;
    private float _elapsed;
    private Func<float, float> _easing;

    /// <summary>Whether the tween is currently active (not yet finished).</summary>
    public bool IsPlaying => _elapsed < _duration && _duration > 0f;

    /// <summary>The current interpolated value.</summary>
    public float Value
    {
        get
        {
            if (_duration <= 0f) return _to;
            var t = Math.Clamp(_elapsed / _duration, 0f, 1f);
            return _from + _easing(t) * (_to - _from);
        }
    }

    /// <summary>Normalized progress [0..1].</summary>
    public float Progress => _duration > 0f ? Math.Clamp(_elapsed / _duration, 0f, 1f) : 1f;

    public Tween()
    {
        _easing = Easing.Linear;
    }

    /// <summary>
    /// Starts or restarts the tween with the given parameters.
    /// </summary>
    public void Reset(float from, float to, float duration, Func<float, float>? easing = null)
    {
        _from = from;
        _to = to;
        _duration = MathF.Max(0f, duration);
        _elapsed = 0f;
        _easing = easing ?? Easing.Linear;
    }

    /// <summary>
    /// Advances the tween by <paramref name="dt"/> seconds.
    /// </summary>
    public void Update(float dt)
    {
        _elapsed = MathF.Min(_elapsed + dt, _duration);
    }

    /// <summary>
    /// Immediately finishes the tween, snapping <see cref="Value"/> to the target.
    /// </summary>
    public void Finish()
    {
        _elapsed = _duration;
    }
}
