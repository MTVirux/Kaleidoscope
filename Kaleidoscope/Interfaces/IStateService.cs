namespace Kaleidoscope.Interfaces;

/// <summary>
/// Tracks UI mode states: fullscreen, edit, locked, and drag states.
/// </summary>
public interface IStateService
{
    bool IsFullscreen { get; set; }
    bool IsEditMode { get; set; }
    bool IsLocked { get; set; }
    bool IsDragging { get; set; }
    bool IsResizing { get; set; }
    bool IsMainWindowMoving { get; set; }
    bool IsMainWindowResizing { get; set; }
    bool IsMainWindowInteracting { get; }
    bool IsInteracting { get; }
    bool CanEditLayout { get; }

    event Action<bool>? OnFullscreenChanged;
    event Action<bool>? OnEditModeChanged;
    event Action<bool>? OnLockedChanged;
    event Action<bool>? OnDraggingChanged;
    event Action<bool>? OnResizingChanged;

    void ToggleEditMode();
    void ToggleLocked();
    void EnterFullscreen();
    void ExitFullscreen();
}
