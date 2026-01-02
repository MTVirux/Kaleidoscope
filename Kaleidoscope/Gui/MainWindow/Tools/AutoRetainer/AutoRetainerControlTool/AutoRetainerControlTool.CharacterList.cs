using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// AutoRetainerControlTool partial class containing character list rendering logic.
/// </summary>
public partial class AutoRetainerControlTool
{
    private void DrawCharacterSection()
    {
        if (_characters == null || _characters.Count == 0)
        {
            ImGui.TextColored(DisabledColor, "No characters registered");
            return;
        }

        // Scrollable region for character list
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        if (ImGui.BeginChild("CharacterList", new Vector2(0, availableHeight), false, ImGuiWindowFlags.None))
        {
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            
            foreach (var character in _characters)
            {
                // Skip hidden characters
                if (HiddenCharacters.Contains(character.CID))
                    continue;
                    
                DrawCharacterEntry(character, now);
            }
        }
        ImGui.EndChild();
    }

    private void DrawCharacterEntry(AutoRetainerCharacterData character, long nowUnix)
    {
        var isExpanded = _expandedCharacters.Contains(character.CID);
        
        // Get enabled retainers for this character
        var enabledRetainerNames = new HashSet<string>();
        if (_enabledRetainers != null && _enabledRetainers.TryGetValue(character.CID, out var names))
        {
            enabledRetainerNames = names;
        }
        
        // Calculate overall character status
        var enabledRetainerCount = character.Retainers.Count(r => enabledRetainerNames.Contains(r.Name));
        var readyRetainers = character.Retainers.Count(r => r.HasVenture && r.VentureEndsAt <= nowUnix && enabledRetainerNames.Contains(r.Name));
        var hasReadyRetainers = readyRetainers > 0;
        var readyVessels = character.Vessels.Count(v => v.ReturnTime > 0 && v.ReturnTime <= nowUnix);
        var hasReadyVessels = readyVessels > 0;
        var hasRetainers = character.Retainers.Count > 0;
        var hasVessels = character.Vessels.Count > 0;
        
        // Character header row
        ImGui.PushID((int)character.CID);
        
        // Calculate vertical offset to center buttons with the collapsing header
        var headerHeight = ImGui.CalcTextSize("A").Y + ImGui.GetStyle().FramePadding.Y * 2;
        var smallButtonHeight = ImGui.CalcTextSize("R").Y + ImGui.GetStyle().FramePadding.Y;
        var verticalOffset = (headerHeight - smallButtonHeight) * 0.5f;
        var startY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(startY + verticalOffset);
        
        // Draw R/D/L buttons FIRST (outside progress bar area)
        DrawCharacterButtons(character, hasRetainers, hasVessels);
        
        ImGui.SameLine();
        
        // Reset cursor Y for the collapsing header to align properly
        ImGui.SetCursorPosY(startY);
        
        // Calculate progress for the header background
        var progress = CalculateCharacterProgress(character, nowUnix, out var hasAnythingReady);
        
        // Build status text for the header
        var (retainerStatus, retainerStatusColor) = BuildRetainerStatus(character, enabledRetainerCount, hasRetainers, hasReadyRetainers);
        var (vesselStatus, vesselStatusColor) = BuildVesselStatus(character, readyVessels, hasVessels, hasReadyVessels);
        
        // Calculate full status width for positioning (with separator and spacing)
        var statusText = BuildStatusText(retainerStatus, vesselStatus);
        
        // Draw custom collapsing header with progress bar fill
        var headerLabel = $"{character.Name} @ {character.World}";
        var headerOpen = DrawProgressCollapsingHeader(headerLabel, progress, hasAnythingReady, isExpanded, ProgressBarColor, ProgressBarReadyColor);
        
        // Update expanded state
        if (headerOpen != isExpanded)
        {
            if (headerOpen)
                _expandedCharacters.Add(character.CID);
            else
                _expandedCharacters.Remove(character.CID);
        }
        
        // Right-click context menu for hiding character
        if (ImGui.BeginPopupContextItem($"CharacterContext_{character.CID}"))
        {
            if (ImGui.MenuItem("Hide Character"))
            {
                HiddenCharacters.Add(character.CID);
                NotifyToolSettingsChanged();
            }
            ImGui.EndPopup();
        }
        
        // Draw status on the same line (right-aligned)
        DrawStatusText(statusText, retainerStatus, retainerStatusColor, vesselStatus, vesselStatusColor);
        
        // Expanded content
        if (headerOpen)
        {
            DrawExpandedCharacterContent(character, nowUnix, enabledRetainerNames);
        }
        
        ImGui.PopID();
    }

    private void DrawCharacterButtons(AutoRetainerCharacterData character, bool hasRetainers, bool hasVessels)
    {
        // Retainer toggle button (disabled if no retainers)
        var retainerColor = !hasRetainers ? DisabledColor : (character.Enabled ? ConnectedColor : DisabledColor);
        ImGui.PushStyleColor(ImGuiCol.Text, retainerColor);
        ImGui.BeginDisabled(!hasRetainers);
        if (ImGui.SmallButton("R"))
        {
            _autoRetainerIpc?.SetCharacterRetainersEnabled(character.CID, !character.Enabled);
            _lastRefresh = DateTime.MinValue;
        }
        ImGui.EndDisabled();
        ImGui.PopStyleColor();
        
        ImGui.SameLine(0, 2);
        
        // Deployable toggle button (disabled if no vessels)
        var deployableColor = !hasVessels ? DisabledColor : (character.WorkshopEnabled ? ConnectedColor : DisabledColor);
        ImGui.PushStyleColor(ImGuiCol.Text, deployableColor);
        ImGui.BeginDisabled(!hasVessels);
        if (ImGui.SmallButton("D"))
        {
            _autoRetainerIpc?.SetCharacterDeployablesEnabled(character.CID, !character.WorkshopEnabled);
            _lastRefresh = DateTime.MinValue;
        }
        ImGui.EndDisabled();
        ImGui.PopStyleColor();
        
        ImGui.SameLine(0, 2);
        
        // Login button (disabled if auto-login not available)
        var canLogin = _canAutoLogin ?? false;
        ImGui.BeginDisabled(!canLogin);
        if (ImGui.SmallButton("L"))
        {
            _autoRetainerIpc?.Relog($"{character.Name}@{character.World}");
        }
        ImGui.EndDisabled();
    }

    private (string? status, Vector4 color) BuildRetainerStatus(AutoRetainerCharacterData character, int enabledCount, bool hasRetainers, bool hasReady)
    {
        if (!hasRetainers)
            return (null, DisabledColor);
        
        var status = $"Ret: {enabledCount}/{character.Retainers.Count}";
        Vector4 color;
        
        if (!character.Enabled || enabledCount == 0)
            color = DisabledColor;
        else if (hasReady)
            color = ReadyColor;
        else
            color = EnabledColor;
        
        return (status, color);
    }

    private (string? status, Vector4 color) BuildVesselStatus(AutoRetainerCharacterData character, int readyVessels, bool hasVessels, bool hasReady)
    {
        if (!hasVessels)
            return (null, DisabledColor);
        
        var totalDeployed = character.Vessels.Count(v => v.ReturnTime > 0);
        if (totalDeployed == 0)
            return (null, DisabledColor);
        
        var status = $"Sub: {readyVessels}/{totalDeployed}";
        Vector4 color;
        
        if (!character.WorkshopEnabled)
            color = DisabledColor;
        else if (hasReady)
            color = ReadyColor;
        else
            color = EnabledColor;
        
        return (status, color);
    }

    private static string BuildStatusText(string? retainerStatus, string? vesselStatus)
    {
        var statusText = "";
        if (retainerStatus != null) statusText += retainerStatus;
        if (retainerStatus != null && vesselStatus != null) statusText += "  |  ";
        if (vesselStatus != null) statusText += vesselStatus;
        statusText += "  ";
        return statusText;
    }

    private void DrawStatusText(string statusText, string? retainerStatus, Vector4 retainerColor, string? vesselStatus, Vector4 vesselColor)
    {
        if (string.IsNullOrEmpty(statusText.Trim()))
            return;
        
        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(statusText).X);
        
        if (retainerStatus != null)
        {
            ImGui.TextColored(retainerColor, retainerStatus);
            if (vesselStatus != null)
            {
                ImGui.SameLine(0, 0);
                ImGui.TextColored(DisabledColor, "  |  ");
                ImGui.SameLine(0, 0);
                ImGui.TextColored(vesselColor, vesselStatus);
            }
        }
        else if (vesselStatus != null)
        {
            ImGui.TextColored(vesselColor, vesselStatus);
        }
        
        ImGui.SameLine(0, 0);
        ImGui.TextUnformatted("  ");
    }

    private void DrawExpandedCharacterContent(AutoRetainerCharacterData character, long nowUnix, HashSet<string> enabledRetainerNames)
    {
        ImGui.Indent();
        
        // Gil info (optional)
        if (ShowGil)
        {
            var gilText = $"{character.Gil:N0} gil";
            if (character.FCGil > 0)
            {
                gilText += $" (+{character.FCGil:N0} FC)";
            }
            ImGui.TextColored(DisabledColor, gilText);
        }
        
        // Retainer list
        if (character.Retainers.Count > 0)
        {
            foreach (var retainer in character.Retainers.OrderBy(r => r.HasVenture ? r.VentureEndsAt : long.MaxValue))
            {
                DrawRetainerEntry(character.CID, retainer, nowUnix, enabledRetainerNames);
            }
        }
        
        // Vessel list (deployables)
        if (character.Vessels.Count > 0)
        {
            ImGui.Spacing();
            foreach (var vessel in character.Vessels.OrderBy(v => v.ReturnTime == 0 ? long.MaxValue : v.ReturnTime))
            {
                DrawVesselEntry(vessel, nowUnix);
            }
        }
        
        ImGui.Unindent();
    }

    private void DrawVesselEntry(AutoRetainerVesselData vessel, long nowUnix)
    {
        // Vessel icon
        var icon = vessel.IsSubmersible ? "◆" : "✈";
        ImGui.TextColored(RetainerColor, icon);
        ImGui.SameLine();
        
        // Vessel name
        ImGui.TextColored(RetainerColor, vessel.Name);
        ImGui.SameLine();
        
        // Type label
        var typeLabel = vessel.IsSubmersible ? "[Sub]" : "[Air]";
        ImGui.TextColored(DisabledColor, typeLabel);
        ImGui.SameLine();
        
        // Return time status
        if (vessel.ReturnTime == 0)
        {
            ImGui.TextColored(DisabledColor, "Not deployed");
        }
        else
        {
            var secondsRemaining = vessel.ReturnTime - nowUnix;
            
            if (secondsRemaining <= 0)
            {
                ImGui.TextColored(ReadyColor, "Returned!");
            }
            else
            {
                var timeText = FormatUtils.FormatCountdown(secondsRemaining);
                var timeColor = secondsRemaining < 300 ? WarningColor : DisabledColor;
                ImGui.TextColored(timeColor, timeText);
            }
        }
    }

    private void DrawRetainerEntry(ulong cid, AutoRetainerRetainerData retainer, long nowUnix, HashSet<string> enabledRetainerNames)
    {
        ImGui.PushID(retainer.Name);
        
        // Enabled checkbox (read-only)
        var isEnabled = enabledRetainerNames.Contains(retainer.Name);
        ImGui.BeginDisabled();
        ImGui.Checkbox($"##{retainer.Name}_enabled", ref isEnabled);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Individual retainer enable/disable is not available via IPC.\nUse AutoRetainer's UI to change this setting.");
        }
        ImGui.SameLine();
        
        // Retainer name
        var nameColor = isEnabled ? RetainerColor : DisabledColor;
        ImGui.TextColored(nameColor, retainer.Name);
        ImGui.SameLine();
        
        // Level
        ImGui.TextColored(DisabledColor, $"Lv{retainer.Level}");
        ImGui.SameLine();
        
        // Venture status
        if (retainer.HasVenture)
        {
            var secondsRemaining = retainer.VentureEndsAt - nowUnix;
            
            if (secondsRemaining <= 0)
            {
                ImGui.TextColored(ReadyColor, "Ready!");
            }
            else
            {
                var timeText = FormatUtils.FormatCountdown(secondsRemaining);
                var timeColor = secondsRemaining < 300 ? WarningColor : DisabledColor;
                ImGui.TextColored(timeColor, timeText);
            }
        }
        else
        {
            ImGui.TextColored(DisabledColor, "No venture");
        }
        
        ImGui.PopID();
    }

    /// <summary>
    /// Calculates the overall progress for a character based on the closest venture/voyage to completion.
    /// </summary>
    private static float CalculateCharacterProgress(AutoRetainerCharacterData character, long nowUnix, out bool hasAnythingReady)
    {
        hasAnythingReady = false;
        
        long closestEndTime = long.MaxValue;
        long maxDuration = 0;
        
        // Check retainers
        foreach (var retainer in character.Retainers)
        {
            if (!retainer.HasVenture) continue;
            
            if (retainer.VentureEndsAt <= nowUnix)
            {
                hasAnythingReady = true;
                return 1.0f;
            }
            
            if (retainer.VentureEndsAt < closestEndTime)
            {
                closestEndTime = retainer.VentureEndsAt;
                maxDuration = MaxVentureDuration;
            }
        }
        
        // Check vessels
        foreach (var vessel in character.Vessels)
        {
            if (vessel.ReturnTime <= 0) continue;
            
            if (vessel.ReturnTime <= nowUnix)
            {
                hasAnythingReady = true;
                return 1.0f;
            }
            
            if (vessel.ReturnTime < closestEndTime)
            {
                closestEndTime = vessel.ReturnTime;
                maxDuration = MaxVoyageDuration;
            }
        }
        
        if (closestEndTime == long.MaxValue)
            return 0.0f;
        
        var secondsRemaining = closestEndTime - nowUnix;
        var progress = 1.0f - (float)secondsRemaining / maxDuration;
        
        return Math.Clamp(progress, 0.0f, 1.0f);
    }

    /// <summary>
    /// Draws a collapsing header with a progress bar fill inside the header background.
    /// </summary>
    private static bool DrawProgressCollapsingHeader(string label, float progress, bool isReady, bool defaultOpen, Vector4 progressColor, Vector4 readyColor)
    {
        var initCursorPos = ImGui.GetCursorPos();
        var headerHeight = ImGui.CalcTextSize("A").Y + ImGui.GetStyle().FramePadding.Y * 2;
        
        if (progress > 0.0f)
        {
            var barColor = isReady ? readyColor : progressColor;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0));
            ImGui.ProgressBar(progress, new Vector2(ImGui.GetContentRegionAvail().X, headerHeight), "");
            ImGui.PopStyleColor(2);
        }
        
        ImGui.SetCursorPos(initCursorPos);
        
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        var isOpen = ImGui.CollapsingHeader(label, flags);
        
        return isOpen;
    }
}
