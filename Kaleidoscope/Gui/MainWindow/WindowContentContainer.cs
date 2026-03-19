using System.Numerics;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

// ── Shared Types ────────────────────────────────────────────────────────────

/// <summary>Tool factory registration for the "Add tool" context menu.</summary>
internal sealed class ToolRegistration
{
    public string Id = string.Empty;
    public string Label = string.Empty;
    public string? Description;
    /// <summary>Category path for nested menus, separated by '>' (e.g. "Gil > Graph").</summary>
    public string? CategoryPath;
    public Func<Vector2, ToolComponent?> Factory = (_) => null;
}

/// <summary>Per-tool instance state: wraps a ToolComponent with drag/resize tracking.</summary>
internal sealed class ToolEntry
{
    public ToolComponent Tool;
    public Vector2 OrigPos;
    public Vector2 OrigSize;
    public bool Dragging;
    public bool Resizing;
    public Vector2 DragMouseStart;
    public Vector2 ResizeMouseStart;
    public ToolEntry(ToolComponent t)
    {
        Tool = t;
        OrigPos = t.Position;
        OrigSize = t.Size;
    }
}

// ── WindowContentContainer ──────────────────────────────────────────────────

/// <summary>
/// Slim orchestrator that manages tool layout and rendering within the main window.
/// Delegates grid drawing, tool interactions, context menus, and dialogs to
/// focused manager classes. Owns the tool list, tool registry, and grid settings.
/// </summary>
public sealed partial class WindowContentContainer
{
    // ── External Dependencies ───────────────────────────────────────────
    private readonly Func<float> _getCellWidthPercent;
    private readonly Func<float> _getCellHeightPercent;
    private readonly Func<int> _getSubdivisions;

    /// <summary>The host window that implements layout persistence and interaction state.</summary>
    internal ILayoutHost? Host { get; set; }

    /// <summary>Optional tool factory for improved layout restore (type-based lookup).</summary>
    internal ToolFactory? Factory { get; set; }

    // ── Sub-Managers ────────────────────────────────────────────────────
    internal readonly DialogManager Dialogs = new();
    internal readonly ContextMenuManager ContextMenus = new();
    internal readonly ToolInteractionManager Interactions = new();

    // ── Tool Data ───────────────────────────────────────────────────────
    internal readonly List<ToolRegistration> ToolRegistry = new();
    internal readonly List<ToolEntry> Tools = new();

    // ── Grid State ──────────────────────────────────────────────────────
    private LayoutGridSettings _currentGridSettings = new();
    private Vector2 _lastContentSize = Vector2.Zero;

    // ── Dirty Suppression ───────────────────────────────────────────────
    private bool _suppressDirtyMarking = false;

    // ── Minimum Tool Dimensions ─────────────────────────────────────────
    internal static float MinToolWidth => ConfigStatic.MinToolWidth;
    internal static float MinToolHeight => MathF.Max(16f, ImGui.GetFrameHeight());

    // ── Constructor ─────────────────────────────────────────────────────

    public WindowContentContainer(Func<float>? getCellWidthPercent = null, Func<float>? getCellHeightPercent = null, Func<int>? getSubdivisions = null)
    {
        _getCellWidthPercent = getCellWidthPercent ?? (() => 25f);
        _getCellHeightPercent = getCellHeightPercent ?? (() => 25f);
        _getSubdivisions = getSubdivisions ?? (() => 4);
    }

    // ── Public Properties ───────────────────────────────────────────────

    /// <summary>Whether any tool is currently being dragged.</summary>
    public bool IsDragging => Interactions.IsDragging;

    /// <summary>Whether any tool is currently being resized.</summary>
    public bool IsResizing => Interactions.IsResizing;

    /// <summary>Whether any interaction (drag or resize) is in progress.</summary>
    public bool IsInteracting => Interactions.IsInteracting;

    /// <summary>Gets the current grid settings for this layout.</summary>
    public LayoutGridSettings GridSettings => _currentGridSettings;

    // ── Grid Calculations ───────────────────────────────────────────────

    /// <summary>Gets the effective number of columns for the current grid settings.</summary>
    public int GetEffectiveColumns(Vector2 contentSize)
    {
        if (_currentGridSettings.AutoAdjustResolution)
        {
            var multiplier = Math.Max(1, _currentGridSettings.GridResolutionMultiplier);
            return Math.Max(1, multiplier * 16);
        }
        return Math.Max(1, _currentGridSettings.Columns);
    }

    /// <summary>Gets the effective number of rows for the current grid settings.</summary>
    public int GetEffectiveRows(Vector2 contentSize)
    {
        if (_currentGridSettings.AutoAdjustResolution)
        {
            var multiplier = Math.Max(1, _currentGridSettings.GridResolutionMultiplier);
            return Math.Max(1, multiplier * 9);
        }
        return Math.Max(1, _currentGridSettings.Rows);
    }

    // ── Grid Settings Management ────────────────────────────────────────

    /// <summary>Updates grid settings and repositions tools to maintain relative positions.</summary>
    public void UpdateGridSettings(LayoutGridSettings newSettings, Vector2 contentSize)
    {
        if (newSettings == null) return;

        var oldCols = GetEffectiveColumns(contentSize);
        var oldRows = GetEffectiveRows(contentSize);

        _currentGridSettings.CopyFrom(newSettings);

        var newCols = GetEffectiveColumns(contentSize);
        var newRows = GetEffectiveRows(contentSize);

        var newCellW = contentSize.X / MathF.Max(1f, newCols);
        var newCellH = contentSize.Y / MathF.Max(1f, newRows);

        if (oldCols > 0 && oldRows > 0 && newCols > 0 && newRows > 0 && (oldCols != newCols || oldRows != newRows))
        {
            var colScale = (float)newCols / oldCols;
            var rowScale = (float)newRows / oldRows;

            foreach (var te in Tools)
            {
                var t = te.Tool;
                t.GridCol *= colScale;
                t.GridRow *= rowScale;
                t.GridColSpan *= colScale;
                t.GridRowSpan *= rowScale;

                t.Position = new Vector2(t.GridCol * newCellW, t.GridRow * newCellH);
                t.Size = new Vector2(
                    MathF.Max(MinToolWidth, t.GridColSpan * newCellW),
                    MathF.Max(MinToolHeight, t.GridRowSpan * newCellH));
                if (newCellW > 0) t.GridColSpan = t.Size.X / newCellW;
                if (newCellH > 0) t.GridRowSpan = t.Size.Y / newCellH;
            }

            MarkLayoutDirty();
        }
    }

    /// <summary>Sets grid settings from a layout state without repositioning tools.</summary>
    public void SetGridSettingsFromLayout(ContentLayoutState? layout)
    {
        if (layout == null) return;
        _currentGridSettings = LayoutGridSettings.FromLayoutState(layout);
    }

    // ── Dirty Notification ──────────────────────────────────────────────

    /// <summary>
    /// Notifies host that layout changed. Suppressed during layout application.
    /// </summary>
    internal void MarkLayoutDirty()
    {
        if (_suppressDirtyMarking) return;
        try { Host?.MarkLayoutDirty(ExportLayout()); }
        catch (Exception ex) { LogService.Error(LogCategory.UI, "Error while invoking MarkLayoutDirty", ex); }
    }

    // ── Tool Registry ───────────────────────────────────────────────────

    public void DefineToolType(string id, string label, Func<Vector2, ToolComponent?> factory, string? description = null, string? categoryPath = null)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id");
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        ToolRegistry.Add(new ToolRegistration { Id = id, Label = label ?? id, Description = description, Factory = factory, CategoryPath = categoryPath });
    }

    // ── Tool Instance Management ────────────────────────────────────────

    public void AddToolInstance(ToolComponent tool)
    {
        if (tool == null) return;

        Tools.Add(new ToolEntry(tool));
        LogService.Debug(LogCategory.UI, $"AddToolInstance: added tool '{tool.Title ?? tool.Id ?? "<unknown>"}' total={Tools.Count}");

        tool.OnToolSettingsChanged += () => MarkLayoutDirty();
        MarkLayoutDirty();
    }

    /// <summary>Removes and disposes all tools from the container.</summary>
    public void ClearAllTools()
    {
        _suppressDirtyMarking = true;
        try
        {
            for (var i = Tools.Count - 1; i >= 0; i--)
            {
                try { Tools[i].Tool.Dispose(); }
                catch (Exception ex) { LogService.Error(LogCategory.UI, $"ClearAllTools: Failed to dispose tool at index {i}", ex); }
            }
            Tools.Clear();
            LogService.Debug(LogCategory.UI, "ClearAllTools: all tools removed");
        }
        finally { _suppressDirtyMarking = false; }
    }

    /// <summary>Adds a tool without marking dirty. Use for initial setup.</summary>
    public void AddToolInstanceWithoutDirty(ToolComponent tool)
    {
        if (tool == null) return;

        _suppressDirtyMarking = true;
        try
        {
            Tools.Add(new ToolEntry(tool));
            LogService.Debug(LogCategory.UI, $"AddToolInstanceWithoutDirty: added tool '{tool.Title ?? tool.Id ?? "<unknown>"}' total={Tools.Count}");
            tool.OnToolSettingsChanged += () => MarkLayoutDirty();
        }
        finally { _suppressDirtyMarking = false; }
    }

    /// <summary>Duplicates a tool by creating a new instance with the same settings.</summary>
    internal void DuplicateTool(ToolComponent source)
    {
        var registration = ToolRegistry.FirstOrDefault(r => r.Id == source.Id);
        if (registration == null)
        {
            LogService.Debug(LogCategory.UI, $"DuplicateTool: no registration found for tool id='{source.Id}'");
            return;
        }

        var offset = new Vector2(20, 20);
        var newTool = registration.Factory(source.Position + offset);
        if (newTool == null)
        {
            LogService.Debug(LogCategory.UI, $"DuplicateTool: factory returned null for tool id='{source.Id}'");
            return;
        }

        newTool.Id = registration.Id;
        newTool.Size = source.Size;
        newTool.Visible = source.Visible;
        newTool.BackgroundEnabled = source.BackgroundEnabled;
        newTool.HeaderVisible = source.HeaderVisible;
        newTool.OutlineEnabled = source.OutlineEnabled;
        newTool.BackgroundColor = source.BackgroundColor;

        newTool.GridCol = source.GridCol + (offset.X / (source.Size.X / source.GridColSpan));
        newTool.GridRow = source.GridRow + (offset.Y / (source.Size.Y / source.GridRowSpan));
        newTool.GridColSpan = source.GridColSpan;
        newTool.GridRowSpan = source.GridRowSpan;
        newTool.HasGridCoords = source.HasGridCoords;

        if (!string.IsNullOrWhiteSpace(source.CustomTitle))
            newTool.CustomTitle = source.CustomTitle + " (Copy)";

        var toolSettings = source.ExportToolSettings();
        LogService.Debug(LogCategory.UI, $"DuplicateTool: exported {toolSettings?.Count ?? 0} settings from source tool");
        if (toolSettings?.Count > 0)
        {
            newTool.ImportToolSettings(toolSettings);
            LogService.Debug(LogCategory.UI, "DuplicateTool: imported settings to new tool");
        }

        AddToolInstance(newTool);
        LogService.Debug(LogCategory.UI, $"DuplicateTool: duplicated tool id='{source.Id}'");
    }

    /// <summary>Removes and disposes the tool at the given index.</summary>
    internal void RemoveTool(int index)
    {
        if (index < 0 || index >= Tools.Count) return;
        var tool = Tools[index].Tool;
        tool.Dispose();
        Tools.RemoveAt(index);
        MarkLayoutDirty();
    }
}