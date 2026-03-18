using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Per-frame drawing context bundling computed layout values.
/// Created at the start of each Draw() call and passed to sub-renderers
/// so they don't need to recompute window positions and grid metrics.
/// </summary>
public readonly struct DrawContext
{
    /// <summary>The ImGui draw list for the current window.</summary>
    public readonly ImDrawListPtr DrawList;
    
    /// <summary>Absolute screen position of the top-left corner of the content area.</summary>
    public readonly Vector2 ContentMin;
    
    /// <summary>Absolute screen position of the bottom-right corner of the content area.</summary>
    public readonly Vector2 ContentMax;
    
    /// <summary>Content origin (same as ContentMin) used for tool positioning.</summary>
    public readonly Vector2 ContentOrigin;
    
    /// <summary>Available content region size (ContentMax - ContentMin).</summary>
    public readonly Vector2 AvailRegion;
    
    /// <summary>Effective number of grid columns.</summary>
    public readonly int EffectiveCols;
    
    /// <summary>Effective number of grid rows.</summary>
    public readonly int EffectiveRows;
    
    /// <summary>Width of one grid cell in pixels.</summary>
    public readonly float CellW;
    
    /// <summary>Height of one grid cell in pixels.</summary>
    public readonly float CellH;
    
    /// <summary>Whether edit mode is currently active.</summary>
    public readonly bool EditMode;

    public DrawContext(
        ImDrawListPtr drawList,
        Vector2 contentMin,
        Vector2 contentMax,
        Vector2 contentOrigin,
        Vector2 availRegion,
        int effectiveCols,
        int effectiveRows,
        float cellW,
        float cellH,
        bool editMode)
    {
        DrawList = drawList;
        ContentMin = contentMin;
        ContentMax = contentMax;
        ContentOrigin = contentOrigin;
        AvailRegion = availRegion;
        EffectiveCols = effectiveCols;
        EffectiveRows = effectiveRows;
        CellW = cellW;
        CellH = cellH;
        EditMode = editMode;
    }
}
