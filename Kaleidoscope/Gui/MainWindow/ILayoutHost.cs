using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Contract between the WindowContentContainer and its host window.
/// Replaces the 20+ callback/delegate fields with a single typed dependency,
/// making the interaction points explicit and testable.
/// </summary>
public interface ILayoutHost
{
    // ── Layout Persistence ──────────────────────────────────────────────

    /// <summary>Saves a layout with the given name and tool states.</summary>
    void SaveLayout(string name, List<ToolLayoutState> tools);

    /// <summary>Loads a layout by name (may prompt unsaved-changes dialog).</summary>
    void LoadLayout(string name);

    /// <summary>Returns names of available layouts for the current mode.</summary>
    List<string> GetAvailableLayoutNames();

    /// <summary>Returns the name of the currently active layout.</summary>
    string GetCurrentLayoutName();

    // ── Dirty State ─────────────────────────────────────────────────────

    /// <summary>Whether the current layout has unsaved changes.</summary>
    bool IsDirty { get; }

    /// <summary>Explicitly saves the current layout.</summary>
    void SaveLayoutExplicit();

    /// <summary>Discards unsaved changes and reverts to persisted state.</summary>
    void DiscardChanges();

    /// <summary>Marks the layout as dirty with the current tool states.</summary>
    void MarkLayoutDirty(List<ToolLayoutState> tools);

    // ── Unsaved Changes Dialog ──────────────────────────────────────────

    /// <summary>Whether the unsaved changes confirmation dialog should be shown.</summary>
    bool ShowUnsavedChangesDialog { get; }

    /// <summary>Description of the pending destructive action for the dialog.</summary>
    string PendingActionDescription { get; }

    /// <summary>Handles the user's choice in the unsaved changes dialog.</summary>
    void HandleUnsavedChangesChoice(UnsavedChangesChoice choice);

    // ── Presets ─────────────────────────────────────────────────────────

    /// <summary>Saves a tool configuration as a user preset.</summary>
    void SavePreset(string toolType, string presetName, Dictionary<string, object?> settings, string? description = null);

    /// <summary>Whether preset saving is available.</summary>
    bool CanSavePresets { get; }

    // ── Layouts Management ──────────────────────────────────────────────

    /// <summary>Opens the layouts management UI (config window layouts tab).</summary>
    void OpenLayoutsManager();

    // ── Interaction State ───────────────────────────────────────────────

    /// <summary>Whether the main window itself is being moved or resized.</summary>
    bool IsMainWindowInteracting { get; }

    /// <summary>Whether fullscreen mode is currently active.</summary>
    bool IsFullscreenMode { get; }

    /// <summary>Notifies the host that tool dragging state changed.</summary>
    void NotifyDraggingChanged(bool dragging);

    /// <summary>Notifies the host that tool resizing state changed.</summary>
    void NotifyResizingChanged(bool resizing);

    // ── Grid Settings ───────────────────────────────────────────────────

    /// <summary>Notifies the host that grid settings changed and need persistence.</summary>
    void NotifyGridSettingsChanged(LayoutGridSettings settings);

    // ── Config Access ───────────────────────────────────────────────────

    /// <summary>Configuration service for reading UI color defaults.</summary>
    ConfigurationService? ConfigService { get; }

    /// <summary>
    /// Returns the tool internal padding override from an external source (e.g., config window).
    /// Returns a negative value if no override is active.
    /// </summary>
    int GetExternalToolInternalPadding();
}
