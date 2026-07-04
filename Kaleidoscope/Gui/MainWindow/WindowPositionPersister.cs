using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Owns windowed position/size persistence for the main window: first-frame restore,
/// post-fullscreen restore, lock/drag/resize constraint positioning, frame-to-frame
/// interaction tracking, and throttled saves to config and the active windowed layout.
/// </summary>
internal sealed class WindowPositionPersister
{
    private readonly ConfigurationService _configService;
    private readonly StateService _stateService;
    private readonly IPluginLog _log;

    private Vector2 _savedPos = ConfigStatic.DefaultWindowPosition;
    private Vector2 _savedSize = ConfigStatic.DefaultWindowSize;

    private Vector2 _lastSavedPos = ConfigStatic.DefaultWindowPosition;
    private Vector2 _lastSavedSize = ConfigStatic.DefaultWindowSize;
    private DateTime _lastSaveTime = DateTime.MinValue;
    private const int SaveThrottleMs = 500;

    private Vector2 _prevFramePos = Vector2.Zero;
    private Vector2 _prevFrameSize = Vector2.Zero;
    private bool _prevFrameInitialized;

    private bool _firstPreDraw = true;
    private bool _pendingWindowRestore;

    private Configuration Config => _configService.Config;

    public WindowPositionPersister(ConfigurationService configService, StateService stateService, IPluginLog log)
    {
        _configService = configService;
        _stateService = stateService;
        _log = log;

        // Initialize last-saved pos/size from config so change detection starts correct
        _lastSavedPos = Config.MainWindowPos;
        _lastSavedSize = Config.MainWindowSize;
    }

    /// <summary>
    /// Captures the current windowed position/size (falling back to config) and persists it to
    /// config and the active windowed layout. Called when entering fullscreen so the windowed
    /// placement can be restored on exit.
    /// </summary>
    public void CaptureCurrentWindowedState()
    {
        // Save current windowed position/size for restoration later
        try
        {
            _savedPos = ImGui.GetWindowPos();
            _savedSize = ImGui.GetWindowSize();
        }
        catch
        {
            _savedPos = Config.MainWindowPos;
            _savedSize = Config.MainWindowSize;
        }

        // Persist windowed pos/size to config so it survives plugin restarts
        Config.MainWindowPos = _savedPos;
        Config.MainWindowSize = _savedSize;
        _lastSavedPos = _savedPos;
        _lastSavedSize = _savedSize;

        // Also persist to the active windowed layout so the position is layout-specific
        PersistWindowedPosToActiveLayout(_savedPos, _savedSize);

        _configService.MarkDirty();
    }

    /// <summary>
    /// Restores the saved windowed position/size after exiting fullscreen mode.
    /// Validates the restored position is within the current viewport to handle
    /// monitor changes that occurred while in fullscreen, then schedules it for the next PreDraw.
    /// </summary>
    public void RestoreWindowedStateWithViewportClamp()
    {
        var restoredPos = _savedPos;
        var restoredSize = _savedSize;

        // Validate the restored position is within the current viewport.
        // Monitors may have been disconnected or resolution may have changed
        // while in fullscreen, leaving the saved position off-screen.
        try
        {
            var viewport = ImGui.GetMainViewport();
            var vpMin = viewport.Pos;
            var vpMax = new Vector2(viewport.Pos.X + viewport.Size.X, viewport.Pos.Y + viewport.Size.Y);

            // Clamp size to not exceed viewport
            restoredSize = new Vector2(
                MathF.Min(restoredSize.X, viewport.Size.X),
                MathF.Min(restoredSize.Y, viewport.Size.Y));

            // Ensure at least part of the window is visible (top-left corner within viewport)
            if (restoredPos.X + restoredSize.X < vpMin.X + 50 || restoredPos.X > vpMax.X - 50 ||
                restoredPos.Y + restoredSize.Y < vpMin.Y + 50 || restoredPos.Y > vpMax.Y - 50)
            {
                // Window would be mostly off-screen — reset to center
                restoredPos = new Vector2(
                    vpMin.X + (viewport.Size.X - restoredSize.X) * 0.5f,
                    vpMin.Y + (viewport.Size.Y - restoredSize.Y) * 0.5f);
                _log.Debug("ExitFullscreen: saved position was off-screen, centering window");
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"ExitFullscreen: viewport bounds check failed: {ex.Message}");
        }

        // Store the validated position/size for the next PreDraw to apply
        // (SetNextWindowPos/Size can't be called here — Draw() runs after the window's Begin)
        _savedPos = restoredPos;
        _savedSize = restoredSize;
        _pendingWindowRestore = true;

        // Always persist the restored windowed position/size to config
        Config.MainWindowPos = restoredPos;
        Config.MainWindowSize = restoredSize;
        _lastSavedPos = restoredPos;
        _lastSavedSize = restoredSize;
        _configService.MarkDirty();
    }

    /// <summary>
    /// Applies a windowed layout's saved position/size, scheduling it for restore on the next PreDraw.
    /// </summary>
    public void ApplyLayoutWindowedPosition(Vector2 pos, Vector2 size)
    {
        _savedPos = pos;
        _savedSize = size;
        Config.MainWindowPos = pos;
        Config.MainWindowSize = size;
        _lastSavedPos = pos;
        _lastSavedSize = size;

        // Schedule the position restore for the next PreDraw
        _pendingWindowRestore = true;
    }

    /// <summary>
    /// PreDraw hook: applies the first-frame and post-fullscreen position restores.
    /// </summary>
    public void ApplyPendingPositioning()
    {
        // On first PreDraw, apply the saved position/size from config so the window
        // opens where it was last closed, regardless of lock state.
        if (_firstPreDraw)
        {
            _firstPreDraw = false;
            ImGui.SetNextWindowPos(Config.MainWindowPos);
            ImGui.SetNextWindowSize(Config.MainWindowSize);
            // Also sync the tracking variables so we don't detect a spurious change
            _savedPos = Config.MainWindowPos;
            _savedSize = Config.MainWindowSize;
            _lastSavedPos = Config.MainWindowPos;
            _lastSavedSize = Config.MainWindowSize;
        }
        // After exiting fullscreen, restore the saved windowed position/size
        else if (_pendingWindowRestore)
        {
            _pendingWindowRestore = false;
            ImGui.SetNextWindowPos(_savedPos);
            ImGui.SetNextWindowSize(_savedSize);
        }
    }

    /// <summary>
    /// PreDraw hook: applies move/resize constraint flags and forced positioning based on the
    /// lock state and whether a contained tool is currently being dragged or resized.
    /// </summary>
    public void ApplyConstraintPositioning(Window window)
    {
        // Prevent the main window from being moved/resized when locked or when
        // a contained tool is currently being dragged or resized.
        // When tools are being dragged/resized, we lock the window position but use
        // the CURRENT position (not Config) to avoid snapping the window.
        if (_stateService.IsLocked)
        {
            // Window is locked: force position from config
            window.Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            ImGui.SetNextWindowPos(Config.MainWindowPos);
            ImGui.SetNextWindowSize(Config.MainWindowSize);
        }
        else if (_stateService.IsDragging || _stateService.IsResizing)
        {
            // Tool is being dragged/resized: prevent window movement but keep current position
            // Use _prevFramePos/_prevFrameSize which track the current window state
            window.Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            if (_prevFrameInitialized)
            {
                ImGui.SetNextWindowPos(_prevFramePos);
                ImGui.SetNextWindowSize(_prevFrameSize);
            }
            else
            {
                // Fallback to config values if we haven't tracked position yet
                ImGui.SetNextWindowPos(Config.MainWindowPos);
                ImGui.SetNextWindowSize(Config.MainWindowSize);
            }
        }
        else
        {
            // Normal mode: allow movement and resize
            window.Flags &= ~ImGuiWindowFlags.NoMove;
            window.Flags &= ~ImGuiWindowFlags.NoResize;
        }
    }

    /// <summary>
    /// Draw hook: detects frame-to-frame move/resize of the main window and latches the
    /// interaction state on the state service.
    /// </summary>
    public void TrackWindowInteraction()
    {
        // Detect if main window is being moved or resized by comparing frame-to-frame position/size
        // But only track position changes when the window is not locked and not being constrained
        try
        {
            var curPos = ImGui.GetWindowPos();
            var curSize = ImGui.GetWindowSize();
            var io = ImGui.GetIO();
            const float eps = 0.5f;

            // Only detect movement/resizing when in free mode (not locked, not edit mode)
            // Title bar button clicks should not trigger window movement detection
            var isConstrained = _stateService.IsLocked || _stateService.IsDragging || _stateService.IsResizing;

            if (_prevFrameInitialized && !isConstrained)
            {
                var posChanging = Math.Abs(curPos.X - _prevFramePos.X) > eps || Math.Abs(curPos.Y - _prevFramePos.Y) > eps;
                var sizeChanging = Math.Abs(curSize.X - _prevFrameSize.X) > eps || Math.Abs(curSize.Y - _prevFrameSize.Y) > eps;

                if (io.MouseDown[0])
                {
                    // Once we detect moving/resizing started, keep the state true until mouse is released
                    // (latch the state on, only clear when mouse is released)
                    if (posChanging)
                        _stateService.IsMainWindowMoving = true;
                    if (sizeChanging)
                        _stateService.IsMainWindowResizing = true;
                }
                else
                {
                    // Mouse released, clear main window interaction state
                    _stateService.IsMainWindowMoving = false;
                    _stateService.IsMainWindowResizing = false;
                }
            }
            else if (!io.MouseDown[0])
            {
                // Always clear interaction state when mouse is released
                _stateService.IsMainWindowMoving = false;
                _stateService.IsMainWindowResizing = false;
            }

            // Always track the current position/size
            _prevFramePos = curPos;
            _prevFrameSize = curSize;
            _prevFrameInitialized = true;
        }
        catch (Exception ex) { _log.Debug($"[MainWindow] Window interaction detection failed: {ex.Message}"); }
    }

    /// <summary>
    /// Draw hook: persists the main window position/size to config (throttled) when it changes.
    /// </summary>
    public void PersistWindowPositionIfChanged()
    {
        // A pending windowed restore means the live ImGui geometry does not yet reflect the
        // intended windowed placement. On the frame a fullscreen exit is processed, the window
        // was still drawn at viewport size, so persisting here would write fullscreen geometry
        // into config and the active layout. Skip until ApplyPendingPositioning has restored it.
        if (_pendingWindowRestore) return;

        try
        {
            var curPos = ImGui.GetWindowPos();
            var curSize = ImGui.GetWindowSize();
            const float eps = 0.5f;
            var posChanged = Math.Abs(curPos.X - _lastSavedPos.X) > eps || Math.Abs(curPos.Y - _lastSavedPos.Y) > eps;
            var sizeChanged = Math.Abs(curSize.X - _lastSavedSize.X) > eps || Math.Abs(curSize.Y - _lastSavedSize.Y) > eps;
            if ((posChanged || sizeChanged) && !_stateService.IsLocked && !_stateService.IsDragging && !_stateService.IsResizing)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastSaveTime).TotalMilliseconds > SaveThrottleMs)
                {
                    Config.MainWindowPos = curPos;
                    Config.MainWindowSize = curSize;

                    // Also persist to the active windowed layout for per-layout position memory
                    PersistWindowedPosToActiveLayout(curPos, curSize);

                    _configService.MarkDirty();
                    _lastSavedPos = curPos;
                    _lastSavedSize = curSize;
                    _lastSaveTime = now;
                    _log.Verbose($"Saved main window pos/size: {curPos}, {curSize}");
                }
            }
        }
        catch (Exception ex) { _log.Debug($"[MainWindow] Window pos/size auto-save failed: {ex.Message}"); }
    }

    /// <summary>
    /// Persists the given windowed position/size to the currently active windowed layout.
    /// This ensures the position is remembered per-layout and survives plugin restarts.
    /// </summary>
    private void PersistWindowedPosToActiveLayout(Vector2 pos, Vector2 size)
    {
        try
        {
            var layouts = Config.Layouts;
            if (layouts == null) return;

            var activeName = Config.ActiveWindowedLayoutName;
            var layout = !string.IsNullOrWhiteSpace(activeName)
                ? layouts.Find(x => x.Type == LayoutType.Windowed && x.Name == activeName)
                : layouts.Find(x => x.Type == LayoutType.Windowed);

            if (layout != null)
            {
                layout.WindowedPos = pos;
                layout.WindowedSize = size;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[MainWindow] Failed to persist windowed pos to layout: {ex.Message}");
        }
    }
}
