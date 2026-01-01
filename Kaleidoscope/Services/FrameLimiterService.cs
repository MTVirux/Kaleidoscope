using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
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
        _framework.Update += OnFrameworkUpdate;
        
        if (_isEnabled)
        {
            DisableChillFrames();
        }
        
        _log.Information($"FrameLimiterService initialized. Enabled: {_isEnabled}, Target: {_targetFramerate} FPS, ChillFrames available: {IsChillFramesAvailable}");
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
            _log.Debug($"ChillFrames IPC not available: {ex.Message}");
            IsChillFramesAvailable = false;
        }
    }
    
    /// <summary>
    /// Disables ChillFrames limiter via IPC.
    /// </summary>
    private void DisableChillFrames()
    {
        if (_chillFramesDisabled) return;
        
        try
        {
            var result = _chillFramesDisable?.InvokeFunc(PluginName);
            if (result == true)
            {
                _chillFramesDisabled = true;
                _log.Debug("ChillFrames limiter disabled via IPC");
            }
        }
        catch (IpcNotReadyError)
        {
            // ChillFrames not loaded, this is fine
            IsChillFramesAvailable = false;
        }
        catch (Exception ex)
        {
            _log.Debug($"Failed to disable ChillFrames: {ex.Message}");
            IsChillFramesAvailable = false;
        }
    }
    
    /// <summary>
    /// Re-enables ChillFrames limiter via IPC.
    /// </summary>
    private void EnableChillFrames()
    {
        if (!_chillFramesDisabled) return;
        
        try
        {
            var result = _chillFramesEnable?.InvokeFunc(PluginName);
            if (result == true)
            {
                _chillFramesDisabled = false;
                _log.Debug("ChillFrames limiter re-enabled via IPC");
            }
        }
        catch (IpcNotReadyError)
        {
            // ChillFrames not loaded, this is fine
            IsChillFramesAvailable = false;
            _chillFramesDisabled = false;
        }
        catch (Exception ex)
        {
            _log.Debug($"Failed to enable ChillFrames: {ex.Message}");
            IsChillFramesAvailable = false;
            _chillFramesDisabled = false;
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
    /// Performs the actual frame limiting using sleep and spin-wait.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private void PerformFrameLimiting()
    {
        var delayMs = (int)(TargetFrametimeMs - _frameTimer.ElapsedMilliseconds);
        
        // Sleep for most of the delay (minus 1ms for spin-wait precision)
        if (delayMs - 1 > 0)
        {
            Thread.Sleep(delayMs - 1);
        }
        
        // Spin-wait for precise timing
        while (_frameTimer.ElapsedTicks < PreciseFrametimeTicks)
        {
            // Empty loop for precise timing
            // Using a delegate prevents the JIT from optimizing this away
            ((Action)(() => { }))();
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
        
        _log.Debug("FrameLimiterService disposed");
    }
}
