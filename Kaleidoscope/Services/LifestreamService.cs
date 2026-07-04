using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Kaleidoscope.Services.Common;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// IPC service for communicating with the Lifestream plugin.
/// Provides character switching (relog) capabilities via IPC.
/// </summary>
/// <remarks>
/// Registered as a singleton service to avoid creating multiple IPC subscriptions.
/// Automatically initializes on first access. Retries connection every 5 seconds if unavailable.
/// <br/>
/// Lifestream uses EzIPC with the prefix "Lifestream." for all IPC names.
/// Source: https://github.com/NightmareXIV/Lifestream/blob/main/Lifestream/IPC/IPCProvider.cs
/// </remarks>
public sealed class LifestreamService : IDisposable, IService
{
    private readonly IDalamudPluginInterface _pluginInterface;

    // "Lifestream.IsBusy" doubles as the availability probe during (re)connection.
    private ICallGateSubscriber<bool>? _isBusy;
    private ICallGateSubscriber<string, string, int>? _changeCharacter;

    private bool _initialized = false;
    private Timer? _retryTimer;
    private const int RetryIntervalMs = 5000;

    /// <summary>
    /// Whether the Lifestream plugin is currently available via IPC.
    /// </summary>
    public bool IsAvailable { get; private set; } = false;

    public LifestreamService(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;

        try
        {
            _isBusy = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
            _changeCharacter = _pluginInterface.GetIpcSubscriber<string, string, int>("Lifestream.ChangeCharacter");

            // Test availability
            try
            {
                _isBusy.InvokeFunc();
                IsAvailable = true;
                StopRetryTimer();
                LogService.Debug(LogCategory.Lifestream, "Lifestream IPC connected");
            }
            catch (Exception)
            {
                IsAvailable = false;
                StartRetryTimer();
                LogService.Debug(LogCategory.Lifestream, "Lifestream not available, will retry");
            }

            _initialized = true;
        }
        catch (Exception)
        {
            IsAvailable = false;
            StartRetryTimer();
        }
    }

    private void StartRetryTimer()
    {
        if (_retryTimer != null) return;
        _retryTimer = new Timer(_ => TryReconnect(), null, RetryIntervalMs, RetryIntervalMs);
    }

    private void StopRetryTimer()
    {
        if (_retryTimer == null) return;
        _retryTimer.Dispose();
        _retryTimer = null;
    }

    private void TryReconnect()
    {
        if (IsAvailable)
        {
            StopRetryTimer();
            return;
        }

        try
        {
            _isBusy = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
            _isBusy.InvokeFunc();
            IsAvailable = true;
            StopRetryTimer();
            LogService.Debug(LogCategory.Lifestream, "Lifestream IPC reconnected");

            // Re-initialize all subscribers
            _initialized = false;
            Initialize();
        }
        catch (Exception)
        {
            // Lifestream IPC still not available, timer will retry
        }
    }

    /// <summary>
    /// Requests Lifestream to change to a different character.
    /// </summary>
    /// <param name="name">Character first and last name.</param>
    /// <param name="world">Character's home world name.</param>
    /// <returns>Lifestream ErrorCode as int (0 = Success).</returns>
    public int ChangeCharacter(string name, string world)
        => IpcInvoker.Invoke(
            IsAvailable && _changeCharacter != null,
            () =>
            {
                var result = _changeCharacter!.InvokeFunc(name, world);
                LogService.Debug(LogCategory.Lifestream, $"ChangeCharacter({name}, {world}) = {result}");
                return result;
            },
            -1,
            _ => IsAvailable = false);

    public void Dispose()
    {
        StopRetryTimer();
    }
}
