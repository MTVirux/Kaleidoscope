namespace Kaleidoscope.Gui.Widgets.Tree;

/// <summary>
/// Configuration for tree node rendering.
/// </summary>
/// <param name="DefaultOpen">Whether the node is expanded by default when first rendered.</param>
/// <param name="OpenOnLabelClick">Whether clicking the label opens the node (vs only the arrow).</param>
/// <param name="IsLeaf">Whether this node is a leaf (cannot be expanded).</param>
/// <param name="SpanFullWidth">Whether the node spans the full available width.</param>
/// <param name="ShowBulletForLeaf">Whether to show a bullet instead of an arrow for nodes without children.</param>
/// <param name="IndentSize">Indent size in pixels for child nodes.</param>
public sealed record TreeNodeConfig(
    bool DefaultOpen = false,
    bool OpenOnLabelClick = true,
    bool IsLeaf = false,
    bool SpanFullWidth = true,
    bool ShowBulletForLeaf = false,
    float IndentSize = 16f);

/// <summary>
/// Represents a node in a hierarchical tree structure.
/// </summary>
/// <typeparam name="TKey">The type of key that uniquely identifies the node.</typeparam>
/// <typeparam name="TData">The type of data stored in the node.</typeparam>
public class TreeNode<TKey, TData> where TKey : notnull
{
    /// <summary>
    /// Unique identifier for this node.
    /// </summary>
    public required TKey Key { get; init; }
    
    /// <summary>
    /// Display label for the node.
    /// </summary>
    public required string Label { get; init; }
    
    /// <summary>
    /// Optional data associated with this node.
    /// </summary>
    public TData? Data { get; init; }
    
    /// <summary>
    /// Child nodes.
    /// </summary>
    public List<TreeNode<TKey, TData>> Children { get; init; } = [];
    
    /// <summary>
    /// Whether this node has any children.
    /// </summary>
    public bool HasChildren => Children.Count > 0;
    
    /// <summary>
    /// Optional icon to display before the label.
    /// </summary>
    public string? Icon { get; init; }
    
    /// <summary>
    /// Optional color for the label text.
    /// </summary>
    public Vector4? LabelColor { get; init; }
}

/// <summary>
/// State for tracking expanded nodes in a tree.
/// </summary>
/// <typeparam name="TKey">The type of key that uniquely identifies nodes.</typeparam>
public class TreeExpansionState<TKey> where TKey : notnull
{
    private readonly HashSet<TKey> _expandedNodes = [];
    
    /// <summary>
    /// Event fired when expansion state changes.
    /// </summary>
    public event Action<TKey, bool>? OnExpansionChanged;
    
    /// <summary>
    /// Checks if a node is expanded.
    /// </summary>
    public bool IsExpanded(TKey key) => _expandedNodes.Contains(key);
    
    /// <summary>
    /// Sets the expansion state of a node.
    /// </summary>
    public void SetExpanded(TKey key, bool expanded)
    {
        var wasExpanded = _expandedNodes.Contains(key);
        if (expanded)
            _expandedNodes.Add(key);
        else
            _expandedNodes.Remove(key);
        
        if (wasExpanded != expanded)
            OnExpansionChanged?.Invoke(key, expanded);
    }
    
    /// <summary>
    /// Toggles the expansion state of a node.
    /// </summary>
    public void Toggle(TKey key)
    {
        SetExpanded(key, !IsExpanded(key));
    }
    
    /// <summary>
    /// Expands all nodes in the provided collection.
    /// </summary>
    public void ExpandAll(IEnumerable<TKey> keys)
    {
        foreach (var key in keys)
            SetExpanded(key, true);
    }
    
    /// <summary>
    /// Collapses all nodes.
    /// </summary>
    public void CollapseAll()
    {
        var keys = _expandedNodes.ToList();
        _expandedNodes.Clear();
        foreach (var key in keys)
            OnExpansionChanged?.Invoke(key, false);
    }
    
    /// <summary>
    /// Gets all currently expanded keys.
    /// </summary>
    public IReadOnlySet<TKey> ExpandedKeys => _expandedNodes;
}
