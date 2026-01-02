using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Status;

/// <summary>
/// Base class for status indicator tools that share common functionality.
/// Provides a standard ShowDetails property with settings persistence.
/// </summary>
public abstract class StatusToolBase : ToolComponent
{
    /// <summary>
    /// Whether to show extra details beyond the primary status indicator.
    /// </summary>
    public bool ShowDetails { get; set; } = true;

    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        var showDetails = ShowDetails;
        if (ImGui.Checkbox("Show Details", ref showDetails))
        {
            ShowDetails = showDetails;
            NotifyToolSettingsChanged();
        }
        
        // Allow subclasses to add additional settings
        DrawAdditionalSettings();
    }
    
    /// <summary>
    /// Override to add additional tool-specific settings below the ShowDetails checkbox.
    /// </summary>
    protected virtual void DrawAdditionalSettings() { }

    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// Override and call base to include ShowDetails in derived class settings.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var settings = ExportAdditionalSettings() ?? new Dictionary<string, object?>();
        settings["ShowDetails"] = ShowDetails;
        return settings;
    }
    
    /// <summary>
    /// Override to export additional tool-specific settings.
    /// </summary>
    protected virtual Dictionary<string, object?>? ExportAdditionalSettings() => null;

    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        ShowDetails = GetSetting(settings, "ShowDetails", ShowDetails);
        ImportAdditionalSettings(settings);
    }
    
    /// <summary>
    /// Override to import additional tool-specific settings.
    /// </summary>
    protected virtual void ImportAdditionalSettings(Dictionary<string, object?> settings) { }
}
