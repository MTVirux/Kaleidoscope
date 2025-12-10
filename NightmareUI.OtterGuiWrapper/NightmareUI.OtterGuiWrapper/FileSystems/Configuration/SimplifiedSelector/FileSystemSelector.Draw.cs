using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using OtterGui;
using OtterGui.Classes;
using OtterGui.Filesystem;
using OtterGui.Raii;
using OtterGui.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NightmareUI.OtterGuiWrapper.FileSystems.Configuration.SimplifiedSelector;

public partial class FileSystemSelector<T, TStateStorage>
{
    private ImGuiStoragePtr _stateStorage;
    private int             _currentDepth;
    private int             _currentIndex;
    private int             _currentEnd;
    private DateTimeOffset  _lastButtonTime = DateTimeOffset.UtcNow;

    private (Vector2, Vector2) DrawStateStruct(StateStruct state)
    {
        return state.Path switch
        {
            FileSystem<T>.Folder f => DrawFolder(f),
            FileSystem<T>.Leaf l   => DrawLeaf(l, state.StateStorage),
            _                      => (Vector2.Zero, Vector2.Zero),
        };
    }

    // Draw a leaf. Returns its item rectangle and manages
    //     - drag'n drop,
    //     - right-click context menu,
    //     - selection.
    private (Vector2, Vector2) DrawLeaf(FileSystem<T>.Leaf leaf, in TStateStorage state)
    {
        DrawLeafName(leaf, state, leaf == SelectedLeaf || SelectedPaths.Contains(leaf));
        if(ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            Select(leaf, state, ImGui.GetIO().KeyCtrl, ImGui.GetIO().KeyShift);

        return (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    // Used for clipping. If we start with an object not on depth 0,
    // we need to add its indentation and the folder-lines for it.
    private void DrawPseudoFolders()
    {
        var first   = _state[_currentIndex]; // The first object drawn during this iteration
        var parents = first.Path.Parents();
        // Push IDs in order and indent.
        ImGui.Indent(ImGui.GetStyle().IndentSpacing * parents.Length);

        // Get start point for the lines (top of the selector).
        var lineStart = ImGui.GetCursorScreenPos();

        // For each pseudo-parent in reverse order draw its children as usual, starting from _currentIndex.
        for (_currentDepth = parents.Length; _currentDepth > 0; --_currentDepth)
        {
            DrawChildren(lineStart);
            lineStart.X -= ImGui.GetStyle().IndentSpacing;
            ImGui.Unindent();
        }
    }

    // Used for clipping. If we end not on depth 0 we need to check
    // whether to terminate the folder lines at that point or continue them to the end of the screen.
    private Vector2 AdjustedLineEnd(Vector2 lineEnd)
    {
        if (_currentIndex != _currentEnd)
            return lineEnd;

        var y = ImGui.GetWindowHeight() + ImGui.GetWindowPos().Y;
        if (y > lineEnd.Y + ImGui.GetTextLineHeight())
            return lineEnd;

        // Continue iterating from the current end.
        for (var idx = _currentEnd; idx < _state.Count; ++idx)
        {
            var state = _state[idx];

            // If we find an object at the same depth, the current folder continues
            // and the line has to go out of the screen.
            if (state.Depth == _currentDepth)
                return lineEnd with { Y = y };

            // If we find an object at a lower depth before reaching current depth,
            // the current folder stops and the line should stop at the last drawn child, too.
            if (state.Depth < _currentDepth)
                return lineEnd;
        }

        // All children are in subfolders of this one, but this folder has no further children on its own.
        return lineEnd;
    }

    // Draw children of a folder or pseudo-folder with a given line start using the current index and end.
    private void DrawChildren(Vector2 lineStart)
    {
        // Folder line stuff.
        var offsetX  = -ImGui.GetStyle().IndentSpacing + ImGui.GetTreeNodeToLabelSpacing() / 2;
        var drawList = ImGui.GetWindowDrawList();
        lineStart.X += offsetX;
        lineStart.Y -= 2 * ImGuiHelpers.GlobalScale;
        var lineEnd = lineStart;

        for (; _currentIndex < _currentEnd; ++_currentIndex)
        {
            // If we leave _currentDepth, its not a child of the current folder anymore.
            var state = _state[_currentIndex];
            if (state.Depth != _currentDepth)
                break;

            var lineSize = Math.Max(0, ImGui.GetStyle().IndentSpacing - 9 * ImGuiHelpers.GlobalScale);
            // Draw the child
            var (minRect, maxRect) = DrawStateStruct(state);
            if (minRect.X == 0)
                continue;

            // Draw the notch and increase the line length.
            var midPoint = (minRect.Y + maxRect.Y) / 2f - 1f;
            drawList.AddLine(lineStart with { Y = midPoint }, new Vector2(lineStart.X + lineSize, midPoint), FolderLineColor,
                ImGuiHelpers.GlobalScale);
            lineEnd.Y = midPoint;
        }

        // Finally, draw the folder line.
        drawList.AddLine(lineStart, AdjustedLineEnd(lineEnd), FolderLineColor, ImGuiHelpers.GlobalScale);
    }

    private List<uint> OpenedFolders = [];

    // Draw a folder. Handles
    //     - drag'n drop
    //     - right-click context menus
    //     - expanding/collapsing
    private (Vector2, Vector2) DrawFolder(FileSystem<T>.Folder folder)
    {
        var flags = ImGuiTreeNodeFlags.NoTreePushOnOpen | (FoldersDefaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        if (SelectedPaths.Contains(folder))
            flags |= ImGuiTreeNodeFlags.Selected;
        flags |= ImGuiTreeNodeFlags.SpanFullWidth;
        var       expandedState = GetPathState(folder);
        using var color         = ImRaii.PushColor(ImGuiCol.Text, expandedState ? ExpandedFolderColor : CollapsedFolderColor);
        if (!OpenedFolders.Contains(folder.Identifier))
        {
            OpenedFolders.Add(folder.Identifier);
            ImGui.SetNextItemOpen(true);
				}
        var recurse = ImUtf8.TreeNode((nint)folder.Identifier, folder.Name.Replace("%", "%%"), flags);

        if (expandedState != recurse)
            AddOrRemoveDescendants(folder, recurse);

        color.Pop();

        if (AllowMultipleSelection && ImGui.IsItemClicked(ImGuiMouseButton.Left) && ImGui.GetIO().KeyCtrl)
            Select(folder, default, true, false);

        var rect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        // If the folder is expanded, draw its children one tier deeper.
        if (!recurse)
            return rect;

        ++_currentDepth;
        ++_currentIndex;
        ImGui.Indent();
        DrawChildren(ImGui.GetCursorScreenPos());
        ImGui.Unindent();
        --_currentIndex;
        --_currentDepth;

        return rect;
    }

    public bool DrawFilter = true;

    // Draw the whole list.
    private bool DrawList(float width)
    {
        // Filter row is outside the child for scrolling.
        if(DrawFilter) DrawFilterRow(width);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var _     = ImRaii.Child(Label, new Vector2(width, 0), true);
        style.Pop();
        if (!_)
            return false;

        ImGui.SetScrollX(0);
        _stateStorage = ImGui.GetStateStorage();
        style.Push(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale)
            .Push(ImGuiStyleVar.ItemSpacing,  new Vector2(ImGui.GetStyle().ItemSpacing.X, ImGuiHelpers.GlobalScale))
            .Push(ImGuiStyleVar.FramePadding, new Vector2(ImGuiHelpers.GlobalScale,       ImGui.GetStyle().FramePadding.Y));
        //// Check if filters are dirty and recompute them before the draw iteration if necessary.
        ApplyFilters();
        if (_jumpToSelection != null)
        {
            var idx = _state.FindIndex(s => s.Path == _jumpToSelection);
            if (idx >= 0)
                ImGui.SetScrollFromPosY(ImGui.GetTextLineHeightWithSpacing() * idx - ImGui.GetScrollY());

            _jumpToSelection = null;
        }

        ImGuiListClipperPtr clipper;
        unsafe
        {
            clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper());
        }

        // TODO: do this right.
        //HandleKeyNavigation();
        clipper.Begin(_state.Count, ImGui.GetTextLineHeightWithSpacing());
        // Draw the clipped list.

        while (clipper.Step())
        {
            _currentIndex = clipper.DisplayStart;
            _currentEnd   = Math.Min(_state.Count, clipper.DisplayEnd);
            if (_currentIndex >= _currentEnd)
                continue;

            if (_state[_currentIndex].Depth != 0)
                DrawPseudoFolders();
            _currentEnd = Math.Min(_state.Count, _currentEnd);
            for (; _currentIndex < _currentEnd; ++_currentIndex)
                DrawStateStruct(_state[_currentIndex]);
        }

        clipper.End();
        clipper.Destroy();

        //// Handle all queued actions at the end of the iteration.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        HandleActions();
        style.Push(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        return true;
    }
}
