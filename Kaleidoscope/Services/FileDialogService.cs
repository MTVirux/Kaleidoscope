using Dalamud.Interface.ImGuiFileDialog;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service wrapper for Dalamud's FileDialogManager.
/// Provides a centralized way to open file and folder dialogs.
/// </summary>
public sealed class FileDialogService : IService, IDisposable
{
    /// <summary>
    /// Static accessor for components without DI access.
    /// </summary>
    public static FileDialogService? Instance { get; private set; }

    private readonly FileDialogManager _manager;

    public FileDialogService()
    {
        _manager = new FileDialogManager
        {
            AddedWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags.NoCollapse | Dalamud.Bindings.ImGui.ImGuiWindowFlags.NoDocking,
        };
        Instance = this;
    }

    /// <summary>
    /// Opens a folder picker dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="callback">Callback with (success, selectedPath).</param>
    /// <param name="startPath">Optional starting directory.</param>
    public void OpenFolderPicker(string title, Action<bool, string> callback, string? startPath = null)
    {
        _manager.OpenFolderDialog(title, callback, startPath);
    }

    /// <summary>
    /// Opens a file picker dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">File filters (e.g., ".log,.txt").</param>
    /// <param name="callback">Callback with (success, selectedPaths).</param>
    /// <param name="maxSelection">Maximum number of files that can be selected.</param>
    /// <param name="startPath">Optional starting directory.</param>
    public void OpenFilePicker(string title, string filters, Action<bool, List<string>> callback, int maxSelection = 1, string? startPath = null)
    {
        _manager.OpenFileDialog(title, filters, callback, maxSelection, startPath);
    }

    /// <summary>
    /// Opens a save file dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filters">File filters.</param>
    /// <param name="defaultFileName">Default file name.</param>
    /// <param name="defaultExtension">Default file extension.</param>
    /// <param name="callback">Callback with (success, selectedPath).</param>
    /// <param name="startPath">Optional starting directory.</param>
    public void OpenSavePicker(string title, string filters, string defaultFileName, string defaultExtension, 
        Action<bool, string> callback, string? startPath = null)
    {
        _manager.SaveFileDialog(title, filters, defaultFileName, defaultExtension, callback, startPath);
    }

    /// <summary>
    /// Draws the dialog if one is open. Must be called each frame.
    /// </summary>
    public void Draw()
    {
        _manager.Draw();
    }

    /// <summary>
    /// Resets/closes any open dialog.
    /// </summary>
    public void Reset()
    {
        _manager.Reset();
    }

    public void Dispose()
    {
        _manager.Reset();
    }
}
