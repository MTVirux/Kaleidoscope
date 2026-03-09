using System.Numerics;

namespace Kaleidoscope;

public sealed class ContentLayoutState
{
    public string Name { get; set; } = string.Empty;
    public LayoutType Type { get; set; } = LayoutType.Windowed;
    public List<ContentComponentState> Components { get; set; } = new();
    public List<ToolLayoutState> Tools { get; set; } = new();

    public bool AutoAdjustResolution { get; set; } = true;
    public int Columns { get; set; } = 16;
    public int Rows { get; set; } = 9;
    public int Subdivisions { get; set; } = 8;
    public int GridResolutionMultiplier { get; set; } = 2;
    
    /// <summary>
    /// Internal padding in pixels inside each tool.
    /// A value of 0 means no padding. Default is 4.
    /// </summary>
    public int ToolInternalPaddingPx { get; set; } = 4;

    /// <summary>
    /// Saved windowed-mode window position for this layout.
    /// When exiting fullscreen, the window restores to this position.
    /// Only meaningful for windowed layouts; fullscreen layouts ignore this.
    /// </summary>
    public Vector2? WindowedPos { get; set; }

    /// <summary>
    /// Saved windowed-mode window size for this layout.
    /// When exiting fullscreen, the window restores to this size.
    /// Only meaningful for windowed layouts; fullscreen layouts ignore this.
    /// </summary>
    public Vector2? WindowedSize { get; set; }
}

public sealed class ContentComponentState
{
    public int Col { get; set; }
    public int Row { get; set; }
    public int ColSpan { get; set; }
    public int RowSpan { get; set; }
}

public sealed class ToolLayoutState
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CustomTitle { get; set; } = null;
    public Vector2 Position { get; set; } = new(50, 50);
    public Vector2 Size { get; set; } = new(300, 200);
    public bool Visible { get; set; } = true;

    public bool BackgroundEnabled { get; set; } = false;
    public bool HeaderVisible { get; set; } = true;
    public bool OutlineEnabled { get; set; } = true;
    public Vector4 BackgroundColor { get; set; } = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);

    public float GridCol { get; set; } = 0f;
    public float GridRow { get; set; } = 0f;
    public float GridColSpan { get; set; } = 4f;
    public float GridRowSpan { get; set; } = 4f;
    public bool HasGridCoords { get; set; } = false;
    
    /// <summary>
    /// List of series names that are hidden in this tool instance.
    /// </summary>
    public List<string> HiddenSeries { get; set; } = new();
    
    /// <summary>
    /// Tool-specific settings stored as key-value pairs.
    /// Each tool type can store its own settings here for instance-specific persistence.
    /// </summary>
    public Dictionary<string, object?> ToolSettings { get; set; } = new();
}

public sealed class UserToolPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public string Name { get; set; } = "New Preset";
    
    public string ToolType { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    
    public Dictionary<string, object?> Settings { get; set; } = new();
}

public sealed class UIColors
{
    public Vector4 MainWindowBackground { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);
    public Vector4 FullscreenBackground { get; set; } = new(0.06f, 0.06f, 0.06f, 0.94f);
    public Vector4 ToolBackground { get; set; } = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);
    public Vector4 ToolHeaderText { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 ToolBorder { get; set; } = new(0.43f, 0.43f, 0.5f, 0.5f);
    public Vector4 TableHeader { get; set; } = new(0.26f, 0.26f, 0.28f, 1f);
    public Vector4 TableRowEven { get; set; } = new(0f, 0f, 0f, 0f);
    public Vector4 TableRowOdd { get; set; } = new(0.1f, 0.1f, 0.1f, 0.3f);
    public Vector4 TableTotalRow { get; set; } = new(0.3f, 0.3f, 0.3f, 0.5f);
    public Vector4 TextPrimary { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 TextSecondary { get; set; } = new(0.7f, 0.7f, 0.7f, 1f);
    public Vector4 TextDisabled { get; set; } = new(0.5f, 0.5f, 0.5f, 1f);
    public Vector4 AccentPrimary { get; set; } = new(0.26f, 0.59f, 0.98f, 1f);
    public Vector4 AccentSuccess { get; set; } = new(0.2f, 0.8f, 0.2f, 1f);
    public Vector4 AccentWarning { get; set; } = new(1f, 0.7f, 0.3f, 1f);
    public Vector4 AccentError { get; set; } = new(0.9f, 0.2f, 0.2f, 1f);
    public Vector4 QuickAccessBarBackground { get; set; } = new(0.1f, 0.1f, 0.1f, 0.87f);
    public Vector4 QuickAccessBarSeparator { get; set; } = new(0.31f, 0.31f, 0.31f, 1f);
    public Vector4 GraphDefault { get; set; } = new(0.4f, 0.6f, 0.9f, 1f);
    public Vector4 GraphAxis { get; set; } = new(0.5f, 0.5f, 0.5f, 0.5f);
    
    public void ResetToDefaults()
    {
        MainWindowBackground = new(0.06f, 0.06f, 0.06f, 0.94f);
        FullscreenBackground = new(0.06f, 0.06f, 0.06f, 0.94f);
        ToolBackground = new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f);
        ToolHeaderText = new(1f, 1f, 1f, 1f);
        ToolBorder = new(0.43f, 0.43f, 0.5f, 0.5f);
        TableHeader = new(0.26f, 0.26f, 0.28f, 1f);
        TableRowEven = new(0f, 0f, 0f, 0f);
        TableRowOdd = new(0.1f, 0.1f, 0.1f, 0.3f);
        TableTotalRow = new(0.3f, 0.3f, 0.3f, 0.5f);
        TextPrimary = new(1f, 1f, 1f, 1f);
        TextSecondary = new(0.7f, 0.7f, 0.7f, 1f);
        TextDisabled = new(0.5f, 0.5f, 0.5f, 1f);
        AccentPrimary = new(0.26f, 0.59f, 0.98f, 1f);
        AccentSuccess = new(0.2f, 0.8f, 0.2f, 1f);
        AccentWarning = new(1f, 0.7f, 0.3f, 1f);
        AccentError = new(0.9f, 0.2f, 0.2f, 1f);
        QuickAccessBarBackground = new(0.1f, 0.1f, 0.1f, 0.87f);
        QuickAccessBarSeparator = new(0.31f, 0.31f, 0.31f, 1f);
        GraphDefault = new(0.4f, 0.6f, 0.9f, 1f);
        GraphAxis = new(0.5f, 0.5f, 0.5f, 0.5f);
    }
}
