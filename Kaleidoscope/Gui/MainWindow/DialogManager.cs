using System.Numerics;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Dalamud.Bindings.ImGui;
using ImGuiHelpers = Kaleidoscope.Gui.Common.ImGuiHelpers;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Manages all modal dialogs for the content container:
/// rename, save preset, tool settings, grid resolution, and unsaved changes.
/// Extracted from WindowContentContainer.Dialogs.cs for single-responsibility.
/// </summary>
internal sealed class DialogManager
{
    // ── Rename Modal State ──────────────────────────────────────────────
    private int _renameToolIndex = -1;
    private bool _renamePopupOpen = false;
    private string _renameBuffer = string.Empty;

    // ── Tool Settings Window State ──────────────────────────────────────
    private int _settingsToolIndex = -1;
    private bool _settingsPopupOpen = false;

    // ── Grid Resolution Modal State ─────────────────────────────────────
    private bool _gridResolutionPopupOpen = false;
    private LayoutGridSettings _editingGridSettings = new();
    private int _previousColumns = 0;
    private int _previousRows = 0;

    // ── Save Preset Modal State ─────────────────────────────────────────
    private int _savePresetToolIndex = -1;
    private bool _savePresetPopupOpen = false;
    private string _savePresetName = string.Empty;
    private string _savePresetDescription = string.Empty;

    /// <summary>Whether the grid resolution modal is currently open.</summary>
    public bool IsGridResolutionOpen => _gridResolutionPopupOpen;

    /// <summary>Opens the rename modal for the tool at the given index.</summary>
    public void OpenRenameModal(int toolIndex, string currentTitle)
    {
        _renameToolIndex = toolIndex;
        _renameBuffer = currentTitle;
        _renamePopupOpen = true;
    }

    /// <summary>Opens the tool settings window for the tool at the given index.</summary>
    public void OpenToolSettings(int toolIndex)
    {
        _settingsToolIndex = toolIndex;
        _settingsPopupOpen = true;
    }

    /// <summary>Opens the grid resolution modal with current settings.</summary>
    public void OpenGridResolution(LayoutGridSettings currentSettings, int currentCols, int currentRows)
    {
        _editingGridSettings = currentSettings.Clone();
        _previousColumns = currentCols;
        _previousRows = currentRows;
        _gridResolutionPopupOpen = true;
    }

    /// <summary>Opens the save preset modal for the tool at the given index.</summary>
    public void OpenSavePreset(int toolIndex)
    {
        _savePresetToolIndex = toolIndex;
        _savePresetPopupOpen = true;
        _savePresetName = string.Empty;
        _savePresetDescription = string.Empty;
    }

    /// <summary>Draws the tool rename modal if open.</summary>
    public void DrawToolRenameModal(IReadOnlyList<ToolEntry> tools, Action markDirty)
    {
        if (_renameToolIndex < 0 || _renameToolIndex >= tools.Count)
            return;

        const string popupName = "rename_tool_popup";
        var toolToRename = tools[_renameToolIndex].Tool;

        if (_renamePopupOpen && !ImGui.IsPopupOpen(popupName))
        {
            ImGui.OpenPopup(popupName);
        }

        if (!ImGui.BeginPopupModal(popupName, ref _renamePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!_renamePopupOpen)
            {
                _renameToolIndex = -1;
            }
            return;
        }

        try
        {
            ImGui.TextUnformatted($"Rename: {toolToRename.Title}");
            ImGui.Separator();
            ImGui.InputText("##rename", ref _renameBuffer, ConfigStatic.TextInputBufferSize);
            
            if (ImGui.Button("OK"))
            {
                toolToRename.CustomTitle = string.IsNullOrWhiteSpace(_renameBuffer) ? null : _renameBuffer.Trim();
                markDirty();
                ImGui.CloseCurrentPopup();
                _renamePopupOpen = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
                _renamePopupOpen = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset"))
            {
                toolToRename.CustomTitle = null;
                markDirty();
                ImGui.CloseCurrentPopup();
                _renamePopupOpen = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, "Error in rename modal", ex);
        }

        ImGui.EndPopup();

        if (!_renamePopupOpen)
        {
            _renameToolIndex = -1;
        }
    }

    /// <summary>Draws the save-as-preset modal if open.</summary>
    public void DrawSavePresetModal(IReadOnlyList<ToolEntry> tools, ILayoutHost host)
    {
        if (_savePresetToolIndex < 0 || _savePresetToolIndex >= tools.Count)
            return;

        const string popupName = "save_preset_popup";
        var toolToSave = tools[_savePresetToolIndex].Tool;

        if (_savePresetPopupOpen && !ImGui.IsPopupOpen(popupName))
        {
            ImGui.OpenPopup(popupName);
        }

        if (!ImGui.BeginPopupModal(popupName, ref _savePresetPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
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
            if (!canSave) ImGui.BeginDisabled();

            if (ImGuiHelpers.ButtonAutoWidth("Save"))
            {
                try
                {
                    var settings = toolToSave.ExportToolSettings();
                    if (settings != null)
                    {
                        var desc = string.IsNullOrWhiteSpace(_savePresetDescription) ? null : _savePresetDescription.Trim();
                        host.SavePreset(toolToSave.Id, _savePresetName.Trim(), settings, desc);
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

    /// <summary>Draws the tool settings window if one is currently open.</summary>
    public void DrawToolSettingsWindow(IReadOnlyList<ToolEntry> tools, ILayoutHost host)
    {
        if (_settingsToolIndex < 0 || _settingsToolIndex >= tools.Count)
            return;

        var toolForSettings = tools[_settingsToolIndex].Tool;
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

        // In fullscreen mode, always bring tool settings window to front.
        // In windowed mode, only bring to front when focused.
        // Skip when any popup is open to prevent z-order issues with dropdowns.
        var isFullscreen = host.IsFullscreenMode;
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

    /// <summary>Draws the grid resolution editing modal.</summary>
    public void DrawGridResolutionModal(
        DrawContext ctx,
        LayoutGridSettings currentGridSettings,
        Action<LayoutGridSettings, Vector2> updateGridSettings,
        Action<LayoutGridSettings> notifyGridSettingsChanged)
    {
        const string popupName = "grid_resolution_popup";

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

                var previewCols = _editingGridSettings.GetEffectiveColumns(16f, 9f);
                var previewRows = _editingGridSettings.GetEffectiveRows(16f, 9f);
                ImGui.TextColored(UiColors.Info, $"Preview (16:9): {previewCols} columns × {previewRows} rows");
            }
            else
            {
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

            // OK / Cancel
            if (ImGuiHelpers.ButtonAutoWidth("OK"))
            {
                try
                {
                    updateGridSettings(_editingGridSettings, ctx.AvailRegion);
                    try { notifyGridSettingsChanged(currentGridSettings); }
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

    /// <summary>Draws the unsaved changes confirmation dialog.</summary>
    public void DrawUnsavedChangesDialog(ILayoutHost host)
    {
        if (!host.ShowUnsavedChangesDialog)
            return;

        const string popupName = "unsaved_changes_popup";

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

            var description = host.PendingActionDescription;
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

            if (ImGuiHelpers.ButtonAutoWidth("Save"))
            {
                host.HandleUnsavedChangesChoice(UnsavedChangesChoice.Save);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Save your changes, then continue");
            }

            ImGui.SameLine();

            if (ImGuiHelpers.ButtonAutoWidth("Discard"))
            {
                host.HandleUnsavedChangesChoice(UnsavedChangesChoice.Discard);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Discard your changes and revert to the last saved layout");
            }

            ImGui.SameLine();

            if (ImGuiHelpers.ButtonAutoWidth("Cancel"))
            {
                host.HandleUnsavedChangesChoice(UnsavedChangesChoice.Cancel);
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
