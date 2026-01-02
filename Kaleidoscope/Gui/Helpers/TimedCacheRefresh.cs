namespace Kaleidoscope.Gui.Helpers;

/// <summary>
/// Helper class for managing timed cache refresh patterns.
/// Encapsulates the common "check interval, refresh if stale" pattern used across multiple tools.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// private readonly TimedCacheRefresh _cacheRefresh = new(TimeSpan.FromSeconds(2));
/// 
/// private void RefreshIfNeeded()
/// {
///     if (!_cacheRefresh.ShouldRefresh()) return;
///     // ... perform refresh logic
/// }
/// </code>
/// </remarks>
public sealed class TimedCacheRefresh
{
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval;
    
    /// <summary>
    /// Creates a new TimedCacheRefresh with the specified interval.
    /// </summary>
    /// <param name="refreshInterval">The minimum time between refreshes.</param>
    public TimedCacheRefresh(TimeSpan refreshInterval)
    {
        _refreshInterval = refreshInterval;
    }
    
    /// <summary>
    /// Creates a new TimedCacheRefresh with the specified interval in seconds.
    /// </summary>
    /// <param name="refreshIntervalSeconds">The minimum time between refreshes in seconds.</param>
    public TimedCacheRefresh(double refreshIntervalSeconds)
        : this(TimeSpan.FromSeconds(refreshIntervalSeconds))
    {
    }
    
    /// <summary>
    /// Gets the configured refresh interval.
    /// </summary>
    public TimeSpan RefreshInterval => _refreshInterval;
    
    /// <summary>
    /// Gets the time of the last refresh.
    /// </summary>
    public DateTime LastRefresh => _lastRefresh;
    
    /// <summary>
    /// Gets the elapsed time since the last refresh.
    /// </summary>
    public TimeSpan TimeSinceLastRefresh => DateTime.UtcNow - _lastRefresh;
    
    /// <summary>
    /// Checks if a refresh is needed and marks the refresh time if so.
    /// Returns true if the interval has elapsed and a refresh should occur.
    /// This method automatically updates the last refresh time when returning true.
    /// </summary>
    public bool ShouldRefresh()
    {
        var now = DateTime.UtcNow;
        if (now - _lastRefresh < _refreshInterval)
            return false;
        
        _lastRefresh = now;
        return true;
    }
    
    /// <summary>
    /// Checks if a refresh is needed without updating the last refresh time.
    /// Use this when you need to check but might not actually perform the refresh.
    /// </summary>
    public bool IsStale() => DateTime.UtcNow - _lastRefresh >= _refreshInterval;
    
    /// <summary>
    /// Manually marks the current time as the last refresh time.
    /// Use this after successfully completing a refresh operation when using IsStale().
    /// </summary>
    public void MarkRefreshed() => _lastRefresh = DateTime.UtcNow;
    
    /// <summary>
    /// Forces the next ShouldRefresh() call to return true by resetting the last refresh time.
    /// </summary>
    public void Invalidate() => _lastRefresh = DateTime.MinValue;
    
    /// <summary>
    /// Executes the provided action if a refresh is needed.
    /// The refresh time is marked before the action executes.
    /// </summary>
    /// <param name="refreshAction">The action to execute when refresh is needed.</param>
    /// <returns>True if the action was executed, false if still within the refresh interval.</returns>
    public bool RefreshIfNeeded(Action refreshAction)
    {
        if (!ShouldRefresh())
            return false;
        
        refreshAction();
        return true;
    }
    
    /// <summary>
    /// Executes the provided function if a refresh is needed and returns the result.
    /// The refresh time is marked before the function executes.
    /// </summary>
    /// <typeparam name="T">The return type of the refresh function.</typeparam>
    /// <param name="refreshFunc">The function to execute when refresh is needed.</param>
    /// <param name="result">The result of the function, or default if not executed.</param>
    /// <returns>True if the function was executed, false if still within the refresh interval.</returns>
    public bool RefreshIfNeeded<T>(Func<T> refreshFunc, out T? result)
    {
        if (!ShouldRefresh())
        {
            result = default;
            return false;
        }
        
        result = refreshFunc();
        return true;
    }
}

/// <summary>
/// A version of TimedCacheRefresh that also stores a cached value.
/// </summary>
/// <typeparam name="T">The type of the cached value.</typeparam>
public sealed class TimedCache<T>
{
    private readonly TimedCacheRefresh _refresh;
    private T? _cachedValue;
    private bool _hasValue;
    
    /// <summary>
    /// Creates a new TimedCache with the specified interval.
    /// </summary>
    /// <param name="refreshInterval">The minimum time between refreshes.</param>
    public TimedCache(TimeSpan refreshInterval)
    {
        _refresh = new TimedCacheRefresh(refreshInterval);
    }
    
    /// <summary>
    /// Creates a new TimedCache with the specified interval in seconds.
    /// </summary>
    /// <param name="refreshIntervalSeconds">The minimum time between refreshes in seconds.</param>
    public TimedCache(double refreshIntervalSeconds)
        : this(TimeSpan.FromSeconds(refreshIntervalSeconds))
    {
    }
    
    /// <summary>
    /// Gets whether a value has been cached.
    /// </summary>
    public bool HasValue => _hasValue;
    
    /// <summary>
    /// Gets the cached value, or default if no value has been cached.
    /// </summary>
    public T? Value => _cachedValue;
    
    /// <summary>
    /// Gets whether the cache is stale and should be refreshed.
    /// </summary>
    public bool IsStale => _refresh.IsStale();
    
    /// <summary>
    /// Gets the value, refreshing it if stale using the provided factory function.
    /// </summary>
    /// <param name="factory">Function to create a new value when refresh is needed.</param>
    /// <returns>The cached or newly created value.</returns>
    public T GetOrRefresh(Func<T> factory)
    {
        if (_refresh.ShouldRefresh() || !_hasValue)
        {
            _cachedValue = factory();
            _hasValue = true;
        }
        return _cachedValue!;
    }
    
    /// <summary>
    /// Invalidates the cache, forcing the next GetOrRefresh call to refresh.
    /// </summary>
    public void Invalidate()
    {
        _refresh.Invalidate();
        _hasValue = false;
        _cachedValue = default;
    }
    
    /// <summary>
    /// Manually sets the cached value and marks it as fresh.
    /// </summary>
    public void Set(T value)
    {
        _cachedValue = value;
        _hasValue = true;
        _refresh.MarkRefreshed();
    }
}
