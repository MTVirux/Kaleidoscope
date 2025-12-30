using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using MTGui.Tree;
using System.Numerics;
using Kaleidoscope.Gui.MainWindow;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Tool Presets management category in the config window.
/// Allows viewing, editing, and deleting user-created tool presets.
/// </summary>
public sealed class ToolPresetsCategory
{
    private readonly ConfigurationService _configService;

    private Configuration Config => _configService.Config;

    // State for editing preset names
    private string? _editingPresetId;
    private string _editingName = string.Empty;
    private string _editingDescription = string.Empty;

    // Filter state
    private string _filterText = string.Empty;
    private string _filterToolType = string.Empty;

    public ToolPresetsCategory(ConfigurationService configService)
    {
        _configService = configService;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Tool Presets");
        ImGui.Separator();
        
        ImGui.TextWrapped("Tool presets save the configuration of a tool for quick reuse. " +
                          "Create presets from any tool's settings menu using 'Save as Preset'.");
        ImGui.Spacing();

        var presets = Config.UserToolPresets ?? new List<UserToolPreset>();

        if (presets.Count == 0)
        {
            ImGui.TextDisabled("No user presets. Create one from a tool's settings menu.");
            return;
        }

        // Filter controls
        ImGui.SetNextItemWidth(200f);
        ImGui.InputTextWithHint("##filter", "Filter by name...", ref _filterText, 256);
        
        ImGui.SameLine();
        
        // Tool type filter dropdown
        var toolTypes = presets.Select(p => p.ToolType).Distinct().OrderBy(t => t).ToList();
        if (toolTypes.Count > 1)
        {
            ImGui.SetNextItemWidth(150f);
            if (ImGui.BeginCombo("##toolTypeFilter", string.IsNullOrEmpty(_filterToolType) ? "All Types" : GetToolDisplayName(_filterToolType)))
            {
                if (ImGui.Selectable("All Types", string.IsNullOrEmpty(_filterToolType)))
                {
                    _filterToolType = string.Empty;
                }
                foreach (var toolType in toolTypes)
                {
                    if (ImGui.Selectable(GetToolDisplayName(toolType), _filterToolType == toolType))
                    {
                        _filterToolType = toolType;
                    }
                }
                ImGui.EndCombo();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Filter presets
        var filteredPresets = presets
            .Where(p => string.IsNullOrEmpty(_filterText) || 
                        p.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
            .Where(p => string.IsNullOrEmpty(_filterToolType) || p.ToolType == _filterToolType)
            .OrderBy(p => p.ToolType)
            .ThenBy(p => p.Name)
            .ToList();

        if (filteredPresets.Count == 0)
        {
            ImGui.TextDisabled("No presets match the filter.");
            return;
        }

        // Group by tool type
        var groupedPresets = filteredPresets.GroupBy(p => p.ToolType);

        string? presetToDelete = null;

        foreach (var group in groupedPresets)
        {
            var toolDisplayName = GetToolDisplayName(group.Key);
            
            if (MTTreeHelpers.DrawCollapsingSection($"{toolDisplayName} ({group.Count()})", true, group.Key))
            {
                foreach (var preset in group)
                {
                    DrawPresetItem(preset, ref presetToDelete);
                }
            }
        }

        // Handle deletion
        if (presetToDelete != null)
        {
            var toRemove = presets.FirstOrDefault(p => p.Id == presetToDelete);
            if (toRemove != null)
            {
                presets.Remove(toRemove);
                _configService.Save();
            }
        }
    }

    private void DrawPresetItem(UserToolPreset preset, ref string? presetToDelete)
    {
        var isEditing = _editingPresetId == preset.Id;
        
        ImGui.PushID(preset.Id);
        
        // Preset name (editable)
        if (isEditing)
        {
            ImGui.SetNextItemWidth(200f);
            if (ImGui.InputText("##name", ref _editingName, 256, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                SaveEditing(preset);
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Save"))
            {
                SaveEditing(preset);
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel"))
            {
                CancelEditing();
            }
            
            // Description field when editing
            ImGui.SetNextItemWidth(400f);
            ImGui.InputTextWithHint("##desc", "Description (optional)", ref _editingDescription, 512);
        }
        else
        {
            // Display mode
            ImGui.TextUnformatted(preset.Name);
            
            if (!string.IsNullOrEmpty(preset.Description))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"- {preset.Description}");
            }
            
            ImGui.SameLine();
            
            // Edit button
            if (ImGui.SmallButton("Edit"))
            {
                StartEditing(preset);
            }
            
            ImGui.SameLine();
            
            // Delete button with confirmation
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.3f, 0.3f, 1f));
            if (ImGui.SmallButton("Delete"))
            {
                ImGui.OpenPopup("ConfirmDelete");
            }
            ImGui.PopStyleColor(2);
            
            // Confirmation popup
            if (ImGui.BeginPopup("ConfirmDelete"))
            {
                ImGui.TextUnformatted($"Delete preset '{preset.Name}'?");
                ImGui.Separator();
                
                if (ImGui.Button("Yes, Delete"))
                {
                    presetToDelete = preset.Id;
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.SameLine();
                
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.EndPopup();
            }
            
            // Show creation date on hover
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Created: {preset.CreatedAt:g}\nModified: {preset.ModifiedAt:g}");
            }
        }
        
        ImGui.PopID();
    }

    private void StartEditing(UserToolPreset preset)
    {
        _editingPresetId = preset.Id;
        _editingName = preset.Name;
        _editingDescription = preset.Description;
    }

    private void SaveEditing(UserToolPreset preset)
    {
        if (!string.IsNullOrWhiteSpace(_editingName))
        {
            preset.Name = _editingName.Trim();
            preset.Description = _editingDescription?.Trim() ?? string.Empty;
            preset.ModifiedAt = DateTime.UtcNow;
            _configService.Save();
        }
        CancelEditing();
    }

    private void CancelEditing()
    {
        _editingPresetId = null;
        _editingName = string.Empty;
        _editingDescription = string.Empty;
    }

    /// <summary>
    /// Gets a user-friendly display name for a tool type ID.
    /// </summary>
    private static string GetToolDisplayName(string toolType)
    {
        return toolType switch
        {
            WindowToolRegistrar.ToolIds.DataGraph => "Data Graph",
            WindowToolRegistrar.ToolIds.DataTable => "Data Table",
            _ => toolType
        };
    }
}
