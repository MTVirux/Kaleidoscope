using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models.Settings;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.Label;

/// <summary>
/// Horizontal alignment options for label text.
/// </summary>
public enum HorizontalAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Vertical alignment options for label text.
/// </summary>
public enum VerticalAlignment
{
    Top,
    Middle,
    Bottom
}

/// <summary>
/// Settings class for LabelTool with all configurable properties.
/// </summary>
public class LabelToolSettings
{
    public string Text { get; set; } = "Label";
    public Vector4 TextColor { get; set; } = new(1f, 1f, 1f, 1f);
    public bool WrapText { get; set; } = true;
    public HorizontalAlignment HorizontalAlign { get; set; } = HorizontalAlignment.Left;
    public VerticalAlignment VerticalAlign { get; set; } = VerticalAlignment.Top;
}

/// <summary>
/// A simple tool that displays customizable text.
/// Useful for adding labels, notes, or separators to layouts.
/// </summary>
public class LabelTool : ToolComponent
{
    private readonly ConfigurationService _configService;
    
    // Settings instance and schema
    private readonly LabelToolSettings _settings = new();
    
    private static readonly SettingsSchema<LabelToolSettings> Schema = SettingsSchema.For<LabelToolSettings>()
        .TextMultiline(s => s.Text, "Label Text:", "The text to display in the label", "Label", 1024, new Vector2(-1, 60))
        .Spacing()
        .ColorEdit(s => s.TextColor, "Text Color", "Color of the label text", new Vector4(1f, 1f, 1f, 1f))
        .Spacing()
        .Checkbox(s => s.WrapText, "Wrap Text", "Automatically wrap text to fit the label width", true)
        .Spacing()
        .RadioGroup(s => s.HorizontalAlign, "Horizontal Alignment:", "Horizontal text alignment", HorizontalAlignment.Left)
        .Spacing()
        .RadioGroup(s => s.VerticalAlign, "Vertical Alignment:", "Vertical text alignment", VerticalAlignment.Top);

    public LabelTool(ConfigurationService configService)
    {
        _configService = configService;
        Title = "Label";
        Size = new Vector2(200, 100);
        HeaderVisible = false;
        BackgroundEnabled = true;
    }

    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    public string Text
    {
        get => _settings.Text;
        set => _settings.Text = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the text color.
    /// </summary>
    public Vector4 TextColor
    {
        get => _settings.TextColor;
        set => _settings.TextColor = value;
    }

    /// <summary>
    /// Gets or sets whether text should wrap.
    /// </summary>
    public bool WrapText
    {
        get => _settings.WrapText;
        set => _settings.WrapText = value;
    }

    /// <summary>
    /// Gets or sets the horizontal alignment.
    /// </summary>
    public HorizontalAlignment HorizontalAlign
    {
        get => _settings.HorizontalAlign;
        set => _settings.HorizontalAlign = value;
    }

    /// <summary>
    /// Gets or sets the vertical alignment.
    /// </summary>
    public VerticalAlignment VerticalAlign
    {
        get => _settings.VerticalAlign;
        set => _settings.VerticalAlign = value;
    }

    public override void DrawContent()
    {
        try
        {
            var availableSize = ImGui.GetContentRegionAvail();
            var wrapWidth = _settings.WrapText ? availableSize.X : 0f;
            
            // Calculate text size for alignment
            var textSize = ImGui.CalcTextSize(_settings.Text, _settings.WrapText, wrapWidth);
            
            // Calculate vertical offset
            var offsetY = 0f;
            switch (_settings.VerticalAlign)
            {
                case VerticalAlignment.Middle:
                    offsetY = (availableSize.Y - textSize.Y) * 0.5f;
                    break;
                case VerticalAlignment.Bottom:
                    offsetY = availableSize.Y - textSize.Y;
                    break;
            }
            
            if (offsetY > 0)
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offsetY);
            }

            if (_settings.WrapText)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableSize.X);
            }

            // Calculate horizontal offset
            var offsetX = 0f;
            switch (_settings.HorizontalAlign)
            {
                case HorizontalAlignment.Center:
                    offsetX = (availableSize.X - textSize.X) * 0.5f;
                    break;
                case HorizontalAlignment.Right:
                    offsetX = availableSize.X - textSize.X;
                    break;
            }
            
            if (offsetX > 0)
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
            }

            ImGui.TextColored(_settings.TextColor, _settings.Text);

            if (_settings.WrapText)
            {
                ImGui.PopTextWrapPos();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[LabelTool] Draw error: {ex.Message}");
        }
    }

    public override bool HasSettings => true;

    protected override bool HasToolSettings => true;
    
    protected override object? GetToolSettingsSchema() => Schema;
    
    protected override object? GetToolSettingsObject() => _settings;
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return Schema.ToDictionary(_settings)!;
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        Schema.FromDictionary(_settings, settings);
    }
}
