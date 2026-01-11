using System.Numerics;
using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A widget that displays a character name with a right-clickable area.
/// Provides context menu integration for character-specific actions.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var widget = new CharacterNameWidget();
/// widget.OnHideCharacter += (cid) => HideCharacter(cid);
/// widget.Draw(characterId, characterName);
/// </code>
/// </remarks>
public sealed class CharacterNameWidget
{
    /// <summary>
    /// Event raised when the user selects "Hide Character" from the context menu.
    /// </summary>
    public event Action<ulong>? OnHideCharacter;
    
    /// <summary>
    /// Gets or sets the text color for the character name.
    /// </summary>
    public Vector4? TextColor { get; set; }
    
    /// <summary>
    /// Gets or sets whether to show a background rectangle when hovered.
    /// </summary>
    public bool ShowHoverBackground { get; set; } = true;
    
    /// <summary>
    /// Gets or sets the hover background color.
    /// </summary>
    public Vector4 HoverBackgroundColor { get; set; } = new(0.2f, 0.2f, 0.3f, 0.5f);
    
    /// <summary>
    /// Gets or sets the padding around the text (in pixels).
    /// </summary>
    public Vector2 Padding { get; set; } = new(4f, 2f);
    
    /// <summary>
    /// Draws the character name widget with right-click context menu support.
    /// </summary>
    /// <param name="characterId">The character ID (CID).</param>
    /// <param name="characterName">The character name to display.</param>
    /// <param name="contextId">Optional unique context ID for the popup. If null, uses the character ID.</param>
    public void Draw(ulong characterId, string characterName, string? contextId = null)
    {
        contextId ??= $"CharNameWidget_{characterId}";
        
        // Calculate text size and rectangle bounds
        var textSize = ImGui.CalcTextSize(characterName);
        var rectMin = ImGui.GetCursorScreenPos();
        var rectMax = new Vector2(
            rectMin.X + textSize.X + (Padding.X * 2),
            rectMin.Y + textSize.Y + (Padding.Y * 2));
        
        // Draw hover background if enabled
        var isHovered = ImGui.IsMouseHoveringRect(rectMin, rectMax);
        if (ShowHoverBackground && isHovered)
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(HoverBackgroundColor));
        }
        
        // Position cursor with padding
        var cursorPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(cursorPos.X + Padding.X, cursorPos.Y + Padding.Y));
        
        // Draw the text
        if (TextColor.HasValue)
        {
            ImGui.TextColored(TextColor.Value, characterName);
        }
        else
        {
            ImGui.TextUnformatted(characterName);
        }
        
        // Create an invisible button over the entire area for right-click detection
        ImGui.SetCursorScreenPos(rectMin);
        ImGui.InvisibleButton($"##CharNameBtn_{characterId}", new Vector2(textSize.X + (Padding.X * 2), textSize.Y + (Padding.Y * 2)));
        
        // Right-click context menu
        if (ImGui.BeginPopupContextItem(contextId))
        {
            ImGui.TextDisabled(characterName);
            ImGui.Separator();
            
            if (ImGui.MenuItem("Hide Character"))
            {
                OnHideCharacter?.Invoke(characterId);
            }
            
            ImGui.EndPopup();
        }
        
        // Restore cursor position after the widget (move to next line)
        ImGui.SetCursorScreenPos(new Vector2(rectMin.X, rectMax.Y));
    }
    
    /// <summary>
    /// Draws the character name widget inline (on the same line) with right-click context menu support.
    /// Call ImGui.SameLine() before this if you want it on the same line as previous content.
    /// </summary>
    /// <param name="characterId">The character ID (CID).</param>
    /// <param name="characterName">The character name to display.</param>
    /// <param name="contextId">Optional unique context ID for the popup. If null, uses the character ID.</param>
    public void DrawInline(ulong characterId, string characterName, string? contextId = null)
    {
        contextId ??= $"CharNameWidget_{characterId}";
        
        // Calculate text size and rectangle bounds
        var textSize = ImGui.CalcTextSize(characterName);
        var rectMin = ImGui.GetCursorScreenPos();
        var rectMax = new Vector2(
            rectMin.X + textSize.X + (Padding.X * 2),
            rectMin.Y + textSize.Y + (Padding.Y * 2));
        
        // Draw hover background if enabled
        var isHovered = ImGui.IsMouseHoveringRect(rectMin, rectMax);
        if (ShowHoverBackground && isHovered)
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(rectMin, rectMax, ImGui.GetColorU32(HoverBackgroundColor));
        }
        
        // Position cursor with padding
        var cursorPos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(cursorPos.X + Padding.X, cursorPos.Y + Padding.Y));
        
        // Draw the text
        if (TextColor.HasValue)
        {
            ImGui.TextColored(TextColor.Value, characterName);
        }
        else
        {
            ImGui.TextUnformatted(characterName);
        }
        
        // Create an invisible button over the entire area for right-click detection
        ImGui.SetCursorScreenPos(rectMin);
        ImGui.InvisibleButton($"##CharNameBtn_{characterId}", new Vector2(textSize.X + (Padding.X * 2), textSize.Y + (Padding.Y * 2)));
        
        // Right-click context menu
        if (ImGui.BeginPopupContextItem(contextId))
        {
            ImGui.TextDisabled(characterName);
            ImGui.Separator();
            
            if (ImGui.MenuItem("Hide Character"))
            {
                OnHideCharacter?.Invoke(characterId);
            }
            
            ImGui.EndPopup();
        }
        
        // Restore cursor position to continue on the same line
        ImGui.SetCursorScreenPos(new Vector2(rectMax.X, rectMin.Y));
    }
}
