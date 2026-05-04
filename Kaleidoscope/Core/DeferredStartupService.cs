using System.Diagnostics;
using System.Reflection;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Kaleidoscope.Gui.Widgets.Common;
using OtterGui.Services;

namespace Kaleidoscope.Core;

/// <summary>
/// Resolves services on a background thread and uses Framework.Update
/// to poll progress and update the notification with flavour messages.
/// Disposed by the plugin once startup completes or fails, or on teardown.
/// </summary>
internal sealed class DeferredStartupService : IDisposable
{
    /// <summary> Project-specific messages that augment the shared FlavourText pool. </summary>
    private static readonly string[] ProjectMessages =
    [
        "Consulting mtvirux.app",
        "Pinging @mtvirux on Discord",
        "Loading the acclaimed MMORPG",
        "Thanking Karashiro",
        "Bribing Yoshi-P",
    ];

    private readonly IFramework _framework;
    private readonly INotificationManager _notificationManager;
    private readonly IActiveNotification _notification;
    private readonly int _totalServices;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly CancellationTokenSource _cts = new();

    private string _currentMessage;
    private double _nextMessageTime;

    // Shared state between background thread and framework thread.
    private int _resolvedCount;
    private volatile bool _completed;
    private volatile bool _failed;
    private volatile string? _errorMessage;
    private volatile bool _disposed;

    public DeferredStartupService(ServiceManager services, Assembly pluginAssembly)
    {
        _framework = services.GetService<IFramework>();
        _notificationManager = services.GetService<INotificationManager>();

        // Collect IService types once for both the progress denominator and the resolve loop.
        var serviceTypes = pluginAssembly.ExportedTypes
            .Where(t => t is { IsInterface: false, IsAbstract: false }
                     && typeof(IService).IsAssignableFrom(t))
            .ToArray();
        _totalServices = serviceTypes.Length;

        _currentMessage = FlavourText.GetRandom(Random.Shared, ProjectMessages);
        _nextMessageTime = 0.5;

        _notification = _notificationManager.AddNotification(new Notification
        {
            Content         = _currentMessage,
            Title           = "Starting up Kaleidoscope...",
            Type            = NotificationType.Info,
            InitialDuration = TimeSpan.MaxValue,
            Progress        = 0f,
            UserDismissable = false,
            Minimized       = false,
        });

        var provider = services.Provider!;
        var token = _cts.Token;

        Task.Run(() =>
        {
            try
            {
                foreach (var type in serviceTypes)
                {
                    if (token.IsCancellationRequested) return;
                    provider.GetRequiredService(type);
                    Interlocked.Increment(ref _resolvedCount);
                }

                _completed = true;
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _errorMessage = ex.InnerException?.Message ?? ex.Message;
                _failed = true;
                KaleidoscopePlugin.Log.Error($"Failed during deferred startup: {ex}");
            }
        }, token);

        _framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _framework.Update -= OnFrameworkUpdate;
        _cts.Cancel();
        _cts.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_disposed)
            return;

        if (_failed)
        {
            _notification.DismissNow();
            _notificationManager.AddNotification(new Notification
            {
                Content         = $"Failed to load: {_errorMessage}",
                Title           = "Kaleidoscope",
                Type            = NotificationType.Error,
                InitialDuration = TimeSpan.FromSeconds(10),
                Progress        = 1f,
            });
            Dispose();
            return;
        }

        if (_completed)
        {
            _notification.DismissNow();
            _notificationManager.AddNotification(new Notification
            {
                Content         = $"Loaded successfully in {_elapsed.Elapsed.TotalSeconds:F1}s.",
                Title           = "Kaleidoscope",
                Type            = NotificationType.Success,
                InitialDuration = TimeSpan.FromSeconds(5),
                Progress        = 1f,
            });
            KaleidoscopePlugin.Log.Information($"Kaleidoscope loaded successfully in {_elapsed.Elapsed.TotalSeconds:F1}s.");
            Dispose();
            return;
        }

        // Rotate flavour message every ~1 seconds.
        var seconds = _elapsed.Elapsed.TotalSeconds;
        if (seconds >= _nextMessageTime)
        {
            _currentMessage = FlavourText.GetRandom(Random.Shared, ProjectMessages);
            _nextMessageTime = seconds + 1;
        }

        var resolved = Volatile.Read(ref _resolvedCount);
        _notification.Content  = $"{_currentMessage}... ({seconds:F0}s)";
        _notification.Progress = Math.Min((float)resolved / _totalServices, 0.99f);
    }
}
