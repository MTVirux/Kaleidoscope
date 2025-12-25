using Dalamud.Bindings.ImGui;
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
/// A simple tool that displays customizable text.
/// Useful for adding labels, notes, or separators to layouts.
/// </summary>
public class LabelTool : ToolComponent
{
    private readonly ConfigurationService _configService;
    private string _text = "Label";
    private Vector4 _textColor = new(1f, 1f, 1f, 1f);
    private bool _wrapText = true;
    private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Left;
    private VerticalAlignment _verticalAlignment = VerticalAlignment.Top;

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
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the text color.
    /// </summary>
    public Vector4 TextColor
    {
        get => _textColor;
        set => _textColor = value;
    }

    /// <summary>
    /// Gets or sets whether text should wrap.
    /// </summary>
    public bool WrapText
    {
        get => _wrapText;
        set => _wrapText = value;
    }

    /// <summary>
    /// Gets or sets the horizontal alignment.
    /// </summary>
    public HorizontalAlignment HorizontalAlign
    {
        get => _horizontalAlignment;
        set => _horizontalAlignment = value;
    }

    /// <summary>
    /// Gets or sets the vertical alignment.
    /// </summary>
    public VerticalAlignment VerticalAlign
    {
        get => _verticalAlignment;
        set => _verticalAlignment = value;
    }

    public override void DrawContent()
    {
        try
        {
            var availableSize = ImGui.GetContentRegionAvail();
            var wrapWidth = _wrapText ? availableSize.X : 0f;
            
            // Calculate text size for alignment
            var textSize = ImGui.CalcTextSize(_text, _wrapText, wrapWidth);
            
            // Calculate vertical offset
            var offsetY = 0f;
            switch (_verticalAlignment)
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

            if (_wrapText)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableSize.X);
            }

            // Calculate horizontal offset
            var offsetX = 0f;
            switch (_horizontalAlignment)
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

            ImGui.TextColored(_textColor, _text);

            if (_wrapText)
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

    protected override void DrawToolSettings()
    {
        var changed = false;

        // Text input (multiline)
        ImGui.TextUnformatted("Label Text:");
        var text = _text;
        if (ImGui.InputTextMultiline("##LabelText", ref text, 1024, new Vector2(-1, 60)))
        {
            _text = text;
            changed = true;
        }

        ImGui.Spacing();

        // Text color
        var color = _textColor;
        if (ImGui.ColorEdit4("Text Color", ref color))
        {
            _textColor = color;
            changed = true;
        }

        ImGui.Spacing();

        // Wrap text checkbox
        var wrapText = _wrapText;
        if (ImGui.Checkbox("Wrap Text", ref wrapText))
        {
            _wrapText = wrapText;
            changed = true;
        }

        ImGui.Spacing();

        // Horizontal alignment
        ImGui.TextUnformatted("Horizontal Alignment:");
        var hAlign = (int)_horizontalAlignment;
        if (ImGui.RadioButton("Left", ref hAlign, (int)HorizontalAlignment.Left))
        {
            _horizontalAlignment = HorizontalAlignment.Left;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Center##H", ref hAlign, (int)HorizontalAlignment.Center))
        {
            _horizontalAlignment = HorizontalAlignment.Center;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Right", ref hAlign, (int)HorizontalAlignment.Right))
        {
            _horizontalAlignment = HorizontalAlignment.Right;
            changed = true;
        }

        ImGui.Spacing();

        // Vertical alignment
        ImGui.TextUnformatted("Vertical Alignment:");
        var vAlign = (int)_verticalAlignment;
        if (ImGui.RadioButton("Top", ref vAlign, (int)VerticalAlignment.Top))
        {
            _verticalAlignment = VerticalAlignment.Top;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Middle", ref vAlign, (int)VerticalAlignment.Middle))
        {
            _verticalAlignment = VerticalAlignment.Middle;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Bottom", ref vAlign, (int)VerticalAlignment.Bottom))
        {
            _verticalAlignment = VerticalAlignment.Bottom;
            changed = true;
        }

        if (changed)
        {
            NotifyToolSettingsChanged();
        }
    }
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["Text"] = _text,
            ["TextColorR"] = _textColor.X,
            ["TextColorG"] = _textColor.Y,
            ["TextColorB"] = _textColor.Z,
            ["TextColorA"] = _textColor.W,
            ["WrapText"] = _wrapText,
            ["HorizontalAlignment"] = (int)_horizontalAlignment,
            ["VerticalAlignment"] = (int)_verticalAlignment
        };
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        _text = GetSetting(settings, "Text", _text) ?? _text;
        
        var r = GetSetting(settings, "TextColorR", _textColor.X);
        var g = GetSetting(settings, "TextColorG", _textColor.Y);
        var b = GetSetting(settings, "TextColorB", _textColor.Z);
        var a = GetSetting(settings, "TextColorA", _textColor.W);
        _textColor = new Vector4(r, g, b, a);
        
        _wrapText = GetSetting(settings, "WrapText", _wrapText);
        _horizontalAlignment = (HorizontalAlignment)GetSetting(settings, "HorizontalAlignment", (int)_horizontalAlignment);
        _verticalAlignment = (VerticalAlignment)GetSetting(settings, "VerticalAlignment", (int)_verticalAlignment);
    }
}
