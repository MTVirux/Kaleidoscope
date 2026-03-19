using System;
using System.Numerics;

namespace Kaleidoscope.Gui.Animation;

/// <summary>
/// A Vector2 tween that interpolates from one position/size to another over a duration,
/// lerping X and Y independently through the same easing curve.
/// </summary>
public sealed class TweenVec2
{
    private Vector2 _from;
    private Vector2 _to;
    private float _duration;
    private float _elapsed;
    private Func<float, float> _easing;

    /// <summary>Whether the tween is currently active (not yet finished).</summary>
    public bool IsPlaying => _elapsed < _duration && _duration > 0f;

    /// <summary>The current interpolated value.</summary>
    public Vector2 Value
    {
        get
        {
            if (_duration <= 0f) return _to;
            var t = Math.Clamp(_elapsed / _duration, 0f, 1f);
            var e = _easing(t);
            return new Vector2(
                _from.X + e * (_to.X - _from.X),
                _from.Y + e * (_to.Y - _from.Y));
        }
    }

    /// <summary>Normalized progress [0..1].</summary>
    public float Progress => _duration > 0f ? Math.Clamp(_elapsed / _duration, 0f, 1f) : 1f;

    public TweenVec2()
    {
        _easing = Easing.Linear;
    }

    /// <summary>
    /// Starts or restarts the tween with the given parameters.
    /// </summary>
    public void Reset(Vector2 from, Vector2 to, float duration, Func<float, float>? easing = null)
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
