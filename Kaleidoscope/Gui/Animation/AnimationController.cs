using System;
using System.Collections.Generic;
using System.Numerics;

namespace Kaleidoscope.Gui.Animation;

/// <summary>
/// Frame-driven animation controller that manages named tweens.
/// Keyed by string (e.g. "tool_3_alpha", "tool_3_pos").
/// Call <see cref="Update"/> once per frame with the delta time.
/// </summary>
public sealed class AnimationController
{
    private readonly Dictionary<string, Tween> _tweens = new();
    private readonly Dictionary<string, TweenVec2> _vec2Tweens = new();
    private readonly List<string> _expiredKeys = new();
    private readonly List<string> _expiredVec2Keys = new();

    // Object pools to avoid allocations
    private readonly Stack<Tween> _tweenPool = new();
    private readonly Stack<TweenVec2> _vec2Pool = new();

    /// <summary>
    /// Starts a float tween. If a tween with the same key already exists, it is replaced.
    /// </summary>
    public void Start(string key, float from, float to, float duration, Func<float, float>? easing = null)
    {
        if (!_tweens.TryGetValue(key, out var tween))
        {
            tween = _tweenPool.Count > 0 ? _tweenPool.Pop() : new Tween();
            _tweens[key] = tween;
        }
        tween.Reset(from, to, duration, easing);
    }

    /// <summary>
    /// Starts a Vector2 tween. If a tween with the same key already exists, it is replaced.
    /// </summary>
    public void StartVec2(string key, Vector2 from, Vector2 to, float duration, Func<float, float>? easing = null)
    {
        if (!_vec2Tweens.TryGetValue(key, out var tween))
        {
            tween = _vec2Pool.Count > 0 ? _vec2Pool.Pop() : new TweenVec2();
            _vec2Tweens[key] = tween;
        }
        tween.Reset(from, to, duration, easing);
    }

    /// <summary>
    /// Advances all active tweens by <paramref name="deltaTime"/> seconds.
    /// Removes completed tweens to avoid unbounded growth.
    /// </summary>
    public void Update(float deltaTime)
    {
        // Update float tweens
        _expiredKeys.Clear();
        foreach (var kvp in _tweens)
        {
            kvp.Value.Update(deltaTime);
            if (!kvp.Value.IsPlaying)
                _expiredKeys.Add(kvp.Key);
        }
        foreach (var key in _expiredKeys)
        {
            var tween = _tweens[key];
            _tweens.Remove(key);
            _tweenPool.Push(tween);
        }

        // Update Vec2 tweens
        _expiredVec2Keys.Clear();
        foreach (var kvp in _vec2Tweens)
        {
            kvp.Value.Update(deltaTime);
            if (!kvp.Value.IsPlaying)
                _expiredVec2Keys.Add(kvp.Key);
        }
        foreach (var key in _expiredVec2Keys)
        {
            var vec2 = _vec2Tweens[key];
            _vec2Tweens.Remove(key);
            _vec2Pool.Push(vec2);
        }
    }

    /// <summary>
    /// Gets the current float tween value, or <paramref name="fallback"/> if no tween is active for the key.
    /// </summary>
    public float Get(string key, float fallback)
    {
        return _tweens.TryGetValue(key, out var tween) ? tween.Value : fallback;
    }

    /// <summary>
    /// Gets the current Vec2 tween value, or <paramref name="fallback"/> if no tween is active for the key.
    /// </summary>
    public Vector2 GetVec2(string key, Vector2 fallback)
    {
        return _vec2Tweens.TryGetValue(key, out var tween) ? tween.Value : fallback;
    }

    /// <summary>Whether a float tween is currently active for the given key.</summary>
    public bool IsAnimating(string key)
    {
        return _tweens.TryGetValue(key, out var tween) && tween.IsPlaying;
    }

    /// <summary>Whether a Vec2 tween is currently active for the given key.</summary>
    public bool IsAnimatingVec2(string key)
    {
        return _vec2Tweens.TryGetValue(key, out var tween) && tween.IsPlaying;
    }

    /// <summary>Whether any animation is currently playing.</summary>
    public bool HasActiveAnimations => _tweens.Count > 0 || _vec2Tweens.Count > 0;

    /// <summary>Cancels a specific tween by key.</summary>
    public void Cancel(string key)
    {
        if (_tweens.Remove(key, out var tween))
            _tweenPool.Push(tween);
        if (_vec2Tweens.Remove(key, out var vec2))
            _vec2Pool.Push(vec2);
    }

    /// <summary>Cancels all active tweens.</summary>
    public void CancelAll()
    {
        foreach (var tween in _tweens.Values) _tweenPool.Push(tween);
        _tweens.Clear();
        foreach (var vec2 in _vec2Tweens.Values) _vec2Pool.Push(vec2);
        _vec2Tweens.Clear();
    }
}
