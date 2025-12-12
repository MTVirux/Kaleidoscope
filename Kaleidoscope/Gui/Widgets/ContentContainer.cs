using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using OtterGui.Raii;

namespace Kaleidoscope.Gui.Widgets;

public static class ContentContainer
{
    /// <summary>
    /// Begins a child region that covers the current window's content area with a margin from the content edges.
    /// Use as: `using var c = ContentContainer.Begin(10f); /* draw inside */`.
    /// </summary>
    /// <param name="margin">Margin in pixels from each window edge.</param>
    /// <param name="id">ImGui id for the child region.</param>
    /// <returns>An RAII end-object that ends the child on Dispose.</returns>
    public static ImRaii.IEndObject Begin(float margin = 10f, string id = "##ContentContainer")
    {
        // Use the content region to avoid overlapping titlebars, headers and window padding.
        var winPos = ImGui.GetWindowPos();
        var contentMin = ImGui.GetWindowContentRegionMin();
        var contentMax = ImGui.GetWindowContentRegionMax();

        var contentPos = winPos + contentMin;
        var contentSize = contentMax - contentMin;

        var pos = contentPos + new Vector2(margin, margin);
        var size = new Vector2(Math.Max(0f, contentSize.X - 2 * margin), Math.Max(0f, contentSize.Y - 2 * margin));

        // Add a small inner padding so child contents are inset from the outline.
        const float padding = 5f;
        var innerPos = pos + new Vector2(padding, padding);
        var innerSize = new Vector2(Math.Max(0f, size.X - 2 * padding), Math.Max(0f, size.Y - 2 * padding));

        // Position the cursor at the inset position for the child region.
        ImGui.SetCursorScreenPos(innerPos);

        // Begin the child region with scrolling disabled so content does not scroll.
        // Use NoScrollbar and NoScrollWithMouse to prevent both visible scrollbars
        // and mouse-driven scrolling inside the container.
        var child = ImRaii.Child(id, innerSize, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        return new ContentContainerEnd(child, pos, size);
    }
}

// RAII end-object that wraps the child end-object and draws an outline
// around the previously reserved child rectangle after the child is ended.
internal sealed class ContentContainerEnd : ImRaii.IEndObject
{
    private readonly ImRaii.IEndObject _childEnd;
    private readonly Vector2 _pos;
    private readonly Vector2 _size;
    private bool _disposed = false;

    public ContentContainerEnd(ImRaii.IEndObject childEnd, Vector2 pos, Vector2 size)
    {
        _childEnd = childEnd;
        _pos = pos;
        _size = size;
    }

    public bool Success => _childEnd != null && _childEnd.Success;

    public void Dispose()
    {
        if (_disposed) return;

        // End the child first so the outline is drawn on top of its contents.
        try { _childEnd.Dispose(); } catch { }

            try
            {
                var drawList = ImGui.GetWindowDrawList();
                var color = ImGui.GetColorU32(ImGuiCol.Border);
                // Draw with zero rounding to ensure square corners.
                var rounding = 0f;
                var thickness = Math.Max(1f, ImGui.GetStyle().ChildBorderSize);
                drawList.AddRect(_pos, _pos + _size, color, rounding, ImDrawFlags.RoundCornersAll, thickness);
            }
        catch { }

        _disposed = true;
    }
}
