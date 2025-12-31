using System.Diagnostics;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Tracks draw times for tools, windows, and other profiling metrics.
/// </summary>
public sealed class ProfilerService : IDisposable, IService
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly object _lock = new();
    
    /// <summary>
    /// Thread-local context for the current tool being profiled.
    /// Allows nested widgets to add child scopes without explicit service passing.
    /// </summary>
    [ThreadStatic]
    private static ProfilerContext? _currentContext;
    
    /// <summary>
    /// Slow operation threshold in milliseconds. Operations exceeding this will be logged.
    /// </summary>
    public double SlowOperationThresholdMs
    {
        get => Config.ProfilerSlowOperationThresholdMs;
        set
        {
            if (Math.Abs(Config.ProfilerSlowOperationThresholdMs - value) < 0.001) return;
            Config.ProfilerSlowOperationThresholdMs = value;
            _configService.MarkDirty();
        }
    }
    
    /// <summary>
    /// Whether to log slow operations to the Dalamud log.
    /// </summary>
    public bool LogSlowOperations
    {
        get => Config.ProfilerLogSlowOperations;
        set
        {
            if (Config.ProfilerLogSlowOperations == value) return;
            Config.ProfilerLogSlowOperations = value;
            _configService.MarkDirty();
        }
    }
    
    /// <summary>
    /// Gets the current profiler context for this thread, if any.
    /// </summary>
    public static ProfilerContext? CurrentContext => _currentContext;

    /// <summary>
    /// Ring buffer size for recent samples (used for rolling stats and percentiles).
    /// </summary>
    private const int RingBufferSize = 600; // ~10 seconds at 60fps

    /// <summary>
    /// Statistics for a single profiled target with detailed metrics.
    /// </summary>
    public class ProfileStats
    {
        public string Name { get; set; } = string.Empty;
        
        // Basic stats
        public double LastDrawTimeMs { get; set; }
        public double MinDrawTimeMs { get; set; } = double.MaxValue;
        public double MaxDrawTimeMs { get; set; } = double.MinValue;
        public double TotalDrawTimeMs { get; set; }
        public long SampleCount { get; set; }
        public double AverageDrawTimeMs => SampleCount > 0 ? TotalDrawTimeMs / SampleCount : 0;

        // Ring buffer for recent samples
        private readonly double[] _recentSamples = new double[RingBufferSize];
        private readonly long[] _recentTimestamps = new long[RingBufferSize]; // Ticks when sample was recorded
        private int _ringIndex;
        private int _ringCount;

        // For standard deviation calculation (Welford's online algorithm)
        private double _m2; // Sum of squared differences from the mean

        // Child scopes for hierarchical profiling
        private Dictionary<string, ProfileStats>? _childScopes;
        
        /// <summary>
        /// Gets child scopes for hierarchical profiling.
        /// </summary>
        public IReadOnlyDictionary<string, ProfileStats> ChildScopes => 
            _childScopes ?? (IReadOnlyDictionary<string, ProfileStats>)new Dictionary<string, ProfileStats>();

        /// <summary>
        /// Gets or creates a child scope for hierarchical profiling.
        /// </summary>
        public ProfileStats GetOrCreateChildScope(string name)
        {
            _childScopes ??= new Dictionary<string, ProfileStats>();
            if (!_childScopes.TryGetValue(name, out var child))
            {
                child = new ProfileStats { Name = name };
                _childScopes[name] = child;
            }
            return child;
        }

        /// <summary>
        /// Standard deviation of all samples (population).
        /// </summary>
        public double StandardDeviationMs => SampleCount > 1 ? Math.Sqrt(_m2 / SampleCount) : 0;

        /// <summary>
        /// Effective FPS based on average draw time (theoretical max if only this component ran).
        /// </summary>
        public double EffectiveFps => AverageDrawTimeMs > 0 ? 1000.0 / AverageDrawTimeMs : 0;

        /// <summary>
        /// Gets the number of samples in the ring buffer.
        /// </summary>
        public int RecentSampleCount => _ringCount;

        /// <summary>
        /// Gets the 50th percentile (median) from recent samples.
        /// </summary>
        public double P50Ms => GetPercentile(50);

        /// <summary>
        /// Gets the 90th percentile from recent samples.
        /// </summary>
        public double P90Ms => GetPercentile(90);

        /// <summary>
        /// Gets the 95th percentile from recent samples.
        /// </summary>
        public double P95Ms => GetPercentile(95);

        /// <summary>
        /// Gets the 99th percentile from recent samples.
        /// </summary>
        public double P99Ms => GetPercentile(99);

        /// <summary>
        /// Gets the average of samples from the last N seconds.
        /// </summary>
        public double GetRollingAverageMs(double seconds)
        {
            if (_ringCount == 0) return 0;
            
            var cutoffTicks = DateTime.UtcNow.Ticks - (long)(seconds * TimeSpan.TicksPerSecond);
            var sum = 0.0;
            var count = 0;
            
            for (var i = 0; i < _ringCount; i++)
            {
                var idx = (_ringIndex - 1 - i + RingBufferSize) % RingBufferSize;
                if (_recentTimestamps[idx] >= cutoffTicks)
                {
                    sum += _recentSamples[idx];
                    count++;
                }
                else
                {
                    break; // Samples are in order, so we can stop
                }
            }
            
            return count > 0 ? sum / count : 0;
        }

        /// <summary>
        /// Gets the 1-second rolling average.
        /// </summary>
        public double Rolling1SecMs => GetRollingAverageMs(1.0);

        /// <summary>
        /// Gets the 5-second rolling average.
        /// </summary>
        public double Rolling5SecMs => GetRollingAverageMs(5.0);

        /// <summary>
        /// Gets samples per second (actual render rate for this component).
        /// </summary>
        public double SamplesPerSecond
        {
            get
            {
                if (_ringCount < 2) return 0;
                
                var cutoffTicks = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond;
                var count = 0;
                
                for (var i = 0; i < _ringCount; i++)
                {
                    var idx = (_ringIndex - 1 - i + RingBufferSize) % RingBufferSize;
                    if (_recentTimestamps[idx] >= cutoffTicks)
                        count++;
                    else
                        break;
                }
                
                return count;
            }
        }

        /// <summary>
        /// Gets the jitter (difference between max and min in recent samples).
        /// </summary>
        public double JitterMs
        {
            get
            {
                if (_ringCount < 2) return 0;
                var min = double.MaxValue;
                var max = double.MinValue;
                for (var i = 0; i < _ringCount; i++)
                {
                    var sample = _recentSamples[i];
                    if (sample < min) min = sample;
                    if (sample > max) max = sample;
                }
                return max - min;
            }
        }

        /// <summary>
        /// Gets recent samples as a copy for histogram display.
        /// </summary>
        public double[] GetRecentSamples()
        {
            var result = new double[_ringCount];
            for (var i = 0; i < _ringCount; i++)
            {
                var idx = (_ringIndex - _ringCount + i + RingBufferSize) % RingBufferSize;
                result[i] = _recentSamples[idx];
            }
            return result;
        }

        private double GetPercentile(int percentile)
        {
            if (_ringCount == 0) return 0;
            
            // Copy and sort recent samples
            var samples = new double[_ringCount];
            for (var i = 0; i < _ringCount; i++)
            {
                samples[i] = _recentSamples[i];
            }
            Array.Sort(samples);
            
            // Calculate percentile index
            var index = (int)Math.Ceiling(percentile / 100.0 * _ringCount) - 1;
            index = Math.Max(0, Math.Min(index, _ringCount - 1));
            
            return samples[index];
        }

        public void Reset()
        {
            LastDrawTimeMs = 0;
            MinDrawTimeMs = double.MaxValue;
            MaxDrawTimeMs = double.MinValue;
            TotalDrawTimeMs = 0;
            SampleCount = 0;
            _m2 = 0;
            _ringIndex = 0;
            _ringCount = 0;
            Array.Clear(_recentSamples, 0, _recentSamples.Length);
            Array.Clear(_recentTimestamps, 0, _recentTimestamps.Length);
            
            if (_childScopes != null)
            {
                foreach (var child in _childScopes.Values)
                    child.Reset();
            }
        }

        public void RecordSample(double drawTimeMs)
        {
            // Update basic stats
            LastDrawTimeMs = drawTimeMs;
            if (drawTimeMs < MinDrawTimeMs) MinDrawTimeMs = drawTimeMs;
            if (drawTimeMs > MaxDrawTimeMs) MaxDrawTimeMs = drawTimeMs;
            TotalDrawTimeMs += drawTimeMs;
            SampleCount++;

            // Update Welford's algorithm for standard deviation
            var delta = drawTimeMs - AverageDrawTimeMs;
            _m2 += delta * (drawTimeMs - AverageDrawTimeMs);

            // Add to ring buffer
            _recentSamples[_ringIndex] = drawTimeMs;
            _recentTimestamps[_ringIndex] = DateTime.UtcNow.Ticks;
            _ringIndex = (_ringIndex + 1) % RingBufferSize;
            if (_ringCount < RingBufferSize) _ringCount++;
        }
    }

    // Window stats
    private readonly ProfileStats _mainWindowStats = new() { Name = "Main Window" };
    private readonly ProfileStats _fullscreenWindowStats = new() { Name = "Fullscreen Window" };

    // Tool stats (keyed by tool ID)
    private readonly Dictionary<string, ProfileStats> _toolStats = new();

    private Configuration Config => _configService.Config;

    /// <summary>
    /// Gets or sets whether profiling is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => Config.ProfilerEnabled;
        set
        {
            if (Config.ProfilerEnabled == value) return;
            Config.ProfilerEnabled = value;
            _configService.MarkDirty();
        }
    }

    public ProfilerService(IPluginLog log, ConfigurationService configService)
    {
        _log = log;
        _configService = configService;
        _log.Debug("ProfilerService initialized");
    }

    /// <summary>
    /// Gets the main window profile stats.
    /// </summary>
    public ProfileStats MainWindowStats => _mainWindowStats;

    /// <summary>
    /// Gets the fullscreen window profile stats.
    /// </summary>
    public ProfileStats FullscreenWindowStats => _fullscreenWindowStats;

    /// <summary>
    /// Gets all tool profile stats.
    /// </summary>
    public IReadOnlyDictionary<string, ProfileStats> ToolStats
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, ProfileStats>(_toolStats);
            }
        }
    }

    /// <summary>
    /// Records a draw time sample for the main window.
    /// </summary>
    public void RecordMainWindowDraw(double drawTimeMs)
    {
        if (!IsEnabled) return;
        _mainWindowStats.RecordSample(drawTimeMs);
    }

    /// <summary>
    /// Records a draw time sample for the fullscreen window.
    /// </summary>
    public void RecordFullscreenWindowDraw(double drawTimeMs)
    {
        if (!IsEnabled) return;
        _fullscreenWindowStats.RecordSample(drawTimeMs);
    }

    /// <summary>
    /// Records a draw time sample for a specific tool.
    /// </summary>
    public void RecordToolDraw(string toolId, string toolName, double drawTimeMs)
    {
        if (!IsEnabled) return;

        lock (_lock)
        {
            if (!_toolStats.TryGetValue(toolId, out var stats))
            {
                stats = new ProfileStats { Name = toolName };
                _toolStats[toolId] = stats;
            }
            stats.RecordSample(drawTimeMs);
        }
    }

    /// <summary>
    /// Resets all profiling statistics.
    /// </summary>
    public void ResetAll()
    {
        _mainWindowStats.Reset();
        _fullscreenWindowStats.Reset();

        lock (_lock)
        {
            foreach (var stats in _toolStats.Values)
            {
                stats.Reset();
            }
        }

        _log.Debug("ProfilerService: All stats reset");
    }

    /// <summary>
    /// Resets statistics for a specific tool.
    /// </summary>
    public void ResetTool(string toolId)
    {
        lock (_lock)
        {
            if (_toolStats.TryGetValue(toolId, out var stats))
            {
                stats.Reset();
            }
        }
    }

    /// <summary>
    /// Clears all tool statistics (removes all entries).
    /// </summary>
    public void ClearToolStats()
    {
        lock (_lock)
        {
            _toolStats.Clear();
        }
    }

    /// <summary>
    /// Creates a scoped timer that records draw time on dispose.
    /// </summary>
    public ProfileScope BeginMainWindowScope() => new(this, ProfileTargetType.MainWindow, string.Empty, string.Empty);

    /// <summary>
    /// Creates a scoped timer that records draw time on dispose.
    /// </summary>
    public ProfileScope BeginFullscreenWindowScope() => new(this, ProfileTargetType.FullscreenWindow, string.Empty, string.Empty);

    /// <summary>
    /// Creates a scoped timer that records draw time on dispose.
    /// </summary>
    public ProfileScope BeginToolScope(string toolId, string toolName) => new(this, ProfileTargetType.Tool, toolId, toolName);

    /// <summary>
    /// Creates a nested scoped timer for profiling a child operation within a tool.
    /// </summary>
    public ChildProfileScope BeginChildScope(string toolId, string childScopeName)
    {
        if (!IsEnabled)
        {
            return new ChildProfileScope(null, null, null, null, null);
        }
        
        lock (_lock)
        {
            // Get or create parent stats - ensures child scopes work even on the first frame
            if (!_toolStats.TryGetValue(toolId, out var parentStats))
            {
                // Get tool name from current context if available
                var toolName = _currentContext?.ToolName ?? toolId;
                parentStats = new ProfileStats { Name = toolName };
                _toolStats[toolId] = parentStats;
            }
            
            var childStats = parentStats.GetOrCreateChildScope(childScopeName);
            return new ChildProfileScope(childStats, Stopwatch.StartNew(), this, childScopeName, parentStats.Name);
        }
    }
    
    /// <summary>
    /// Creates a child scope using the current thread-local context.
    /// This is the preferred way to add child profiling from nested widgets.
    /// </summary>
    /// <param name="scopeName">Name of the operation being profiled.</param>
    /// <returns>A disposable scope that records the elapsed time.</returns>
    public static ChildProfileScope BeginStaticChildScope(string scopeName)
    {
        var context = _currentContext;
        if (context == null)
        {
            return new ChildProfileScope(null, null, null, null, null);
        }
        return context.BeginScope(scopeName);
    }

    /// <summary>
    /// Gets current GC collection counts for monitoring allocations.
    /// </summary>
    public (int gen0, int gen1, int gen2) GetGcCollectionCounts() =>
        (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    /// <summary>
    /// Gets the total managed memory in bytes.
    /// </summary>
    public long GetTotalManagedMemory() => GC.GetTotalMemory(forceFullCollection: false);

    public void Dispose()
    {
        _log.Debug("ProfilerService disposed");
    }

    public enum ProfileTargetType
    {
        MainWindow,
        FullscreenWindow,
        Tool
    }

    /// <summary>
    /// Context for the current profiling scope, allowing nested widgets to add child scopes.
    /// </summary>
    public sealed class ProfilerContext
    {
        public ProfilerService Service { get; }
        public string ToolId { get; }
        public string ToolName { get; }
        
        internal ProfilerContext(ProfilerService service, string toolId, string toolName)
        {
            Service = service;
            ToolId = toolId;
            ToolName = toolName;
        }
        
        /// <summary>
        /// Begins a child scope for timing a sub-operation within the current tool.
        /// </summary>
        /// <param name="scopeName">Name of the sub-operation being timed.</param>
        public ChildProfileScope BeginScope(string scopeName) => Service.BeginChildScope(ToolId, scopeName);
    }

    /// <summary>
    /// Disposable scope for timing draw operations.
    /// Sets up thread-local context for nested child scope access.
    /// </summary>
    public readonly struct ProfileScope : IDisposable
    {
        private readonly ProfilerService _service;
        private readonly ProfileTargetType _targetType;
        private readonly string _toolId;
        private readonly string _toolName;
        private readonly Stopwatch _stopwatch;
        private readonly ProfilerContext? _previousContext;

        public ProfileScope(ProfilerService service, ProfileTargetType targetType, string toolId, string toolName)
        {
            _service = service;
            _targetType = targetType;
            _toolId = toolId;
            _toolName = toolName;
            _stopwatch = Stopwatch.StartNew();
            
            // Set up thread-local context for Tool scopes
            _previousContext = _currentContext;
            if (targetType == ProfileTargetType.Tool && service.IsEnabled)
            {
                _currentContext = new ProfilerContext(service, toolId, toolName);
            }
        }

        public void Dispose()
        {
            // Restore previous context
            _currentContext = _previousContext;
            
            _stopwatch.Stop();
            var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;

            switch (_targetType)
            {
                case ProfileTargetType.MainWindow:
                    _service.RecordMainWindowDraw(elapsedMs);
                    break;
                case ProfileTargetType.FullscreenWindow:
                    _service.RecordFullscreenWindowDraw(elapsedMs);
                    break;
                case ProfileTargetType.Tool:
                    _service.RecordToolDraw(_toolId, _toolName, elapsedMs);
                    // Log slow tool draws
                    if (_service.LogSlowOperations && elapsedMs > _service.SlowOperationThresholdMs)
                    {
                        _service._log.Debug($"[Profiler] Slow tool draw: {_toolName} took {elapsedMs:F2}ms");
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Disposable scope for timing child operations within a tool.
    /// </summary>
    public readonly struct ChildProfileScope : IDisposable
    {
        private readonly ProfileStats? _stats;
        private readonly Stopwatch? _stopwatch;
        private readonly ProfilerService? _service;
        private readonly string? _scopeName;
        private readonly string? _toolName;

        public ChildProfileScope(ProfileStats? stats, Stopwatch? stopwatch, ProfilerService? service = null, string? scopeName = null, string? toolName = null)
        {
            _stats = stats;
            _stopwatch = stopwatch;
            _service = service;
            _scopeName = scopeName;
            _toolName = toolName;
        }

        public void Dispose()
        {
            if (_stopwatch == null || _stats == null) return;
            _stopwatch.Stop();
            var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
            _stats.RecordSample(elapsedMs);
            
            // Log slow child operations
            if (_service != null && _service.LogSlowOperations && elapsedMs > _service.SlowOperationThresholdMs)
            {
                _service._log.Debug($"[Profiler] Slow operation: {_toolName}/{_scopeName} took {elapsedMs:F2}ms");
            }
        }
    }
}
