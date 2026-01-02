using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using MTGui.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// AutoRetainerControlTool partial class containing settings UI and import/export logic.
/// </summary>
public partial class AutoRetainerControlTool
{
    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        var showControls = ShowControls;
        if (ImGui.Checkbox("Show Control Buttons", ref showControls))
        {
            ShowControls = showControls;
            NotifyToolSettingsChanged();
        }

        var showCharacterList = ShowCharacterList;
        if (ImGui.Checkbox("Show Character List", ref showCharacterList))
        {
            ShowCharacterList = showCharacterList;
            NotifyToolSettingsChanged();
        }

        var showGil = ShowGil;
        if (ImGui.Checkbox("Show Gil", ref showGil))
        {
            ShowGil = showGil;
            NotifyToolSettingsChanged();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Display gil information for each character (including FC gil if available)");
        }

        ImGui.Spacing();
        
        if (ImGui.Button("Force Refresh"))
        {
            _autoRetainerIpc?.Refresh();
            _lastRefresh = DateTime.MinValue;
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        DrawHiddenCharactersSection();
        DrawColorsSection();
    }

    private void DrawHiddenCharactersSection()
    {
        if (MTTreeHelpers.DrawCollapsingSection("Hidden Characters", false))
        {
            ImGui.Indent();
            
            if (HiddenCharacters.Count == 0)
            {
                ImGui.TextColored(DisabledColor, "No hidden characters");
            }
            else
            {
                if (ImGui.Button("Unhide All"))
                {
                    HiddenCharacters.Clear();
                    NotifyToolSettingsChanged();
                }
                ImGui.Spacing();
                
                ulong? characterToUnhide = null;
                foreach (var cid in HiddenCharacters)
                {
                    var characterName = "Unknown";
                    if (_characters != null)
                    {
                        var character = _characters.FirstOrDefault(c => c.CID == cid);
                        if (character != null)
                        {
                            characterName = $"{character.Name} @ {character.World}";
                        }
                    }
                    
                    ImGui.TextUnformatted(characterName);
                    ImGui.SameLine();
                    ImGui.PushID((int)cid);
                    if (ImGui.SmallButton("Unhide"))
                    {
                        characterToUnhide = cid;
                    }
                    ImGui.PopID();
                }
                
                if (characterToUnhide.HasValue)
                {
                    HiddenCharacters.Remove(characterToUnhide.Value);
                    NotifyToolSettingsChanged();
                }
            }
            
            ImGui.Unindent();
        }
    }

    private void DrawColorsSection()
    {
        if (MTTreeHelpers.DrawCollapsingSection("Colors", false))
        {
            ImGui.Indent();
            
            // Text colors
            ImGui.TextUnformatted("Text Colors");
            ImGui.Spacing();
            
            DrawColorSetting("##readytext", "Ready", ref _readyColor, DefaultReadyColor);
            DrawColorSetting("##enabledtext", "Enabled", ref _enabledColor, DefaultEnabledColor);
            DrawColorSetting("##disabledtext", "Disabled", ref _disabledColor, DefaultDisabledColor);
            DrawColorSetting("##connectedtext", "Connected/On", ref _connectedColor, DefaultConnectedColor);
            DrawColorSetting("##warningtext", "Warning", ref _warningColor, DefaultWarningColor);
            DrawColorSetting("##retainertext", "Retainer/Vessel Name", ref _retainerColor, DefaultRetainerColor);
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Header colors
            ImGui.TextUnformatted("Header Progress Colors");
            ImGui.Spacing();
            
            DrawColorSetting("##readyheader", "Ready (green)", ref _progressBarReadyColor, DefaultProgressBarReadyColor);
            DrawColorSetting("##inprogressheader", "In Progress (red)", ref _progressBarColor, DefaultProgressBarColor);
            
            ImGui.Unindent();
        }
    }

    private void DrawColorSetting(string id, string label, ref Vector4 color, Vector4 defaultColor)
    {
        var (changed, newColor) = ImGuiHelpers.ColorPickerWithReset(id, color, defaultColor, label);
        if (changed)
        {
            color = newColor;
            NotifyToolSettingsChanged();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
    }

    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        var settings = new Dictionary<string, object?>
        {
            ["ShowCharacterList"] = ShowCharacterList,
            ["ShowControls"] = ShowControls,
            ["ShowGil"] = ShowGil,
        };
        
        ExportHashSet(settings, "HiddenCharacters", HiddenCharacters);
        
        ExportColor(settings, "ConnectedColor", ConnectedColor);
        ExportColor(settings, "DisconnectedColor", DisconnectedColor);
        ExportColor(settings, "WarningColor", WarningColor);
        ExportColor(settings, "ReadyColor", ReadyColor);
        ExportColor(settings, "DisabledColor", DisabledColor);
        ExportColor(settings, "EnabledColor", EnabledColor);
        ExportColor(settings, "HeaderColor", HeaderColor);
        ExportColor(settings, "RetainerColor", RetainerColor);
        ExportColor(settings, "ProgressBarColor", ProgressBarColor);
        ExportColor(settings, "ProgressBarReadyColor", ProgressBarReadyColor);
        
        return settings;
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        ShowCharacterList = GetSetting(settings, "ShowCharacterList", ShowCharacterList);
        ShowControls = GetSetting(settings, "ShowControls", ShowControls);
        ShowGil = GetSetting(settings, "ShowGil", ShowGil);
        
        HiddenCharacters = ImportHashSet(settings, "HiddenCharacters", HiddenCharacters);
        
        ConnectedColor = ImportColor(settings, "ConnectedColor", DefaultConnectedColor);
        DisconnectedColor = ImportColor(settings, "DisconnectedColor", DefaultDisconnectedColor);
        WarningColor = ImportColor(settings, "WarningColor", DefaultWarningColor);
        ReadyColor = ImportColor(settings, "ReadyColor", DefaultReadyColor);
        DisabledColor = ImportColor(settings, "DisabledColor", DefaultDisabledColor);
        EnabledColor = ImportColor(settings, "EnabledColor", DefaultEnabledColor);
        HeaderColor = ImportColor(settings, "HeaderColor", DefaultHeaderColor);
        RetainerColor = ImportColor(settings, "RetainerColor", DefaultRetainerColor);
        ProgressBarColor = ImportColor(settings, "ProgressBarColor", DefaultProgressBarColor);
        ProgressBarReadyColor = ImportColor(settings, "ProgressBarReadyColor", DefaultProgressBarReadyColor);
    }
}
