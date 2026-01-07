using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;

namespace Kaleidoscope.Gui.MainWindow;

public partial class WindowContentContainer
{

        /// <summary>
        /// Draws the tool rename modal if one is currently open.
        /// </summary>
        private void DrawToolRenameModal()
        {
            if (_renameToolIndex < 0 || _renameToolIndex >= _tools.Count)
                return;

            const string popupName = "tool_rename_popup";
            var toolToRename = _tools[_renameToolIndex].Tool;

            // The popup must be opened each frame until it appears
            if (_renamePopupOpen && !ImGui.IsPopupOpen(popupName))
            {
                ImGui.OpenPopup(popupName);
            }

            if (!ImGui.BeginPopupModal(popupName, ref _renamePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                // Modal not showing - if user closed it, reset state
                if (!_renamePopupOpen)
                {
                    _renameToolIndex = -1;
                }
                return;
            }

            try
            {
                ImGui.TextUnformatted("Rename Tool");
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextUnformatted("Enter a new name for this tool:");
                ImGui.InputText("##renameinput", ref _renameBuffer, ConfigStatic.TextInputBufferSize);
                
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"Original name: {toolToRename.Title}");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGuiHelpers.ButtonAutoWidth("OK"))
                {
                    var trimmed = _renameBuffer?.Trim();
                    // If the name is empty or matches the original title, clear the custom title
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed == toolToRename.Title)
                    {
                        toolToRename.CustomTitle = null;
                    }
                    else
                    {
                        toolToRename.CustomTitle = trimmed;
                    }
                    MarkLayoutDirty();
                    ImGui.CloseCurrentPopup();
                    _renamePopupOpen = false;
                }
                ImGui.SameLine();
                if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                    _renamePopupOpen = false;
                }
                ImGui.SameLine();
                if (ImGuiHelpers.ButtonAutoWidth("Reset"))
                {
                    toolToRename.CustomTitle = null;
                    MarkLayoutDirty();
                    ImGui.CloseCurrentPopup();
                    _renamePopupOpen = false;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Reset to the original name");
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.UI, "Error in tool rename modal", ex);
            }

            ImGui.EndPopup();

            if (!_renamePopupOpen)
            {
                _renameToolIndex = -1;
            }
        }

        /// <summary>
        /// Draws the save as preset modal if one is currently open.
        /// </summary>
        private void DrawSavePresetModal()
        {
            if (_savePresetToolIndex < 0 || _savePresetToolIndex >= _tools.Count)
                return;

            const string popupName = "save_preset_popup";
            var toolToSave = _tools[_savePresetToolIndex].Tool;

            // The popup must be opened each frame until it appears
            if (_savePresetPopupOpen && !ImGui.IsPopupOpen(popupName))
            {
                ImGui.OpenPopup(popupName);
            }

            if (!ImGui.BeginPopupModal(popupName, ref _savePresetPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                // Modal not showing - if user closed it, reset state
                if (!_savePresetPopupOpen)
                {
                    _savePresetToolIndex = -1;
                }
                return;
            }

            try
            {
                ImGui.TextUnformatted("Save as Preset");
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextWrapped("Save the current tool configuration as a reusable preset.");
                ImGui.Spacing();

                ImGui.TextUnformatted("Preset Name:");
                ImGui.SetNextItemWidth(300f);
                ImGui.InputTextWithHint("##presetNameInput", "Enter preset name", ref _savePresetName, 256);

                ImGui.Spacing();
                ImGui.TextUnformatted("Description (optional):");
                ImGui.SetNextItemWidth(300f);
                ImGui.InputTextWithHint("##presetDescInput", "Enter description", ref _savePresetDescription, 512);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var canSave = !string.IsNullOrWhiteSpace(_savePresetName);
                if (!canSave)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGuiHelpers.ButtonAutoWidth("Save"))
                {
                    try
                    {
                        var settings = toolToSave.ExportToolSettings();
                        if (settings != null && OnSavePreset != null)
                        {
                            OnSavePreset.Invoke(toolToSave.Id, _savePresetName.Trim(), settings);
                            LogService.Debug(LogCategory.UI, $"Saved preset '{_savePresetName}' for tool type '{toolToSave.Id}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(LogCategory.UI, "Error saving preset", ex);
                    }
                    ImGui.CloseCurrentPopup();
                    _savePresetPopupOpen = false;
                }

                if (!canSave)
                {
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        ImGui.SetTooltip("Enter a preset name to save");
                    }
                }

                ImGui.SameLine();
                if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                    _savePresetPopupOpen = false;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.UI, "Error in save preset modal", ex);
            }

            ImGui.EndPopup();

            if (!_savePresetPopupOpen)
            {
                _savePresetToolIndex = -1;
            }
        }

        /// <summary>
        /// Draws the tool settings window if one is currently open.
        /// </summary>
        private void DrawToolSettingsWindow()
        {
        if (_settingsToolIndex < 0 || _settingsToolIndex >= _tools.Count)
            return;

        var toolForSettings = _tools[_settingsToolIndex].Tool;
        var windowTitle = $"{toolForSettings.Title ?? "Tool"} Settings###ToolSettingsWindow";

        ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin(windowTitle, ref _settingsPopupOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            if (!_settingsPopupOpen)
            {
                _settingsToolIndex = -1;
            }
            return;
        }
        
        // In fullscreen mode, always bring tool settings window to front so it stays above the main window.
        // In windowed mode, only bring to front when focused.
        // Skip when any popup is open (dropdowns, context menus, etc.) to prevent z-order issues
        // where this window would cover its own dropdowns.
        var isFullscreen = IsFullscreenMode?.Invoke() ?? false;
        var shouldBringToFront = isFullscreen || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (shouldBringToFront && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
        {
            var window = ImGuiP.GetCurrentWindow();
            ImGuiP.BringWindowToDisplayFront(window);
        }

        try
        {
            try
            {
                toolForSettings.DrawSettings();
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.UI, "Error while drawing tool settings", ex);
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "Error rendering settings");
            }

            ImGui.Separator();
            if (ImGuiHelpers.ButtonAutoWidth("Close"))
            {
                _settingsPopupOpen = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, "Error in tool settings window", ex);
        }

        ImGui.End();

        if (!_settingsPopupOpen)
        {
            _settingsToolIndex = -1;
        }
    }

    /// <summary>
    /// Draws the grid resolution editing modal.
    /// </summary>
    private void DrawGridResolutionModal(Vector2 contentSize, float cellW, float cellH)
    {
        const string popupName = "grid_resolution_popup";
        
        // Open the popup if flagged
        if (_gridResolutionPopupOpen && !ImGui.IsPopupOpen(popupName))
        {
            ImGui.OpenPopup(popupName);
        }
        
        if (!ImGui.BeginPopupModal(popupName, ref _gridResolutionPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }
        
        try
        {
            ImGui.TextUnformatted("Edit Grid Resolution");
            ImGui.Separator();
            ImGui.Spacing();
            
            // Auto-adjust checkbox
            var autoAdjust = _editingGridSettings.AutoAdjustResolution;
            if (ImGui.Checkbox("Auto-adjust resolution", ref autoAdjust))
            {
                _editingGridSettings.AutoAdjustResolution = autoAdjust;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("When enabled, grid resolution is calculated from aspect ratio.\nColumns = AspectWidth × Multiplier\nRows = AspectHeight × Multiplier");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            if (_editingGridSettings.AutoAdjustResolution)
            {
                // Show only the resolution multiplier slider
                var multiplier = _editingGridSettings.GridResolutionMultiplier;
                ImGui.TextUnformatted("Grid Resolution Multiplier:");
                if (ImGui.SliderInt("##resolution", ref multiplier, 1, 10))
                {
                    _editingGridSettings.GridResolutionMultiplier = multiplier;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Higher values create a finer grid.\nFor 16:9 aspect ratio:\n  1 = 16×9 grid\n  2 = 32×18 grid\n  4 = 64×36 grid");
                }
                
                ImGui.Spacing();
                
                // Show preview of calculated values
                var previewCols = _editingGridSettings.GetEffectiveColumns(16f, 9f);
                var previewRows = _editingGridSettings.GetEffectiveRows(16f, 9f);
                ImGui.TextColored(UiColors.Info, $"Preview (16:9): {previewCols} columns × {previewRows} rows");
            }
            else
            {
                // Show manual column/row inputs
                ImGui.TextUnformatted("Columns:");
                var cols = _editingGridSettings.Columns;
                if (ImGui.InputInt("##cols", ref cols))
                {
                    _editingGridSettings.Columns = Math.Max(1, Math.Min(100, cols));
                }
                
                ImGui.TextUnformatted("Rows:");
                var rows = _editingGridSettings.Rows;
                if (ImGui.InputInt("##rows", ref rows))
                {
                    _editingGridSettings.Rows = Math.Max(1, Math.Min(100, rows));
                }
                
                ImGui.Spacing();
                
                ImGui.TextColored(UiColors.Info, $"Grid: {_editingGridSettings.Columns} columns × {_editingGridSettings.Rows} rows");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Tool internal padding
            ImGui.TextUnformatted("Tool Internal Padding (pixels):");
            var toolPadding = _editingGridSettings.ToolInternalPaddingPx;
            if (ImGui.SliderInt("##toolpadding", ref toolPadding, 0, 32))
            {
                _editingGridSettings.ToolInternalPaddingPx = toolPadding;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Padding in pixels inside each tool.\nHigher values create more space around tool content.\n0 = no padding.");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // OK / Cancel buttons
            if (ImGuiHelpers.ButtonAutoWidth("OK"))
            {
                try
                {
                    // Apply the new settings and reposition tools
                    UpdateGridSettings(_editingGridSettings, contentSize);
                    
                    // Notify host to persist the settings
                    try { OnGridSettingsChanged?.Invoke(_currentGridSettings); }
                    catch (Exception ex) { LogService.Debug(LogCategory.UI, $"OnGridSettingsChanged error: {ex.Message}"); }
                }
                catch (Exception ex)
                {
                    LogService.Error(LogCategory.UI, "Error applying grid settings", ex);
                }
                
                ImGui.CloseCurrentPopup();
                _gridResolutionPopupOpen = false;
            }
            
            ImGui.SameLine();
            
            if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
            {
                ImGui.CloseCurrentPopup();
                _gridResolutionPopupOpen = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, "Error in grid resolution modal", ex);
        }
        
        ImGui.EndPopup();
    }

    /// <summary>
    /// Draws the unsaved changes confirmation dialog using state from LayoutEditingService.
    /// </summary>
    private void DrawUnsavedChangesDialog()
    {
        // Check if the dialog should be shown via LayoutEditingService callback
        var shouldShow = GetShowUnsavedChangesDialog?.Invoke() ?? false;
        if (!shouldShow)
        {
            return;
        }
        
        const string popupName = "unsaved_changes_popup";
        
        // Open the popup if not already open
        if (!ImGui.IsPopupOpen(popupName))
        {
            ImGui.OpenPopup(popupName);
        }
        
        var open = true;
        if (!ImGui.BeginPopupModal(popupName, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }
        
        try
        {
            ImGui.TextUnformatted("Unsaved Layout Changes");
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.TextWrapped("You have unsaved changes to the current layout.");
            
            var description = GetPendingActionDescription?.Invoke();
            if (!string.IsNullOrWhiteSpace(description))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), $"Action: {description}");
            }
            ImGui.Spacing();
            ImGui.TextUnformatted("What would you like to do?");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Save button
            if (ImGuiHelpers.ButtonAutoWidth("Save"))
            {
                HandleUnsavedChangesChoice?.Invoke(UnsavedChangesChoice.Save);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Save your changes, then continue");
            }
            
            ImGui.SameLine();
            
            // Discard button
            if (ImGuiHelpers.ButtonAutoWidth("Discard"))
            {
                HandleUnsavedChangesChoice?.Invoke(UnsavedChangesChoice.Discard);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Discard your changes and revert to the last saved layout");
            }
            
            ImGui.SameLine();
            
            // Cancel button
            if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
            {
                HandleUnsavedChangesChoice?.Invoke(UnsavedChangesChoice.Cancel);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Cancel and return to editing");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, "Error in unsaved changes dialog", ex);
        }

        ImGui.EndPopup();
    }
}