using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// A tool that provides control over AutoRetainer via IPC.
/// Displays status information and provides buttons to control AutoRetainer functions.
/// </summary>
public class AutoRetainerControlTool : ToolComponent
{
    public override string ToolName => "AutoRetainer Control";

    private readonly AutoRetainerIpcService? _autoRetainerIpc;

    // Default colors (static for reference)
    private static readonly Vector4 DefaultConnectedColor = new(0.2f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 DefaultDisconnectedColor = new(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 DefaultWarningColor = new(0.9f, 0.7f, 0.2f, 1f);
    private static readonly Vector4 DefaultReadyColor = new(0.4f, 1.0f, 0.4f, 1f);
    private static readonly Vector4 DefaultDisabledColor = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 DefaultEnabledColor = new(1.0f, 1.0f, 1.0f, 1f);
    private static readonly Vector4 DefaultHeaderColor = new(0.6f, 0.8f, 1f, 1f);
    private static readonly Vector4 DefaultRetainerColor = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 DefaultProgressBarColor = new(0.31f, 0.0f, 0.0f, 0.73f);
    private static readonly Vector4 DefaultProgressBarReadyColor = new(0.0f, 0.31f, 0.0f, 0.73f);
    
    // Configurable colors
    public Vector4 ConnectedColor { get; set; } = DefaultConnectedColor;
    public Vector4 DisconnectedColor { get; set; } = DefaultDisconnectedColor;
    public Vector4 WarningColor { get; set; } = DefaultWarningColor;
    public Vector4 ReadyColor { get; set; } = DefaultReadyColor;
    public Vector4 DisabledColor { get; set; } = DefaultDisabledColor;
    public Vector4 EnabledColor { get; set; } = DefaultEnabledColor;
    public Vector4 HeaderColor { get; set; } = DefaultHeaderColor;
    public Vector4 RetainerColor { get; set; } = DefaultRetainerColor;
    public Vector4 ProgressBarColor { get; set; } = DefaultProgressBarColor;
    public Vector4 ProgressBarReadyColor { get; set; } = DefaultProgressBarReadyColor;
    
    // Assumed max venture duration for progress calculation (18 hours = 64800 seconds)
    private const long MaxVentureDuration = 64800;
    // Assumed max voyage duration for progress calculation (24 hours = 86400 seconds)
    private const long MaxVoyageDuration = 86400;

    // Cached state to avoid spamming IPC calls
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);
    
    private bool? _isBusy;
    private bool? _isSuppressed;
    private bool? _isMultiModeEnabled;
    private bool? _canAutoLogin;
    private List<AutoRetainerCharacterData>? _characters;
    private Dictionary<ulong, HashSet<string>>? _enabledRetainers;

    // Expanded character states for tree view
    private readonly HashSet<ulong> _expandedCharacters = new();

    /// <summary>
    /// Whether to show the character list section.
    /// </summary>
    public bool ShowCharacterList { get; set; } = true;

    /// <summary>
    /// Whether to show control buttons.
    /// </summary>
    public bool ShowControls { get; set; } = true;

    /// <summary>
    /// Whether to show gil information for characters.
    /// </summary>
    public bool ShowGil { get; set; } = true;

    /// <summary>
    /// Set of character IDs that are hidden from the list.
    /// </summary>
    public HashSet<ulong> HiddenCharacters { get; set; } = new();

    public AutoRetainerControlTool(AutoRetainerIpcService? autoRetainerIpc = null)
    {
        _autoRetainerIpc = autoRetainerIpc;

        Title = "AutoRetainer Control";
        Size = new Vector2(350, 450);
    }

    public override void RenderToolContent()
    {
        try
        {
            RefreshCachedState();

            if (_autoRetainerIpc == null || !_autoRetainerIpc.IsAvailable)
            {
                DrawUnavailableState();
                return;
            }

            DrawStatusSection();
            
            if (ShowControls)
            {
                DrawControlsSection();
            }

            if (ShowCharacterList)
            {
                ImGui.Separator();
                DrawCharacterSection();
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[AutoRetainerControlTool] Draw error: {ex.Message}");
        }
    }

    private void RefreshCachedState()
    {
        if (_autoRetainerIpc == null || !_autoRetainerIpc.IsAvailable) return;
        
        var now = DateTime.Now;
        if (now - _lastRefresh < _refreshInterval) return;
        
        _lastRefresh = now;
        
        try
        {
            _isBusy = _autoRetainerIpc.IsBusy();
            _isSuppressed = _autoRetainerIpc.GetSuppressed();
            _isMultiModeEnabled = _autoRetainerIpc.GetMultiModeEnabled();
            _canAutoLogin = _autoRetainerIpc.CanAutoLogin();
            _characters = _autoRetainerIpc.GetAllFullCharacterData();
            _enabledRetainers = _autoRetainerIpc.GetEnabledRetainers();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[AutoRetainerControlTool] Refresh error: {ex.Message}");
        }
    }

    private void DrawUnavailableState()
    {
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        
        DrawStatusIndicator(false, "Not Connected");
        ImGui.Spacing();
        
        ImGui.TextColored(DisabledColor, "AutoRetainer plugin is not detected.");
        ImGui.Spacing();
        ImGui.TextColored(DisabledColor, "Install AutoRetainer from the Puni.sh repository to enable this tool.");
        
        ImGui.Spacing();
        if (ImGui.Button("Refresh Connection"))
        {
            _autoRetainerIpc?.Refresh();
        }
        
        ImGui.PopTextWrapPos();
    }

    private void DrawStatusSection()
    {
        // Use AlignTextToFramePadding to vertically center text with checkbox
        //ImGui.AlignTextToFramePadding();
        
        // Connection status indicator
        DrawStatusIndicator(true, "Connected");
        
        // State indicator (on same line)
        if (_isBusy.HasValue)
        {
            ImGui.SameLine();
            var stateColor = _isBusy.Value ? WarningColor : ConnectedColor;
            var stateIcon = _isBusy.Value ? "◐" : "●";
            ImGui.TextColored(stateColor, stateIcon);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_isBusy.Value ? "State: Processing..." : "State: Idle");
            }
        }
        
        // Auto-login indicator (on same line)
        if (_canAutoLogin.HasValue)
        {
            ImGui.SameLine();
            var loginColor = _canAutoLogin.Value ? ConnectedColor : DisabledColor;
            var loginIcon = _canAutoLogin.Value ? "●" : "○";
            ImGui.TextColored(loginColor, loginIcon);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_canAutoLogin.Value ? "Auto-Login: Available" : "Auto-Login: Not Available");
            }
        }
    }

    private void DrawControlsSection()
    {
        // Multi-Mode checkbox (on same line as status indicators)
        if (_isMultiModeEnabled.HasValue)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 3);
            var multiMode = _isMultiModeEnabled.Value;
            if (ImGui.Checkbox("Multi-Mode", ref multiMode))
            {
                _autoRetainerIpc?.SetMultiModeEnabled(multiMode);
                _lastRefresh = DateTime.MinValue;
            }
        }
    }

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
        var totalRetainersWithVentures = character.Retainers.Count(r => r.HasVenture && enabledRetainerNames.Contains(r.Name));
        var hasReadyRetainers = readyRetainers > 0;
        var readyVessels = character.Vessels.Count(v => v.ReturnTime > 0 && v.ReturnTime <= nowUnix);
        var hasReadyVessels = readyVessels > 0;
        var hasRetainers = character.Retainers.Count > 0;
        var hasVessels = character.Vessels.Count > 0;
        
        // Character header row
        ImGui.PushID((int)character.CID);
        
        // Calculate vertical offset to center buttons with the collapsing header
        // Header height = text height + FramePadding.Y * 2
        // SmallButton height = text height + FramePadding.Y (half padding on each side)
        var headerHeight = ImGui.CalcTextSize("A").Y + ImGui.GetStyle().FramePadding.Y * 2;
        var smallButtonHeight = ImGui.CalcTextSize("R").Y + ImGui.GetStyle().FramePadding.Y;
        var verticalOffset = (headerHeight - smallButtonHeight) * 0.5f;
        var startY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(startY + verticalOffset);
        
        // Draw R/D/L buttons FIRST (outside progress bar area)
        // Retainer toggle button (disabled if no retainers)
        var retainerColor = !hasRetainers ? DisabledColor : (character.Enabled ? ConnectedColor : DisabledColor);
        ImGui.PushStyleColor(ImGuiCol.Text, retainerColor);
        ImGui.BeginDisabled(!hasRetainers);
        if (ImGui.SmallButton("R"))
        {
            _autoRetainerIpc?.SetCharacterRetainersEnabled(character.CID, !character.Enabled);
            _lastRefresh = DateTime.MinValue; // Force refresh
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
            _lastRefresh = DateTime.MinValue; // Force refresh
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
        
        ImGui.SameLine();
        
        // Reset cursor Y for the collapsing header to align properly
        ImGui.SetCursorPosY(startY);
        
        // Calculate progress for the header background
        var progress = CalculateCharacterProgress(character, nowUnix, out var hasAnythingReady);
        
        // Build status text for the header
        string? retainerStatus = null;
        Vector4 retainerStatusColor = DisabledColor;
        string? vesselStatus = null;
        Vector4 vesselStatusColor = DisabledColor;
        
        if (hasRetainers)
        {
            // Show enabled count / total count
            retainerStatus = $"Ret: {enabledRetainerCount}/{character.Retainers.Count}";
            // Grey if character disabled or none enabled, green if ready, white if enabled but not ready
            if (!character.Enabled || enabledRetainerCount == 0)
                retainerStatusColor = DisabledColor;
            else if (hasReadyRetainers)
                retainerStatusColor = ReadyColor;
            else
                retainerStatusColor = EnabledColor;
        }
        
        if (hasVessels)
        {
            var totalVesselsDeployed = character.Vessels.Count(v => v.ReturnTime > 0);
            if (totalVesselsDeployed > 0)
            {
                vesselStatus = $"Sub: {readyVessels}/{totalVesselsDeployed}";
                // Grey if disabled, green if ready, white if enabled but not ready
                if (!character.WorkshopEnabled)
                    vesselStatusColor = DisabledColor;
                else if (hasReadyVessels)
                    vesselStatusColor = ReadyColor;
                else
                    vesselStatusColor = EnabledColor;
            }
        }
        
        // Calculate full status width for positioning (with separator and spacing)
        var statusText = "";
        if (retainerStatus != null) statusText += retainerStatus;
        if (retainerStatus != null && vesselStatus != null) statusText += "  |  ";
        if (vesselStatus != null) statusText += vesselStatus;
        statusText += "  "; // Add trailing space for padding from edge
        
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
        
        // Draw status on the same line (right-aligned) - draw each part with its own color
        if (!string.IsNullOrEmpty(statusText.Trim()))
        {
            ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(statusText).X);
            
            if (retainerStatus != null)
            {
                ImGui.TextColored(retainerStatusColor, retainerStatus);
                if (vesselStatus != null)
                {
                    ImGui.SameLine(0, 0);
                    ImGui.TextColored(DisabledColor, "  |  ");
                    ImGui.SameLine(0, 0);
                    ImGui.TextColored(vesselStatusColor, vesselStatus);
                }
            }
            else if (vesselStatus != null)
            {
                ImGui.TextColored(vesselStatusColor, vesselStatus);
            }
            
            // Trailing space for padding
            ImGui.SameLine(0, 0);
            ImGui.TextUnformatted("  ");
        }
        
        // Expanded content
        if (headerOpen)
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
        
        ImGui.PopID();
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
                var timeColor = secondsRemaining < 300 ? WarningColor : DisabledColor; // Yellow if < 5 min
                ImGui.TextColored(timeColor, timeText);
            }
        }
    }

    private void DrawRetainerEntry(ulong cid, AutoRetainerRetainerData retainer, long nowUnix, HashSet<string> enabledRetainerNames)
    {
        ImGui.PushID(retainer.Name);
        
        // Enabled checkbox (read-only - AutoRetainer IPC doesn't support modifying individual retainer enabled state)
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
                var timeColor = secondsRemaining < 300 ? WarningColor : DisabledColor; // Yellow if < 5 min
                ImGui.TextColored(timeColor, timeText);
            }
        }
        else
        {
            ImGui.TextColored(DisabledColor, "No venture");
        }
        
        ImGui.PopID();
    }

    private void DrawStatusIndicator(bool isConnected, string tooltip)
    {
        var color = isConnected ? ConnectedColor : DisconnectedColor;
        var icon = isConnected ? "●" : "○";
        
        ImGui.TextColored(color, icon);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    public override bool HasSettings => true;
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
        
        // Hidden characters management
        if (ImGui.CollapsingHeader("Hidden Characters"))
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
                
                // Build list of hidden characters with their names
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
                
                // Remove outside the loop to avoid modifying collection during iteration
                if (characterToUnhide.HasValue)
                {
                    HiddenCharacters.Remove(characterToUnhide.Value);
                    NotifyToolSettingsChanged();
                }
            }
            
            ImGui.Unindent();
        }
        
        // Color settings
        if (ImGui.CollapsingHeader("Colors"))
        {
            ImGui.Indent();
            
            // Text colors
            ImGui.TextUnformatted("Text Colors");
            ImGui.Spacing();
            
            var (readyChanged, newReady) = ImGuiHelpers.ColorPickerWithReset(
                "##readytext", ReadyColor, DefaultReadyColor, "Ready");
            if (readyChanged)
            {
                ReadyColor = newReady;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Ready");
            
            var (enabledChanged, newEnabled) = ImGuiHelpers.ColorPickerWithReset(
                "##enabledtext", EnabledColor, DefaultEnabledColor, "Enabled");
            if (enabledChanged)
            {
                EnabledColor = newEnabled;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Enabled");
            
            var (disabledChanged, newDisabled) = ImGuiHelpers.ColorPickerWithReset(
                "##disabledtext", DisabledColor, DefaultDisabledColor, "Disabled");
            if (disabledChanged)
            {
                DisabledColor = newDisabled;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Disabled");
            
            var (connectedChanged, newConnected) = ImGuiHelpers.ColorPickerWithReset(
                "##connectedtext", ConnectedColor, DefaultConnectedColor, "Connected/On");
            if (connectedChanged)
            {
                ConnectedColor = newConnected;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Connected/On");
            
            var (warningChanged, newWarning) = ImGuiHelpers.ColorPickerWithReset(
                "##warningtext", WarningColor, DefaultWarningColor, "Warning");
            if (warningChanged)
            {
                WarningColor = newWarning;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Warning");
            
            var (retainerChanged, newRetainer) = ImGuiHelpers.ColorPickerWithReset(
                "##retainertext", RetainerColor, DefaultRetainerColor, "Retainer/Vessel Name");
            if (retainerChanged)
            {
                RetainerColor = newRetainer;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Retainer/Vessel Name");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Header colors
            ImGui.TextUnformatted("Header Progress Colors");
            ImGui.Spacing();
            
            var (progressReadyChanged, newProgressReady) = ImGuiHelpers.ColorPickerWithReset(
                "##readyheader", ProgressBarReadyColor, DefaultProgressBarReadyColor, "Ready (green)");
            if (progressReadyChanged)
            {
                ProgressBarReadyColor = newProgressReady;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Ready (green)");
            
            var (progressChanged, newProgress) = ImGuiHelpers.ColorPickerWithReset(
                "##inprogressheader", ProgressBarColor, DefaultProgressBarColor, "In Progress (red)");
            if (progressChanged)
            {
                ProgressBarColor = newProgress;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("In Progress (red)");
            
            ImGui.Unindent();
        }
    }

    /// <summary>
    /// Calculates the overall progress for a character based on the closest venture/voyage to completion.
    /// </summary>
    /// <param name="character">The character data.</param>
    /// <param name="nowUnix">Current unix timestamp.</param>
    /// <param name="hasAnythingReady">Output: true if any venture/voyage is ready.</param>
    /// <returns>Progress from 0.0 to 1.0, where 1.0 means ready/complete.</returns>
    private static float CalculateCharacterProgress(AutoRetainerCharacterData character, long nowUnix, out bool hasAnythingReady)
    {
        hasAnythingReady = false;
        
        // Find the closest venture/voyage to completion
        long closestEndTime = long.MaxValue;
        long maxDuration = 0;
        
        // Check retainers
        foreach (var retainer in character.Retainers)
        {
            if (!retainer.HasVenture) continue;
            
            if (retainer.VentureEndsAt <= nowUnix)
            {
                hasAnythingReady = true;
                return 1.0f; // Something is ready, show full bar
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
                return 1.0f; // Something is ready, show full bar
            }
            
            if (vessel.ReturnTime < closestEndTime)
            {
                closestEndTime = vessel.ReturnTime;
                maxDuration = MaxVoyageDuration;
            }
        }
        
        // No active ventures/voyages
        if (closestEndTime == long.MaxValue)
            return 0.0f;
        
        // Calculate progress based on time remaining
        var secondsRemaining = closestEndTime - nowUnix;
        var progress = 1.0f - (float)secondsRemaining / maxDuration;
        
        return Math.Clamp(progress, 0.0f, 1.0f);
    }

    /// <summary>
    /// Draws a collapsing header with a progress bar fill inside the header background.
    /// Uses the same technique as AutoRetainer: draw a ProgressBar, then overlay the CollapsingHeader.
    /// </summary>
    /// <param name="label">The header label text.</param>
    /// <param name="progress">Progress from 0.0 to 1.0.</param>
    /// <param name="isReady">Whether something is ready (uses brighter color).</param>
    /// <param name="defaultOpen">Whether the header should be open by default.</param>
    /// <param name="progressColor">Color for in-progress state.</param>
    /// <param name="readyColor">Color for ready state.</param>
    /// <returns>True if the header is expanded.</returns>
    private static bool DrawProgressCollapsingHeader(string label, float progress, bool isReady, bool defaultOpen, Vector4 progressColor, Vector4 readyColor)
    {
        // Save cursor position before drawing progress bar
        var initCursorPos = ImGui.GetCursorPos();
        
        // Calculate header height (same as CollapsingHeader)
        var headerHeight = ImGui.CalcTextSize("A").Y + ImGui.GetStyle().FramePadding.Y * 2;
        
        // Draw progress bar as background
        if (progress > 0.0f)
        {
            // Use color based on ready state
            var barColor = isReady ? readyColor : progressColor;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0)); // Transparent frame background
            ImGui.ProgressBar(progress, new Vector2(ImGui.GetContentRegionAvail().X, headerHeight), "");
            ImGui.PopStyleColor(2);
        }
        
        // Reset cursor to draw CollapsingHeader on top of the progress bar
        ImGui.SetCursorPos(initCursorPos);
        
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        var isOpen = ImGui.CollapsingHeader(label, flags);
        
        return isOpen;
    }
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["ShowCharacterList"] = ShowCharacterList,
            ["ShowControls"] = ShowControls,
            ["ShowGil"] = ShowGil,
            ["HiddenCharacters"] = HiddenCharacters.ToList(),
            ["ConnectedColorR"] = ConnectedColor.X,
            ["ConnectedColorG"] = ConnectedColor.Y,
            ["ConnectedColorB"] = ConnectedColor.Z,
            ["ConnectedColorA"] = ConnectedColor.W,
            ["DisconnectedColorR"] = DisconnectedColor.X,
            ["DisconnectedColorG"] = DisconnectedColor.Y,
            ["DisconnectedColorB"] = DisconnectedColor.Z,
            ["DisconnectedColorA"] = DisconnectedColor.W,
            ["WarningColorR"] = WarningColor.X,
            ["WarningColorG"] = WarningColor.Y,
            ["WarningColorB"] = WarningColor.Z,
            ["WarningColorA"] = WarningColor.W,
            ["ReadyColorR"] = ReadyColor.X,
            ["ReadyColorG"] = ReadyColor.Y,
            ["ReadyColorB"] = ReadyColor.Z,
            ["ReadyColorA"] = ReadyColor.W,
            ["DisabledColorR"] = DisabledColor.X,
            ["DisabledColorG"] = DisabledColor.Y,
            ["DisabledColorB"] = DisabledColor.Z,
            ["DisabledColorA"] = DisabledColor.W,
            ["EnabledColorR"] = EnabledColor.X,
            ["EnabledColorG"] = EnabledColor.Y,
            ["EnabledColorB"] = EnabledColor.Z,
            ["EnabledColorA"] = EnabledColor.W,
            ["HeaderColorR"] = HeaderColor.X,
            ["HeaderColorG"] = HeaderColor.Y,
            ["HeaderColorB"] = HeaderColor.Z,
            ["HeaderColorA"] = HeaderColor.W,
            ["RetainerColorR"] = RetainerColor.X,
            ["RetainerColorG"] = RetainerColor.Y,
            ["RetainerColorB"] = RetainerColor.Z,
            ["RetainerColorA"] = RetainerColor.W,
            ["ProgressBarColorR"] = ProgressBarColor.X,
            ["ProgressBarColorG"] = ProgressBarColor.Y,
            ["ProgressBarColorB"] = ProgressBarColor.Z,
            ["ProgressBarColorA"] = ProgressBarColor.W,
            ["ProgressBarReadyColorR"] = ProgressBarReadyColor.X,
            ["ProgressBarReadyColorG"] = ProgressBarReadyColor.Y,
            ["ProgressBarReadyColorB"] = ProgressBarReadyColor.Z,
            ["ProgressBarReadyColorA"] = ProgressBarReadyColor.W
        };
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
        
        var hiddenChars = GetSetting<List<ulong>>(settings, "HiddenCharacters", null);
        if (hiddenChars != null)
        {
            HiddenCharacters = new HashSet<ulong>(hiddenChars);
        }
        
        ConnectedColor = new Vector4(
            GetSetting(settings, "ConnectedColorR", ConnectedColor.X),
            GetSetting(settings, "ConnectedColorG", ConnectedColor.Y),
            GetSetting(settings, "ConnectedColorB", ConnectedColor.Z),
            GetSetting(settings, "ConnectedColorA", ConnectedColor.W));
        
        DisconnectedColor = new Vector4(
            GetSetting(settings, "DisconnectedColorR", DisconnectedColor.X),
            GetSetting(settings, "DisconnectedColorG", DisconnectedColor.Y),
            GetSetting(settings, "DisconnectedColorB", DisconnectedColor.Z),
            GetSetting(settings, "DisconnectedColorA", DisconnectedColor.W));
        
        WarningColor = new Vector4(
            GetSetting(settings, "WarningColorR", WarningColor.X),
            GetSetting(settings, "WarningColorG", WarningColor.Y),
            GetSetting(settings, "WarningColorB", WarningColor.Z),
            GetSetting(settings, "WarningColorA", WarningColor.W));
        
        ReadyColor = new Vector4(
            GetSetting(settings, "ReadyColorR", ReadyColor.X),
            GetSetting(settings, "ReadyColorG", ReadyColor.Y),
            GetSetting(settings, "ReadyColorB", ReadyColor.Z),
            GetSetting(settings, "ReadyColorA", ReadyColor.W));
        
        DisabledColor = new Vector4(
            GetSetting(settings, "DisabledColorR", DisabledColor.X),
            GetSetting(settings, "DisabledColorG", DisabledColor.Y),
            GetSetting(settings, "DisabledColorB", DisabledColor.Z),
            GetSetting(settings, "DisabledColorA", DisabledColor.W));
        
        EnabledColor = new Vector4(
            GetSetting(settings, "EnabledColorR", EnabledColor.X),
            GetSetting(settings, "EnabledColorG", EnabledColor.Y),
            GetSetting(settings, "EnabledColorB", EnabledColor.Z),
            GetSetting(settings, "EnabledColorA", EnabledColor.W));
        
        HeaderColor = new Vector4(
            GetSetting(settings, "HeaderColorR", HeaderColor.X),
            GetSetting(settings, "HeaderColorG", HeaderColor.Y),
            GetSetting(settings, "HeaderColorB", HeaderColor.Z),
            GetSetting(settings, "HeaderColorA", HeaderColor.W));
        
        RetainerColor = new Vector4(
            GetSetting(settings, "RetainerColorR", RetainerColor.X),
            GetSetting(settings, "RetainerColorG", RetainerColor.Y),
            GetSetting(settings, "RetainerColorB", RetainerColor.Z),
            GetSetting(settings, "RetainerColorA", RetainerColor.W));
        
        ProgressBarColor = new Vector4(
            GetSetting(settings, "ProgressBarColorR", ProgressBarColor.X),
            GetSetting(settings, "ProgressBarColorG", ProgressBarColor.Y),
            GetSetting(settings, "ProgressBarColorB", ProgressBarColor.Z),
            GetSetting(settings, "ProgressBarColorA", ProgressBarColor.W));
        
        ProgressBarReadyColor = new Vector4(
            GetSetting(settings, "ProgressBarReadyColorR", ProgressBarReadyColor.X),
            GetSetting(settings, "ProgressBarReadyColorG", ProgressBarReadyColor.Y),
            GetSetting(settings, "ProgressBarReadyColorB", ProgressBarReadyColor.Z),
            GetSetting(settings, "ProgressBarReadyColorA", ProgressBarReadyColor.W));
    }
}
