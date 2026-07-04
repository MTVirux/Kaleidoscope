using System;
using System.Diagnostics;
using System.Threading;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using Kaleidoscope.Services.Common;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service that provides framerate limiting functionality.
/// Integrates with ChillFrames IPC to disable its limiter when Kaleidoscope's is active.
/// </summary>
/// <remarks>
/// Uses the game's framework update system to correctly limit framerate.
/// When enabled, disables ChillFrames via IPC to avoid conflicts.
/// Handles the case where ChillFrames may not be installed.
/// </remarks>
public sealed class FrameLimiterService : IDisposable, IService
{
    private const string PluginName = "Kaleidoscope";
    private const string ChillFramesDisableLimiter = "ChillFrames.DisableLimiter";
    private const string ChillFramesEnableLimiter = "ChillFrames.EnableLimiter";
    
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly IDalamudPluginInterface _pluginInterface;
    
    private readonly Stopwatch _frameTimer = Stopwatch.StartNew();
    
    // ChillFrames IPC subscribers
    private ICallGateSubscriber<string, bool>? _chillFramesDisable;
    private ICallGateSubscriber<string, bool>? _chillFramesEnable;
    
    private bool _isEnabled;
    private bool _chillFramesDisabled;
    private int _targetFramerate = 60;
    
    /// <summary>
    /// Gets or sets whether the frame limiter is enabled.
    /// When toggled, automatically manages ChillFrames IPC.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            
            _isEnabled = value;
            
            if (_isEnabled)
            {
                DisableChillFrames();
            }
            else
            {
                EnableChillFrames();
            }
            
            _configService.Config.FrameLimiterEnabled = _isEnabled;
            _configService.MarkDirty();
        }
    }
    
    /// <summary>
    /// Gets or sets the target framerate in frames per second.
    /// Minimum is 10 FPS to prevent excessively slow frame times.
    /// </summary>
    public int TargetFramerate
    {
        get => _targetFramerate;
        set
        {
            _targetFramerate = Math.Clamp(value, 10, 1000);
            _configService.Config.FrameLimiterTargetFps = _targetFramerate;
            _configService.MarkDirty();
        }
    }
    
    /// <summary>
    /// Gets the target frame time in milliseconds.
    /// </summary>
    private int TargetFrametimeMs => 1000 / _targetFramerate;
    
    /// <summary>
    /// Gets the precise target frame time in ticks (10000 ticks per ms).
    /// </summary>
    private long PreciseFrametimeTicks => (long)(1000.0 / _targetFramerate * TimeSpan.TicksPerMillisecond);
    
    /// <summary>
    /// Gets the last measured frame time.
    /// </summary>
    public TimeSpan LastFrametime { get; private set; }
    
    /// <summary>
    /// Gets the current frames per second.
    /// </summary>
    public double CurrentFps => LastFrametime.TotalMilliseconds > 0 
        ? 1000.0 / LastFrametime.TotalMilliseconds 
        : 0;
    
    /// <summary>
    /// Gets whether ChillFrames IPC is available.
    /// </summary>
    public bool IsChillFramesAvailable { get; private set; }
    
    /// <summary>
    /// Creates the frame limiter service.
    /// </summary>
    public FrameLimiterService(
        IFramework framework,
        IPluginLog log,
        ConfigurationService configService,
        IDalamudPluginInterface pluginInterface)
    {
        _framework = framework;
        _log = log;
        _configService = configService;
        _pluginInterface = pluginInterface;
        
        _isEnabled = _configService.Config.FrameLimiterEnabled;
        _targetFramerate = Math.Clamp(_configService.Config.FrameLimiterTargetFps, 10, 1000);
        
        InitializeChillFramesIpc();

        // Deferred startup constructs this on a background thread; subscribe to the framework
        // update on the framework thread so the handler is hooked in the update dispatcher's
        // own context rather than off-thread.
        _framework.RunOnFrameworkThread(() => { _framework.Update += OnFrameworkUpdate; }).GetAwaiter().GetResult();

        if (_isEnabled)
        {
            DisableChillFrames();
        }
        
        LogService.Info(LogCategory.UI, $"FrameLimiterService initialized. Enabled: {_isEnabled}, Target: {_targetFramerate} FPS, ChillFrames available: {IsChillFramesAvailable}");
    }
    
    /// <summary>
    /// Initializes the ChillFrames IPC subscribers.
    /// </summary>
    private void InitializeChillFramesIpc()
    {
        try
        {
            _chillFramesDisable = _pluginInterface.GetIpcSubscriber<string, bool>(ChillFramesDisableLimiter);
            _chillFramesEnable = _pluginInterface.GetIpcSubscriber<string, bool>(ChillFramesEnableLimiter);
            
            // Test if ChillFrames is available by attempting a benign operation
            // We don't actually call it here, just set up the subscribers
            IsChillFramesAvailable = true;
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.UI, $"ChillFrames IPC not available: {ex.Message}");
            IsChillFramesAvailable = false;
        }
    }
    
    /// <summary>
    /// Disables ChillFrames limiter via IPC.
    /// </summary>
    private void DisableChillFrames()
    {
        if (_chillFramesDisabled) return;

        var result = IpcInvoker.Invoke<bool?>(
            _chillFramesDisable != null,
            () => _chillFramesDisable!.InvokeFunc(PluginName),
            null,
            ex =>
            {
                IsChillFramesAvailable = false;
                if (ex is not IpcNotReadyError)
                {
                    LogService.Debug(LogCategory.UI, $"Failed to disable ChillFrames: {ex.Message}");
                }
            });

        if (result == true)
        {
            _chillFramesDisabled = true;
            LogService.Debug(LogCategory.UI, "ChillFrames limiter disabled via IPC");
        }
    }

    /// <summary>
    /// Re-enables ChillFrames limiter via IPC.
    /// </summary>
    private void EnableChillFrames()
    {
        if (!_chillFramesDisabled) return;

        var result = IpcInvoker.Invoke<bool?>(
            _chillFramesEnable != null,
            () => _chillFramesEnable!.InvokeFunc(PluginName),
            null,
            ex =>
            {
                IsChillFramesAvailable = false;
                _chillFramesDisabled = false;
                if (ex is not IpcNotReadyError)
                {
                    LogService.Debug(LogCategory.UI, $"Failed to enable ChillFrames: {ex.Message}");
                }
            });

        if (result == true)
        {
            _chillFramesDisabled = false;
            LogService.Debug(LogCategory.UI, "ChillFrames limiter re-enabled via IPC");
        }
    }
    
    /// <summary>
    /// Framework update handler that performs frame limiting.
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_isEnabled)
        {
            PerformFrameLimiting();
        }
        
        LastFrametime = _frameTimer.Elapsed;
        _frameTimer.Restart();
    }
    
    /// <summary>
    /// Performs the actual frame limiting using sleep and cooperative spin-wait.
    /// Uses SpinWait to yield CPU time to the OS scheduler instead of burning a full core.
    /// </summary>
    private void PerformFrameLimiting()
    {
        var delayMs = (int)(TargetFrametimeMs - _frameTimer.ElapsedMilliseconds);
        
        // Sleep for most of the delay (minus 1ms for spin-wait precision)
        if (delayMs - 1 > 0)
        {
            Thread.Sleep(delayMs - 1);
        }
        
        // Cooperative spin-wait for precise timing — yields to OS scheduler 
        // instead of burning a full CPU core with an empty busy loop
        var sw = new SpinWait();
        while (_frameTimer.ElapsedTicks < PreciseFrametimeTicks)
        {
            sw.SpinOnce();
        }
    }
    
    /// <summary>
    /// Disposes the service and cleans up IPC.
    /// </summary>
    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        
        // Re-enable ChillFrames if we disabled it
        if (_chillFramesDisabled)
        {
            EnableChillFrames();
        }
        
        LogService.Debug(LogCategory.UI, "FrameLimiterService disposed");
    }
}
