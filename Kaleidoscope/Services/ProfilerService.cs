using System.Diagnostics;
using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Tracks draw times for tools, windows, and other profiling metrics.
/// </summary>
public sealed class ProfilerService : IService, IDisposable
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly object _lock = new();

    /// <summary>
    /// Statistics for a single profiled target.
    /// </summary>
    public class ProfileStats
    {
        public string Name { get; set; } = string.Empty;
        public double LastDrawTimeMs { get; set; }
        public double MinDrawTimeMs { get; set; } = double.MaxValue;
        public double MaxDrawTimeMs { get; set; } = double.MinValue;
        public double TotalDrawTimeMs { get; set; }
        public long SampleCount { get; set; }
        public double AverageDrawTimeMs => SampleCount > 0 ? TotalDrawTimeMs / SampleCount : 0;

        public void Reset()
        {
            LastDrawTimeMs = 0;
            MinDrawTimeMs = double.MaxValue;
            MaxDrawTimeMs = double.MinValue;
            TotalDrawTimeMs = 0;
            SampleCount = 0;
        }

        public void RecordSample(double drawTimeMs)
        {
            LastDrawTimeMs = drawTimeMs;
            if (drawTimeMs < MinDrawTimeMs) MinDrawTimeMs = drawTimeMs;
            if (drawTimeMs > MaxDrawTimeMs) MaxDrawTimeMs = drawTimeMs;
            TotalDrawTimeMs += drawTimeMs;
            SampleCount++;
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
            _configService.Save();
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
    /// Disposable scope for timing draw operations.
    /// </summary>
    public readonly struct ProfileScope : IDisposable
    {
        private readonly ProfilerService _service;
        private readonly ProfileTargetType _targetType;
        private readonly string _toolId;
        private readonly string _toolName;
        private readonly Stopwatch _stopwatch;

        public ProfileScope(ProfilerService service, ProfileTargetType targetType, string toolId, string toolName)
        {
            _service = service;
            _targetType = targetType;
            _toolId = toolId;
            _toolName = toolName;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
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
                    break;
            }
        }
    }
}
