using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// A tool that provides control over AutoRetainer via IPC.
/// Displays status information and provides buttons to control AutoRetainer functions.
/// </summary>
/// <remarks>
/// This is a partial class split across multiple files:
/// - AutoRetainerControlTool.cs: Core setup, fields, constructor, main rendering
/// - AutoRetainerControlTool.CharacterList.cs: Character list and entry rendering
/// - AutoRetainerControlTool.Settings.cs: Tool settings UI and import/export
/// </remarks>
public partial class AutoRetainerControlTool : ToolComponent
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
    
    // Configurable colors (private backing fields for settings partial)
    private Vector4 _connectedColor = DefaultConnectedColor;
    private Vector4 _disconnectedColor = DefaultDisconnectedColor;
    private Vector4 _warningColor = DefaultWarningColor;
    private Vector4 _readyColor = DefaultReadyColor;
    private Vector4 _disabledColor = DefaultDisabledColor;
    private Vector4 _enabledColor = DefaultEnabledColor;
    private Vector4 _headerColor = DefaultHeaderColor;
    private Vector4 _retainerColor = DefaultRetainerColor;
    private Vector4 _progressBarColor = DefaultProgressBarColor;
    private Vector4 _progressBarReadyColor = DefaultProgressBarReadyColor;
    
    // Color properties
    public Vector4 ConnectedColor { get => _connectedColor; set => _connectedColor = value; }
    public Vector4 DisconnectedColor { get => _disconnectedColor; set => _disconnectedColor = value; }
    public Vector4 WarningColor { get => _warningColor; set => _warningColor = value; }
    public Vector4 ReadyColor { get => _readyColor; set => _readyColor = value; }
    public Vector4 DisabledColor { get => _disabledColor; set => _disabledColor = value; }
    public Vector4 EnabledColor { get => _enabledColor; set => _enabledColor = value; }
    public Vector4 HeaderColor { get => _headerColor; set => _headerColor = value; }
    public Vector4 RetainerColor { get => _retainerColor; set => _retainerColor = value; }
    public Vector4 ProgressBarColor { get => _progressBarColor; set => _progressBarColor = value; }
    public Vector4 ProgressBarReadyColor { get => _progressBarReadyColor; set => _progressBarReadyColor = value; }
    
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
            LogDebug($"Draw error: {ex.Message}");
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
            LogDebug($"Refresh error: {ex.Message}");
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
}
