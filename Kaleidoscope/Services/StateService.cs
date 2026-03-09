using Dalamud.Plugin.Services;
using Kaleidoscope.Interfaces;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Tracks UI mode states: fullscreen, edit, locked, and drag states.
/// </summary>
public sealed class StateService : IStateService, IService
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;

    private bool _isFullscreen;
    private bool _isEditMode;
    private bool _isLocked;
    private bool _isDragging;
    private bool _isResizing;
    private bool _isMainWindowMoving;
    private bool _isMainWindowResizing;

    /// <summary>
    /// Indicates whether the plugin was compiled in Debug configuration.
    /// </summary>
    public static bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    public StateService(IPluginLog log, ConfigurationService configService)
    {
        _log = log;
        _configService = configService;

        _isEditMode = configService.Config.EditMode;
        _isLocked = configService.Config.PinMainWindow;
        _isFullscreen = configService.Config.ExclusiveFullscreen;

        LogService.Debug(LogCategory.UI, "StateService initialized");
    }

    private Configuration Config => _configService.Config;

    /// <inheritdoc />
    public bool IsFullscreen
    {
        get => _isFullscreen;
        set
        {
            if (_isFullscreen == value) return;
            _isFullscreen = value;
            LogService.Debug(LogCategory.UI, $"IsFullscreen changed to {value}");
            OnFullscreenChanged?.Invoke(value);
        }
    }

    /// <inheritdoc />
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (_isEditMode == value) return;
            _isEditMode = value;
            Config.EditMode = value;
            _configService.MarkDirty();
            LogService.Debug(LogCategory.UI, $"IsEditMode changed to {value}");
            OnEditModeChanged?.Invoke(value);
        }
    }

    /// <inheritdoc />
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value) return;
            _isLocked = value;
            Config.PinMainWindow = value;
            _configService.MarkDirty();
            LogService.Debug(LogCategory.UI, $"IsLocked changed to {value}");
            OnLockedChanged?.Invoke(value);
        }
    }

    /// <inheritdoc />
    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging == value) return;
            _isDragging = value;
            LogService.Verbose(LogCategory.UI, $"IsDragging changed to {value}");
            OnDraggingChanged?.Invoke(value);
        }
    }

    /// <inheritdoc />
    public bool IsResizing
    {
        get => _isResizing;
        set
        {
            if (_isResizing == value) return;
            _isResizing = value;
            LogService.Verbose(LogCategory.UI, $"IsResizing changed to {value}");
            OnResizingChanged?.Invoke(value);
        }
    }

    /// <inheritdoc />
    public bool IsMainWindowMoving
    {
        get => _isMainWindowMoving;
        set
        {
            if (_isMainWindowMoving == value) return;
            _isMainWindowMoving = value;
            LogService.Verbose(LogCategory.UI, $"IsMainWindowMoving changed to {value}");
        }
    }

    /// <inheritdoc />
    public bool IsMainWindowResizing
    {
        get => _isMainWindowResizing;
        set
        {
            if (_isMainWindowResizing == value) return;
            _isMainWindowResizing = value;
            LogService.Verbose(LogCategory.UI, $"IsMainWindowResizing changed to {value}");
        }
    }

    /// <inheritdoc />
    public bool IsMainWindowInteracting => _isMainWindowMoving || _isMainWindowResizing;

    /// <inheritdoc />
    public bool IsInteracting => _isDragging || _isResizing;

    /// <inheritdoc />
    public bool CanEditLayout => _isEditMode;

    /// <inheritdoc />
    public event Action<bool>? OnFullscreenChanged;

    /// <inheritdoc />
    public event Action<bool>? OnEditModeChanged;

    /// <inheritdoc />
    public event Action<bool>? OnLockedChanged;

    /// <inheritdoc />
    public event Action<bool>? OnDraggingChanged;

    /// <inheritdoc />
    public event Action<bool>? OnResizingChanged;

    /// <inheritdoc />
    public void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
    }

    /// <inheritdoc />
    public void ToggleLocked()
    {
        IsLocked = !IsLocked;
    }

    /// <inheritdoc />
    public void EnterFullscreen()
    {
        if (_isFullscreen) return;
        
        // Exit edit mode when entering fullscreen to prevent accidental edits
        if (_isEditMode)
        {
            IsEditMode = false;
            LogService.Debug(LogCategory.UI, "Exited edit mode due to fullscreen entry");
        }
        
        IsFullscreen = true;
    }

    /// <inheritdoc />
    public void ExitFullscreen()
    {
        if (!_isFullscreen) return;
        IsFullscreen = false;
    }

}
