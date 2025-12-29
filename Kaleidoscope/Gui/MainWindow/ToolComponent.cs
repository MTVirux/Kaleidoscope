using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Models.Settings;
using Kaleidoscope.Services;
using MTGui.Tree;
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
    /// Event raised when tool-specific settings change and need to be persisted.
    /// The container should subscribe to this to trigger layout saves.
    /// </summary>
    public event Action? OnToolSettingsChanged;
    
    /// <summary>
    /// Raises the OnToolSettingsChanged event to notify the container that settings need persistence.
    /// </summary>
    protected void NotifyToolSettingsChanged() => OnToolSettingsChanged?.Invoke();
    
    /// <summary>
    /// The inherent name of this tool type (e.g., "Data Table", "Data Graph").
    /// This should not change based on user customization or presets.
    /// </summary>
    public virtual string ToolName => "Tool";
    
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

    public abstract void RenderToolContent();

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
    /// Override to provide a settings schema for declarative settings rendering.
    /// When provided, the schema will be used instead of DrawToolSettings().
    /// </summary>
    /// <returns>A SettingsSchema instance, or null to use DrawToolSettings().</returns>
    protected virtual object? GetToolSettingsSchema() => null;
    
    /// <summary>
    /// Override to provide the settings object instance that the schema binds to.
    /// Required when GetToolSettingsSchema() returns a schema.
    /// </summary>
    /// <returns>The settings instance, or null if not using schema-based settings.</returns>
    protected virtual object? GetToolSettingsObject() => null;
    
    /// <summary>
    /// Draws all settings for this tool, including tool-specific settings and registered component settings.
    /// Override DrawToolSettings to add tool-specific settings that appear before component settings.
    /// All settings sections are wrapped in collapsible headers.
    /// </summary>
    public virtual void DrawSettings()
    {
        try
        {
            // Draw tool-specific settings first (in collapsible header)
            if (HasToolSettings)
            {
                if (MTTreeHelpers.DrawCollapsingSection("Tool Settings", true))
                {
                    // Check if tool provides a schema for declarative rendering
                    var schema = GetToolSettingsSchema();
                    var settingsObj = GetToolSettingsObject();
                    
                    if (schema != null && settingsObj != null)
                    {
                        // Use schema-based rendering
                        if (DrawSettingsFromSchema(schema, settingsObj))
                        {
                            NotifyToolSettingsChanged();
                        }
                    }
                    else
                    {
                        // Fall back to imperative DrawToolSettings()
                        DrawToolSettings();
                    }
                }
            }
            
            // Add separator between tool settings and component settings if there are any
            if (_settingsProviders.Any(p => p.HasSettings))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }
            
            // Draw all registered component settings (each in its own collapsible header)
            foreach (var provider in _settingsProviders)
            {
                if (!provider.HasSettings) continue;
                
                if (ImGui.CollapsingHeader(provider.SettingsName))
                {
                    provider.DrawSettings();
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ToolComponent] DrawSettings error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Draws settings from a schema using the SettingsSchemaRenderer.
    /// Handles type discovery for the generic schema type.
    /// </summary>
    private static bool DrawSettingsFromSchema(object schema, object settings)
    {
        // Use reflection to call SettingsSchemaRenderer.Draw<TSettings>(schema, settings)
        var schemaType = schema.GetType();
        if (!schemaType.IsGenericType || schemaType.GetGenericTypeDefinition() != typeof(SettingsSchema<>))
            return false;
        
        var settingsType = schemaType.GetGenericArguments()[0];
        var drawMethod = typeof(SettingsSchemaRenderer)
            .GetMethod(nameof(SettingsSchemaRenderer.Draw))
            ?.MakeGenericMethod(settingsType);
        
        if (drawMethod == null) return false;
        
        var result = drawMethod.Invoke(null, new[] { schema, settings, true });
        return result is true;
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
    
    /// <summary>
    /// Exports tool-specific settings to a dictionary for layout persistence.
    /// Override in derived classes to persist instance-specific settings.
    /// </summary>
    /// <returns>Dictionary of settings to persist, or null if no settings to export.</returns>
    public virtual Dictionary<string, object?>? ExportToolSettings() => null;
    
    /// <summary>
    /// Imports tool-specific settings from a dictionary when loading a layout.
    /// Override in derived classes to restore instance-specific settings.
    /// </summary>
    /// <param name="settings">Dictionary of settings from the layout.</param>
    public virtual void ImportToolSettings(Dictionary<string, object?>? settings) { }
    
    /// <summary>
    /// Helper method to safely get a typed value from settings dictionary.
    /// Handles JSON deserialization for complex types.
    /// </summary>
    protected static T? GetSetting<T>(Dictionary<string, object?>? settings, string key, T? defaultValue = default)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return defaultValue;
        
        try
        {
            // Handle Newtonsoft.Json JValue/JToken (used by ConfigManager)
            if (value is Newtonsoft.Json.Linq.JToken jToken)
            {
                return jToken.ToObject<T>();
            }
            
            // Handle System.Text.Json JsonElement (when loaded from JSON)
            if (value is System.Text.Json.JsonElement jsonElement)
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
            
            // Direct cast for simple types
            if (value is T typedValue)
                return typedValue;
            
            // Try convert for numeric types
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

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
