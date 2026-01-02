using Dalamud.Bindings.ImGui;
using Kaleidoscope.Interfaces;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A reusable widget for managing hidden items (characters, entities, etc.) with:
/// - Context menu integration for hiding items
/// - Settings UI section for viewing and unhiding items
/// - Settings persistence via ISettingsProvider
/// </summary>
/// <typeparam name="TId">The type of the identifier (e.g., ulong for character CIDs, string for entity keys)</typeparam>
/// <remarks>
/// Usage:
/// <code>
/// // Create the manager
/// private readonly HiddenItemsManager&lt;ulong&gt; _hiddenCharacters = new("Characters", "Character");
/// 
/// // In rendering, check if hidden
/// if (_hiddenCharacters.IsHidden(characterCid)) continue;
/// 
/// // Add context menu to items
/// _hiddenCharacters.DrawContextMenuItem(characterCid, GetCharacterName);
/// 
/// // In settings, draw the management UI
/// _hiddenCharacters.DrawSettingsSection(GetCharacterName);
/// 
/// // Register as settings provider in tool constructor
/// RegisterSettingsProvider(_hiddenCharacters);
/// </code>
/// </remarks>
public sealed class HiddenItemsManager<TId> : ISettingsProvider where TId : notnull
{
    private readonly string _pluralName;
    private readonly string _singularName;
    private readonly HashSet<TId> _hiddenItems = new();
    
    /// <summary>
    /// Event raised when the hidden items collection changes.
    /// </summary>
    public event Action? OnChanged;
    
    /// <summary>
    /// Gets or sets the color used for disabled/informational text.
    /// </summary>
    public Vector4 DisabledTextColor { get; set; } = new(0.5f, 0.5f, 0.5f, 1f);
    
    /// <summary>
    /// Creates a new HiddenItemsManager.
    /// </summary>
    /// <param name="pluralName">The plural name for display (e.g., "Characters", "Retainers")</param>
    /// <param name="singularName">The singular name for display (e.g., "Character", "Retainer")</param>
    public HiddenItemsManager(string pluralName, string singularName)
    {
        _pluralName = pluralName;
        _singularName = singularName;
    }
    
    /// <summary>
    /// Gets the set of hidden item IDs. Modify this directly for import/export.
    /// </summary>
    public HashSet<TId> HiddenItems => _hiddenItems;
    
    /// <summary>
    /// Gets the number of hidden items.
    /// </summary>
    public int Count => _hiddenItems.Count;
    
    /// <summary>
    /// Checks if an item is hidden.
    /// </summary>
    public bool IsHidden(TId id) => _hiddenItems.Contains(id);
    
    /// <summary>
    /// Hides an item and raises the OnChanged event.
    /// </summary>
    public void Hide(TId id)
    {
        if (_hiddenItems.Add(id))
            OnChanged?.Invoke();
    }
    
    /// <summary>
    /// Unhides an item and raises the OnChanged event.
    /// </summary>
    public void Unhide(TId id)
    {
        if (_hiddenItems.Remove(id))
            OnChanged?.Invoke();
    }
    
    /// <summary>
    /// Clears all hidden items and raises the OnChanged event.
    /// </summary>
    public void Clear()
    {
        if (_hiddenItems.Count > 0)
        {
            _hiddenItems.Clear();
            OnChanged?.Invoke();
        }
    }
    
    /// <summary>
    /// Replaces all hidden items (for import). Raises OnChanged.
    /// </summary>
    public void SetAll(IEnumerable<TId> items)
    {
        _hiddenItems.Clear();
        foreach (var item in items)
            _hiddenItems.Add(item);
        OnChanged?.Invoke();
    }
    
    /// <summary>
    /// Draws a context menu item for hiding the specified item.
    /// Call this inside a BeginPopupContextItem block, or it will create one.
    /// </summary>
    /// <param name="id">The item ID to hide when clicked</param>
    /// <param name="contextId">Unique ID for the context menu popup</param>
    public void DrawContextMenuPopup(TId id, string contextId)
    {
        if (ImGui.BeginPopupContextItem(contextId))
        {
            if (ImGui.MenuItem($"Hide {_singularName}"))
            {
                Hide(id);
            }
            ImGui.EndPopup();
        }
    }
    
    /// <summary>
    /// Draws just the menu item (assumes you're already in a popup context).
    /// Returns true if the item was clicked.
    /// </summary>
    public bool DrawHideMenuItem(TId id)
    {
        if (ImGui.MenuItem($"Hide {_singularName}"))
        {
            Hide(id);
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Draws the settings section for managing hidden items.
    /// Includes "Unhide All" button and individual unhide buttons.
    /// </summary>
    /// <param name="getDisplayName">Function to get display name for an ID</param>
    /// <param name="useCollapsingHeader">If true, wraps content in a collapsing header</param>
    public void DrawSettingsSection(Func<TId, string> getDisplayName, bool useCollapsingHeader = true)
    {
        if (useCollapsingHeader)
        {
            if (!ImGui.CollapsingHeader($"Hidden {_pluralName}"))
                return;
            ImGui.Indent();
        }
        
        try
        {
            DrawSettingsContent(getDisplayName);
        }
        finally
        {
            if (useCollapsingHeader)
                ImGui.Unindent();
        }
    }
    
    /// <summary>
    /// Draws the settings content without any wrapper (for custom layout).
    /// </summary>
    public void DrawSettingsContent(Func<TId, string> getDisplayName)
    {
        if (_hiddenItems.Count == 0)
        {
            ImGui.TextColored(DisabledTextColor, $"No hidden {_pluralName.ToLowerInvariant()}");
            ImGui.TextColored(DisabledTextColor, $"Right-click a {_singularName.ToLowerInvariant()} to hide it.");
            return;
        }
        
        if (ImGui.Button($"Unhide All##{_pluralName}"))
        {
            Clear();
        }
        ImGui.Spacing();
        
        TId? itemToUnhide = default;
        bool hasItemToUnhide = false;
        
        foreach (var id in _hiddenItems)
        {
            var displayName = getDisplayName(id);
            ImGui.TextUnformatted(displayName);
            ImGui.SameLine();
            ImGui.PushID(id.GetHashCode());
            if (ImGui.SmallButton("Unhide"))
            {
                itemToUnhide = id;
                hasItemToUnhide = true;
            }
            ImGui.PopID();
        }
        
        if (hasItemToUnhide && itemToUnhide != null)
        {
            Unhide(itemToUnhide);
        }
    }
    
    #region ISettingsProvider Implementation
    
    bool ISettingsProvider.HasSettings => _hiddenItems.Count > 0;
    
    string ISettingsProvider.SettingsName => $"Hidden {_pluralName}";
    
    bool ISettingsProvider.DrawSettings()
    {
        var countBefore = _hiddenItems.Count;
        DrawSettingsContent(id => id.ToString() ?? "Unknown");
        return _hiddenItems.Count != countBefore;
    }
    
    #endregion
    
    #region Serialization Helpers
    
    /// <summary>
    /// Exports the hidden items to a list for serialization.
    /// </summary>
    public List<TId> ToList() => _hiddenItems.ToList();
    
    /// <summary>
    /// Imports hidden items from a list.
    /// </summary>
    public void FromList(List<TId>? list)
    {
        if (list == null) return;
        SetAll(list);
    }
    
    #endregion
}
