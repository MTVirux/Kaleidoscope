using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Base class for draggable/resizable tool components in the main window.
/// Implements IDisposable for consistent cleanup of derived tool resources.
/// Supports automatic settings aggregation from child ISettingsProvider components.
/// </summary>
public abstract class ToolComponent : IDisposable
{
    private readonly List<ISettingsProvider> _settingsProviders = new();
    
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// The default title for this tool type.
    /// </summary>
    public string Title { get; set; } = "Tool";
    
    /// <summary>
    /// User-customized title override. When set, this is displayed instead of Title.
    /// </summary>
    public string? CustomTitle { get; set; } = null;
    
    /// <summary>
    /// Gets the display title for the header. Returns CustomTitle if set, otherwise Title.
    /// </summary>
    public string DisplayTitle => !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle : Title;
    
    public Vector2 Position { get; set; } = new(50, 50);
    public Vector2 Size { get; set; } = new(300, 200);
    public bool Visible { get; set; } = true;
    public bool BackgroundEnabled { get; set; } = true;
    public Vector4 BackgroundColor { get; set; } = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);
    public bool HeaderVisible { get; set; } = true;
    public bool OutlineEnabled { get; set; } = true;

    // Grid-based coordinates for proportional resizing
    public float GridCol { get; set; } = 0f;
    public float GridRow { get; set; } = 0f;
    public float GridColSpan { get; set; } = 4f;
    public float GridRowSpan { get; set; } = 4f;
    public bool HasGridCoords { get; set; } = false;

    public abstract void DrawContent();

    /// <summary>
    /// Gets whether this tool has settings to display.
    /// Returns true if there are any registered settings providers or if HasToolSettings is true.
    /// </summary>
    public virtual bool HasSettings => _settingsProviders.Count > 0 || HasToolSettings;
    
    /// <summary>
    /// Override in derived classes to indicate the tool has its own settings beyond component settings.
    /// </summary>
    protected virtual bool HasToolSettings => false;
    
    /// <summary>
    /// Draws all settings for this tool, including tool-specific settings and registered component settings.
    /// Override DrawToolSettings to add tool-specific settings that appear before component settings.
    /// </summary>
    public virtual void DrawSettings()
    {
        try
        {
            // Draw tool-specific settings first
            if (HasToolSettings)
            {
                DrawToolSettings();
            }
            
            // Draw all registered component settings
            foreach (var provider in _settingsProviders)
            {
                if (!provider.HasSettings) continue;
                
                ImGui.Spacing();
                ImGui.TextUnformatted(provider.SettingsName);
                ImGui.Separator();
                
                provider.DrawSettings();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ToolComponent] DrawSettings error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Override in derived classes to draw tool-specific settings.
    /// These appear before any registered component settings.
    /// </summary>
    protected virtual void DrawToolSettings() { }
    
    /// <summary>
    /// Registers a settings provider (widget/component) whose settings should be included
    /// in this tool's settings panel. Call this when adding a widget to the tool.
    /// </summary>
    /// <param name="provider">The settings provider to register.</param>
    protected void RegisterSettingsProvider(ISettingsProvider provider)
    {
        if (provider != null && !_settingsProviders.Contains(provider))
        {
            _settingsProviders.Add(provider);
        }
    }
    
    /// <summary>
    /// Unregisters a settings provider from this tool.
    /// Call this when removing a widget from the tool.
    /// </summary>
    /// <param name="provider">The settings provider to unregister.</param>
    protected void UnregisterSettingsProvider(ISettingsProvider provider)
    {
        _settingsProviders.Remove(provider);
    }
    
    /// <summary>
    /// Gets the registered settings providers for this tool.
    /// </summary>
    protected IReadOnlyList<ISettingsProvider> SettingsProviders => _settingsProviders;
    
    /// <summary>
    /// Disposes resources held by this tool. Override in derived classes for cleanup.
    /// </summary>
    public virtual void Dispose() { }

    protected void ShowSettingTooltip(string description, string defaultText)
    {
        try
        {
            if (!ImGui.IsItemHovered()) return;

            ImGui.BeginTooltip();
            if (!string.IsNullOrEmpty(description))
                ImGui.TextUnformatted(description);
            if (!string.IsNullOrEmpty(defaultText))
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Default: {defaultText}");
            }
            ImGui.EndTooltip();
        }
        catch (Exception ex)
        {
            LogService.Debug($"Tooltip error: {ex.Message}");
        }
    }
}
