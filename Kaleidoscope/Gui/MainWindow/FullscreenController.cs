using Dalamud.Plugin.Services;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Owns the main window's fullscreen state machine: deferred enter/exit transitions,
/// mode-appropriate layout swapping, and coordination of windowed position capture/restore
/// with <see cref="WindowPositionPersister"/>.
/// </summary>
internal sealed class FullscreenController
{
    private readonly ConfigurationService _configService;
    private readonly LayoutEditingService _layoutEditingService;
    private readonly StateService _stateService;
    private readonly IPluginLog _log;
    private readonly WindowPositionPersister _positionPersister;
    private readonly Func<WindowContentContainer?> _getContainer;
    private readonly Action _updateWindowTitle;

    private bool _isFullscreenMode;
    private bool _pendingEnterFullscreen;
    private bool _pendingExitFullscreen;

    private Configuration Config => _configService.Config;
    private WindowContentContainer? Container => _getContainer();

    public FullscreenController(
        ConfigurationService configService,
        LayoutEditingService layoutEditingService,
        StateService stateService,
        IPluginLog log,
        WindowPositionPersister positionPersister,
        Func<WindowContentContainer?> getContainer,
        Action updateWindowTitle)
    {
        _configService = configService;
        _layoutEditingService = layoutEditingService;
        _stateService = stateService;
        _log = log;
        _positionPersister = positionPersister;
        _getContainer = getContainer;
        _updateWindowTitle = updateWindowTitle;
    }

    /// <summary>
    /// Gets whether the window is currently in fullscreen mode.
    /// </summary>
    public bool IsFullscreenMode => _isFullscreenMode;

    /// <summary>
    /// PreDraw hook: processes any deferred fullscreen transition. These are deferred from the
    /// unsaved-changes dialog callback which runs inside Draw(); executing them here (before Draw)
    /// avoids modifying the tool list mid-iteration.
    /// </summary>
    public void ProcessPendingTransitions()
    {
        if (_pendingEnterFullscreen)
        {
            _pendingEnterFullscreen = false;
            EnterFullscreenModeInternal();
        }
        if (_pendingExitFullscreen)
        {
            _pendingExitFullscreen = false;
            ExitFullscreenModeInternal();
        }
    }

    /// <summary>
    /// Enters fullscreen mode - window fills viewport with no decorations.
    /// Loads the active fullscreen layout.
    /// </summary>
    public void EnterFullscreenMode()
    {
        if (_isFullscreenMode) return;

        if (!_layoutEditingService.TryPerformDestructiveAction("enter fullscreen mode", () =>
        {
            // Defer to PreDraw — this callback may run inside Draw() (from the
            // unsaved-changes dialog), and EnterFullscreenModeInternal modifies
            // the tool list which would corrupt the in-progress iteration.
            _pendingEnterFullscreen = true;
        }))
        {
            // Dialog will be shown, action deferred
            return;
        }
        // Not dirty — action ran immediately from a safe call site (title bar
        // button or PreDraw), so process the flag right away.
        if (_pendingEnterFullscreen)
        {
            _pendingEnterFullscreen = false;
            EnterFullscreenModeInternal();
        }
    }

    private void EnterFullscreenModeInternal()
    {
        if (_isFullscreenMode) return;

        // Capture the current windowed placement so it can be restored on exit.
        _positionPersister.CaptureCurrentWindowedState();

        _isFullscreenMode = true;
        _stateService.IsFullscreen = true;

        try
        {
            // Load the fullscreen layout
            LoadLayoutForCurrentMode();
        }
        catch (Exception ex)
        {
            // Revert fullscreen state so the window isn't stuck half-transitioned
            _isFullscreenMode = false;
            _stateService.IsFullscreen = false;
            _log.Error($"Failed to enter fullscreen mode, reverting: {ex.Message}");
            return;
        }

        _log.Debug("Entered fullscreen mode");
    }

    /// <summary>
    /// Exits fullscreen mode - restores windowed appearance and position.
    /// Loads the active windowed layout.
    /// </summary>
    public void ExitFullscreenMode()
    {
        if (!_isFullscreenMode) return;

        if (!_layoutEditingService.TryPerformDestructiveAction("exit fullscreen mode", () =>
        {
            // Defer to PreDraw — see EnterFullscreenMode for rationale.
            _pendingExitFullscreen = true;
        }))
        {
            // Dialog will be shown, action deferred
            return;
        }
        // Not dirty — safe to process immediately.
        if (_pendingExitFullscreen)
        {
            _pendingExitFullscreen = false;
            ExitFullscreenModeInternal();
        }
    }

    private void ExitFullscreenModeInternal()
    {
        if (!_isFullscreenMode) return;

        _isFullscreenMode = false;
        _stateService.IsFullscreen = false;

        // Restore windowed position/size
        _positionPersister.RestoreWindowedStateWithViewportClamp();

        try
        {
            // Load the windowed layout
            LoadLayoutForCurrentMode();
        }
        catch (Exception ex)
        {
            // Revert fullscreen state so the window isn't stuck half-transitioned
            _isFullscreenMode = true;
            _stateService.IsFullscreen = true;
            _log.Error($"Failed to exit fullscreen mode, reverting: {ex.Message}");
            return;
        }

        _log.Debug("Exited fullscreen mode");
    }

    /// <summary>
    /// Loads the appropriate layout for the current mode (windowed or fullscreen).
    /// </summary>
    private void LoadLayoutForCurrentMode()
    {
        try
        {
            var layouts = Config.Layouts ?? new List<ContentLayoutState>();
            var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
            var activeName = _isFullscreenMode
                ? Config.ActiveFullscreenLayoutName
                : Config.ActiveWindowedLayoutName;

            var filteredLayouts = layouts.Where(x => x.Type == targetType).ToList();
            ContentLayoutState? layout = null;

            if (!string.IsNullOrWhiteSpace(activeName))
                layout = filteredLayouts.Find(x => x.Name == activeName);
            layout ??= filteredLayouts.FirstOrDefault();

            var container = Container;
            if (layout != null && container != null)
            {
                container.SetGridSettingsFromLayout(layout);
                if (layout.Tools is { Count: > 0 })
                    container.ApplyLayout(layout.Tools);
                else
                    container.ClearAllTools();

                // When switching to a windowed layout, restore its saved window position/size
                if (!_isFullscreenMode && layout.WindowedPos.HasValue && layout.WindowedSize.HasValue)
                {
                    _positionPersister.ApplyLayoutWindowedPosition(layout.WindowedPos.Value, layout.WindowedSize.Value);
                }

                // Update the active layout name if we fell back to a different one
                if (_isFullscreenMode && Config.ActiveFullscreenLayoutName != layout.Name)
                {
                    Config.ActiveFullscreenLayoutName = layout.Name;
                    _configService.MarkDirty();
                }
                else if (!_isFullscreenMode && Config.ActiveWindowedLayoutName != layout.Name)
                {
                    Config.ActiveWindowedLayoutName = layout.Name;
                    _configService.MarkDirty();
                }

                _layoutEditingService.InitializeFromPersisted(
                    layout.Name,
                    targetType,
                    layout.Tools,
                    container.GridSettings);

                _log.Information($"Loaded {targetType} layout '{layout.Name}' ({layout.Tools?.Count ?? 0} tools)");
            }
            else
            {
                // No layout exists for this mode — clear any leftover tools and initialize with defaults
                container?.ClearAllTools();
                _layoutEditingService.InitializeFromPersisted(
                    "Default",
                    targetType,
                    new List<ToolLayoutState>(),
                    container?.GridSettings);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load layout for current mode: {ex.Message}");

            // Fall back to empty defaults so the window remains usable
            try
            {
                Container?.ClearAllTools();
            }
            catch { /* best effort */ }

            var fallbackType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
            _layoutEditingService.InitializeFromPersisted(
                "Default",
                fallbackType,
                new List<ToolLayoutState>(),
                Container?.GridSettings);
        }

        _updateWindowTitle();
    }

    /// <summary>
    /// Applies a layout by name.
    /// </summary>
    public void ApplyLayoutByName(string name)
    {
        var targetType = _isFullscreenMode ? LayoutType.Fullscreen : LayoutType.Windowed;
        var layout = Config.Layouts?.Find(x => x.Name == name && x.Type == targetType)
                  ?? Config.Layouts?.Where(x => x.Type == targetType).FirstOrDefault();
        var container = Container;
        if (layout != null && container != null)
        {
            container.SetGridSettingsFromLayout(layout);
            if (layout.Tools is { Count: > 0 })
                container.ApplyLayout(layout.Tools);
            else
                container.ClearAllTools();
        }
    }
}
