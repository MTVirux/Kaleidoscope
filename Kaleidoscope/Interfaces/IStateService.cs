namespace Kaleidoscope.Interfaces;

/// <summary>
/// Tracks the current UI mode states for the plugin:
/// - Fullscreen mode: whether the plugin is displaying in fullscreen
/// - Edit mode: whether the HUD layout is being edited
/// - Locked mode: whether the window position/size is locked
/// - Dragging state: whether a tool or element is currently being dragged
/// </summary>
public interface IStateService
{
        /// <summary>
        /// Gets or sets whether the plugin is currently in fullscreen mode.
        /// </summary>
        bool IsFullscreen { get; set; }

        /// <summary>
        /// Gets or sets whether the plugin is in HUD edit mode (tools can be moved/resized).
        /// </summary>
        bool IsEditMode { get; set; }

        /// <summary>
        /// Gets or sets whether the main window is locked (no moving or resizing).
        /// </summary>
        bool IsLocked { get; set; }

        /// <summary>
        /// Gets or sets whether something is currently being dragged.
        /// </summary>
        bool IsDragging { get; set; }

        /// <summary>
        /// Gets or sets whether something is currently being resized.
        /// </summary>
        bool IsResizing { get; set; }

        /// <summary>
        /// Gets or sets whether the main window is currently being moved.
        /// </summary>
        bool IsMainWindowMoving { get; set; }

        /// <summary>
        /// Gets or sets whether the main window is currently being resized.
        /// </summary>
        bool IsMainWindowResizing { get; set; }

        /// <summary>
        /// Returns true if the main window is being moved or resized.
        /// </summary>
        bool IsMainWindowInteracting { get; }

        /// <summary>
        /// Returns true if any interaction is active (dragging or resizing).
        /// </summary>
        bool IsInteracting { get; }

        /// <summary>
        /// Returns true if layout editing operations are allowed (edit mode is on and window is locked).
        /// </summary>
        bool CanEditLayout { get; }

        /// <summary>
        /// Event raised when the fullscreen state changes.
        /// </summary>
        event Action<bool>? OnFullscreenChanged;

        /// <summary>
        /// Event raised when the edit mode state changes.
        /// </summary>
        event Action<bool>? OnEditModeChanged;

        /// <summary>
        /// Event raised when the locked state changes.
        /// </summary>
        event Action<bool>? OnLockedChanged;

        /// <summary>
        /// Event raised when the dragging state changes.
        /// </summary>
        event Action<bool>? OnDraggingChanged;

        /// <summary>
        /// Event raised when the resizing state changes.
        /// </summary>
        event Action<bool>? OnResizingChanged;

        /// <summary>
        /// Toggles edit mode and applies appropriate side effects (e.g., locking window).
        /// </summary>
        void ToggleEditMode();

        /// <summary>
    /// Toggles the locked state.
    /// </summary>
    void ToggleLocked();

    /// <summary>
    /// Enters fullscreen mode.
    /// </summary>
    void EnterFullscreen();

    /// <summary>
    /// Exits fullscreen mode.
    /// </summary>
    void ExitFullscreen();
}
