using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using System.Text.Json;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A collapsible widget for displaying and editing a single layout entry.
/// </summary>
public class LayoutItemWidget
{
    private readonly ConfigurationService _configService;
    private readonly ContentLayoutState _layout;
    private readonly Action _onDelete;
    private readonly Action _onSetActive;
    private readonly Func<bool> _isActive;

    private string _renameBuffer;

    public LayoutItemWidget(
            ConfigurationService configService,
            ContentLayoutState layout,
            Func<bool> isActive,
            Action onSetActive,
            Action onDelete)
        {
            _configService = configService;
            _layout = layout;
            _isActive = isActive;
            _onSetActive = onSetActive;
            _onDelete = onDelete;
            _renameBuffer = layout.Name;
        }

        /// <summary>
        /// Draws the layout item widget. Returns true if the layout was deleted.
        /// </summary>
        public bool Draw()
        {
            var deleted = false;
            var isActive = _isActive();
            
            // Build header label
            var headerLabel = _layout.Name;
            if (isActive)
            {
                headerLabel = $"{_layout.Name} [Active]";
            }

            // Use a unique ID for this layout
            ImGui.PushID($"layout_{_layout.Name}_{_layout.Type}");
            
            try
            {
                // Draw the "Set Active" button before the collapsible header
                var buttonSize = new Vector2(24, 24);
                
                // Star/check icon for active state
                if (isActive)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.7f, 0.3f, 1f));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 1f));
                }
                
                var icon = isActive ? FontAwesomeIcon.Check : FontAwesomeIcon.Star;
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button(icon.ToIconString(), buttonSize))
                {
                    if (!isActive)
                    {
                        _onSetActive();
                    }
                }
                ImGui.PopFont();
                ImGui.PopStyleColor(2);
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(isActive ? "Currently active" : "Set as active layout");
                }
                
                ImGui.SameLine();
                
                // Collapsible header
                var headerFlags = ImGuiTreeNodeFlags.None;
                var headerOpen = ImGui.CollapsingHeader(headerLabel, headerFlags);
                
                if (headerOpen)
                {
                    ImGui.Indent();
                    
                    // Rename section
                    ImGui.TextUnformatted("Name:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200);
                    if (ImGui.InputText("##rename", ref _renameBuffer, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        ApplyRename();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Apply"))
                    {
                        ApplyRename();
                    }
                    
                    ImGui.Spacing();
                    
                    // Copy to clipboard
                    if (ImGui.Button("Copy to Clipboard"))
                    {
                        try
                        {
                            var json = JsonSerializer.Serialize(_layout, new JsonSerializerOptions { WriteIndented = true });
                            ImGui.SetClipboardText(json);
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug($"[LayoutItemWidget] Export failed: {ex.Message}");
                        }
                    }
                    
                    ImGui.SameLine();
                    
                    // Delete button (with confirmation via double-click or shift+click)
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.3f, 0.3f, 1f));
                    if (ImGui.Button("Delete"))
                    {
                        var io = ImGui.GetIO();
                        if (io.KeyShift)
                        {
                            _onDelete();
                            deleted = true;
                        }
                        else
                        {
                            ImGui.OpenPopup("confirm_delete");
                        }
                    }
                    ImGui.PopStyleColor(2);
                    
                    if (ImGui.IsItemHovered() && !deleted)
                    {
                        ImGui.SetTooltip("Hold Shift and click to delete immediately");
                    }
                    
                    // Delete confirmation popup
                    if (ImGui.BeginPopup("confirm_delete"))
                    {
                        ImGui.TextUnformatted($"Delete layout '{_layout.Name}'?");
                        if (ImGui.Button("Yes, Delete"))
                        {
                            _onDelete();
                            deleted = true;
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Cancel"))
                        {
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.EndPopup();
                    }
                    
                    // Layout info
                    ImGui.Spacing();
                    ImGui.TextDisabled($"Tools: {_layout.Tools?.Count ?? 0}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"| Grid: {_layout.Columns}x{_layout.Rows}");
                    
                    ImGui.Unindent();
                }
        }
        finally
        {
            ImGui.PopID();
        }

        return deleted;
    }

    private void ApplyRename()
    {
        if (!string.IsNullOrWhiteSpace(_renameBuffer) && _renameBuffer != _layout.Name)
        {
            // Update active layout name references if this was active
            if (string.Equals(_configService.Config.ActiveWindowedLayoutName, _layout.Name, StringComparison.OrdinalIgnoreCase))
            {
                _configService.Config.ActiveWindowedLayoutName = _renameBuffer;
            }
            if (string.Equals(_configService.Config.ActiveFullscreenLayoutName, _layout.Name, StringComparison.OrdinalIgnoreCase))
            {
                _configService.Config.ActiveFullscreenLayoutName = _renameBuffer;
            }

            _layout.Name = _renameBuffer;
            _configService.Save();
        }
    }
}
