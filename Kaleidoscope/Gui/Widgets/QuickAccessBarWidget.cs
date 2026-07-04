using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Kaleidoscope;
using Kaleidoscope.Gui.Animation;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services.Universalis;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A floating quick-access toolbar that appears when CTRL+ALT is held.
/// Provides buttons for Edit, Lock, Fullscreen, Save (when dirty), and integration status indicators.
/// </summary>
public sealed class QuickAccessBarWidget
{
    private readonly StateService _stateService;
    private readonly LayoutEditingService _layoutEditingService;
    private readonly GameStateService _gameState;
    private readonly ConfigurationService? _configurationService;
    private readonly CurrencyTrackerService? _currencyTrackerService;
    private readonly UniversalisWebSocketService? _webSocketService;
    private readonly AutoRetainerService? _autoRetainerService;
    private readonly FrameLimiterService? _frameLimiterService;
    private readonly Action? _onFullscreenToggle;
    private readonly Action? _onSave;
    private readonly Action? _onOpenSettings;
    private readonly Func<bool>? _onExitEditModeWithDirtyCheck;
    private readonly Action<string>? _onLayoutChanged;
    
    private static readonly string[] FpsOptions = { "Custom", "240", "144", "90", "75", "60", "30", "Off" };
    private static readonly int[] FpsValues = { -1, 240, 144, 90, 75, 60, 30, 0 }; // -1 = custom, 0 = disabled

    private const float BarHeight = 32f;
    private const float ButtonWidth = 28f;
    private const float ButtonSpacing = 4f;
    private const float BarPadding = 8f;
    private const float StatusIndicatorSize = 10f;
    private const float StatusSpacing = 6f;
    private const float SeparatorWidth = 1f;
    private const float SeparatorMargin = 8f;
    private const uint DefaultBarBackgroundColor = 0xDD1A1A1A; // Dark semi-transparent fallback
    private const uint SaveButtonColor = 0xFF2A5A2A; // Green tint for save when dirty
    private const uint SaveButtonHoverColor = 0xFF3A7A3A;
    private const uint SaveIconColor = 0xFF80FF80; // Light green glyph for the save icon
    private const uint StatusConnectedColor = 0xFF00CC00; // Green
    private const uint StatusDisconnectedColor = 0xFF0000CC; // Red
    private const uint StatusWarningColor = 0xFF00AAFF; // Orange/Yellow
    private const uint DefaultSeparatorColor = 0xFF505050; // Fallback separator color
    private const uint PinActiveColor = 0xFF00CC00; // Green when pinned
    private const uint PinInactiveColor = 0xFF808080; // Gray when not pinned
    private const float AnimationDuration = 0.1f; // 0.1 second dropdown animation
    private const float TopOffset = 2f; // Reduced spacing from top

    /// <summary>Gets the bar background color from config, falling back to the default.</summary>
    private uint BarBackgroundColor => _configurationService != null
        ? ImGui.GetColorU32(_configurationService.Config.UIColors.QuickAccessBarBackground)
        : DefaultBarBackgroundColor;

    /// <summary>Gets the separator color from config, falling back to the default.</summary>
    private uint SeparatorColor => _configurationService != null
        ? ImGui.GetColorU32(_configurationService.Config.UIColors.QuickAccessBarSeparator)
        : DefaultSeparatorColor;

    private bool _isPinned = false;
    private readonly AnimationController _animator = new();
    private bool _wasVisible = false;

    /// <summary>
    /// Creates a new quick access bar widget.
    /// </summary>
    /// <param name="stateService">State service for edit/lock/fullscreen state.</param>
    /// <param name="layoutEditingService">Layout editing service for dirty state.</param>
    /// <param name="gameState">Game state service for player/login status.</param>
    /// <param name="configurationService">Configuration service for layout access (optional).</param>
    /// <param name="currencyTrackerService">Currency tracking service for database status (optional).</param>
    /// <param name="webSocketService">WebSocket service for Universalis connection status (optional).</param>
    /// <param name="autoRetainerService">AutoRetainer IPC service for plugin integration status (optional).</param>
    /// <param name="frameLimiterService">Frame limiter service for FPS control (optional).</param>
    /// <param name="onFullscreenToggle">Callback to toggle fullscreen mode.</param>
    /// <param name="onSave">Callback to save the layout.</param>
    /// <param name="onOpenSettings">Callback to open settings window.</param>
    /// <param name="onExitEditModeWithDirtyCheck">Callback when toggling edit mode off with dirty state. Returns true if handled (e.g., showing dialog).</param>
    /// <param name="onLayoutChanged">Callback when user selects a different layout from the dropdown.</param>
    public QuickAccessBarWidget(
        StateService stateService,
        LayoutEditingService layoutEditingService,
        GameStateService gameState,
        ConfigurationService? configurationService = null,
        CurrencyTrackerService? currencyTrackerService = null,
        UniversalisWebSocketService? webSocketService = null,
        AutoRetainerService? autoRetainerService = null,
        FrameLimiterService? frameLimiterService = null,
        Action? onFullscreenToggle = null,
        Action? onSave = null,
        Action? onOpenSettings = null,
        Func<bool>? onExitEditModeWithDirtyCheck = null,
        Action<string>? onLayoutChanged = null)
    {
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _layoutEditingService = layoutEditingService ?? throw new ArgumentNullException(nameof(layoutEditingService));
        _gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
        _configurationService = configurationService;
        _currencyTrackerService = currencyTrackerService;
        _webSocketService = webSocketService;
        _autoRetainerService = autoRetainerService;
        _frameLimiterService = frameLimiterService;
        _onFullscreenToggle = onFullscreenToggle;
        _onSave = onSave;
        _onOpenSettings = onOpenSettings;
        _onExitEditModeWithDirtyCheck = onExitEditModeWithDirtyCheck;
        _onLayoutChanged = onLayoutChanged;
    }

    /// <summary>
    /// Draws the quick access bar if CTRL+ALT is held or pinned.
    /// </summary>
    /// <returns>True if the bar was drawn.</returns>
    public bool Draw()
    {
        var io = ImGui.GetIO();
        
        // Show when CTRL+ALT is held (but not SHIFT) OR when pinned
        var keyComboHeld = io.KeyCtrl && io.KeyAlt && !io.KeyShift;
        var shouldBeVisible = keyComboHeld || _isPinned;
        
        // Update animation controller
        _animator.Update(io.DeltaTime);

        // Handle animation state transitions
        if (shouldBeVisible && !_wasVisible)
            _animator.Start("qab_alpha", 0f, 1f, AnimationDuration, Easing.QuadOut);
        else if (!shouldBeVisible && _wasVisible)
            _animator.Start("qab_alpha", 1f, 0f, AnimationDuration, Easing.QuadIn);
        
        _wasVisible = shouldBeVisible;
        
        // Resolve animation progress (1.0 when fully visible, 0.0 when hidden)
        var _animationProgress = shouldBeVisible && !_animator.IsAnimating("qab_alpha")
            ? 1f
            : _animator.Get("qab_alpha", shouldBeVisible ? 1f : 0f);
        
        // Don't draw if fully hidden
        if (_animationProgress <= 0f)
            return false;

        var isDirty = _layoutEditingService.IsDirty;
        var buttonCount = isDirty ? 6 : 5; // Pin, Edit, Lock, Fullscreen, Settings, and optionally Save
        
        var statusCount = 0;
        if (_currencyTrackerService != null) statusCount++;
        if (_webSocketService != null) statusCount++;
        if (_autoRetainerService != null) statusCount++;
        
        var isLoggedIn = _gameState.PlayerContentId != 0;
        var characterText = isLoggedIn
            ? (_gameState.LocalPlayerName ?? "Unknown")
            : "In Titlescreen";
        var characterTextSize = ImGui.CalcTextSize(characterText);
        
        var hasLayoutDropdown = _configurationService != null;
        var layouts = _configurationService?.Config.Layouts ?? new List<ContentLayoutState>();
        var isFullscreen = _stateService.IsFullscreen;
        var filteredLayouts = layouts.Where(l => l.Type == (isFullscreen ? LayoutType.Fullscreen : LayoutType.Windowed)).ToList();
        var currentLayoutName = isFullscreen 
            ? (_configurationService?.Config.ActiveFullscreenLayoutName ?? "")
            : (_configurationService?.Config.ActiveWindowedLayoutName ?? "");
        var layoutTextSize = hasLayoutDropdown && filteredLayouts.Count > 0 
            ? ImGui.CalcTextSize(currentLayoutName.Length > 0 ? currentLayoutName : "Layout") 
            : Vector2.Zero;
        var layoutDropdownWidth = hasLayoutDropdown && filteredLayouts.Count > 0 ? layoutTextSize.X + 30f : 0f; // Extra space for dropdown arrow
        
        var hasFpsDropdown = _frameLimiterService != null;
        var currentFpsText = GetCurrentFpsDisplayText();
        var fpsTextSize = hasFpsDropdown ? ImGui.CalcTextSize(currentFpsText) : Vector2.Zero;
        var fpsDropdownWidth = hasFpsDropdown ? fpsTextSize.X + 30f : 0f;
        
        var buttonsWidth = (ButtonWidth * buttonCount) + (ButtonSpacing * (buttonCount - 1));
        var statusWidth = statusCount > 0 ? (StatusIndicatorSize * statusCount) + (StatusSpacing * (statusCount - 1)) : 0f;
        var characterWidth = characterTextSize.X;
        var separatorSpace = SeparatorMargin * 2 + SeparatorWidth;
        
        // Calculate total bar width: buttons | layout | fps | character | indicators (with separators between each)
        var layoutSectionWidth = (hasLayoutDropdown && filteredLayouts.Count > 0) ? layoutDropdownWidth + separatorSpace : 0f;
        var fpsSectionWidth = hasFpsDropdown ? fpsDropdownWidth + separatorSpace : 0f;
        var characterSectionWidth = characterWidth + separatorSpace;
        var statusSectionWidth = statusCount > 0 ? statusWidth + separatorSpace : 0f;
        var barWidth = buttonsWidth + layoutSectionWidth + fpsSectionWidth + characterSectionWidth + statusSectionWidth + (BarPadding * 2);
        
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var contentMin = ImGui.GetWindowContentRegionMin();
        
        // _animationProgress is already eased by the AnimationController (QuadOut/QuadIn)
        var animationOffset = BarHeight * (1f - _animationProgress);
        
        var barPos = new Vector2(
            windowPos.X + (windowSize.X - barWidth) / 2f, 
            windowPos.Y + contentMin.Y + TopOffset - animationOffset);
        
        var dl = ImGui.GetWindowDrawList();
        var barMin = barPos;
        var barMax = barPos + new Vector2(barWidth, BarHeight);
        dl.AddRectFilled(barMin, barMax, BarBackgroundColor, 6f);
        dl.AddRect(barMin, barMax, 0xFF404040, 6f, ImDrawFlags.None, 1f);

        var buttonY = barPos.Y + (BarHeight - ButtonWidth) / 2f;
        var currentX = barPos.X + BarPadding;

        DrawPinButton(dl, ref currentX, buttonY);
        currentX += ButtonSpacing;

        DrawIconButton(dl, ref currentX, buttonY,
            _stateService.IsEditMode ? FontAwesomeIcon.Check : FontAwesomeIcon.Edit,
            _stateService.IsEditMode ? "Exit Edit Mode" : "Enter Edit Mode",
            _stateService.IsEditMode,
            false,
            () =>
            {
                if (_stateService.IsEditMode)
                {
                    // Check if we should handle dirty state
                    if (_layoutEditingService.IsDirty && _onExitEditModeWithDirtyCheck != null)
                    {
                        if (!_onExitEditModeWithDirtyCheck())
                            _stateService.ToggleEditMode();
                    }
                    else
                    {
                        _stateService.ToggleEditMode();
                    }
                }
                else
                {
                    _stateService.ToggleEditMode();
                }
            });

        currentX += ButtonSpacing;

        DrawIconButton(dl, ref currentX, buttonY,
            _stateService.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            _stateService.IsLocked ? "Unlock Window" : "Lock Window",
            _stateService.IsLocked,
            false,
            () => _stateService.ToggleLocked());

        currentX += ButtonSpacing;

        DrawIconButton(dl, ref currentX, buttonY,
            _stateService.IsFullscreen ? FontAwesomeIcon.Compress : FontAwesomeIcon.Expand,
            _stateService.IsFullscreen ? "Exit Fullscreen" : "Enter Fullscreen",
            _stateService.IsFullscreen,
            false,
            () => _onFullscreenToggle?.Invoke());

        currentX += ButtonSpacing;

        DrawIconButton(dl, ref currentX, buttonY,
            FontAwesomeIcon.Cog,
            "Open Settings",
            false,
            false,
            () => _onOpenSettings?.Invoke());

        // Save Button (only when dirty)
        if (isDirty)
        {
            currentX += ButtonSpacing;
            DrawIconButton(dl, ref currentX, buttonY,
                FontAwesomeIcon.Save,
                "Save Layout",
                false,
                true, // highlight as save button
                () => _onSave?.Invoke());
        }

        currentX += SeparatorMargin;
        
        var separatorTop = barPos.Y + 6f;
        var separatorBottom = barPos.Y + BarHeight - 6f;
        
        if (hasLayoutDropdown && filteredLayouts.Count > 0)
        {
            dl.AddLine(
                new Vector2(currentX, separatorTop),
                new Vector2(currentX, separatorBottom),
                SeparatorColor,
                SeparatorWidth);
            currentX += SeparatorWidth + SeparatorMargin;
            
            var comboY = barPos.Y + (BarHeight - 20f) / 2f;
            ImGui.SetCursorScreenPos(new Vector2(currentX, comboY));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
            ImGui.SetNextItemWidth(layoutDropdownWidth);
            if (ImGui.BeginCombo("##LayoutSelect", currentLayoutName, ImGuiComboFlags.NoArrowButton))
            {
                foreach (var layout in filteredLayouts)
                {
                    var isSelected = string.Equals(layout.Name, currentLayoutName, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(layout.Name, isSelected))
                    {
                        _onLayoutChanged?.Invoke(layout.Name);
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopStyleVar();
            currentX += layoutDropdownWidth + SeparatorMargin;
        }
        
        if (hasFpsDropdown)
        {
            dl.AddLine(
                new Vector2(currentX, separatorTop),
                new Vector2(currentX, separatorBottom),
                SeparatorColor,
                SeparatorWidth);
            currentX += SeparatorWidth + SeparatorMargin;
            
            var fpsComboY = barPos.Y + (BarHeight - 20f) / 2f;
            ImGui.SetCursorScreenPos(new Vector2(currentX, fpsComboY));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 2f));
            ImGui.SetNextItemWidth(fpsDropdownWidth);
            if (ImGui.BeginCombo("##FpsSelect", currentFpsText, ImGuiComboFlags.NoArrowButton))
            {
                for (var i = 0; i < FpsOptions.Length; i++)
                {
                    var isSelected = GetCurrentFpsIndex() == i;
                    if (ImGui.Selectable(FpsOptions[i], isSelected))
                    {
                        ApplyFpsSelection(i);
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopStyleVar();
            currentX += fpsDropdownWidth + SeparatorMargin;
        }
        
        dl.AddLine(
            new Vector2(currentX, separatorTop),
            new Vector2(currentX, separatorBottom),
            SeparatorColor,
            SeparatorWidth);
        currentX += SeparatorWidth + SeparatorMargin;
        
        var textY = barPos.Y + (BarHeight - characterTextSize.Y) / 2f;
        var textColor = isLoggedIn ? 0xFF80FF80u : 0xFFAAAAAAu; // Light green when logged in, gray when not
        dl.AddText(new Vector2(currentX, textY), textColor, characterText);
        currentX += characterTextSize.X;
        
        if (statusCount > 0)
        {
            currentX += SeparatorMargin;
            dl.AddLine(
                new Vector2(currentX, separatorTop),
                new Vector2(currentX, separatorBottom),
                SeparatorColor,
                SeparatorWidth);
            currentX += SeparatorWidth + SeparatorMargin;
            var statusY = barPos.Y + (BarHeight - StatusIndicatorSize) / 2f;
            
            if (_currencyTrackerService != null)
            {
                var hasDb = _currencyTrackerService.HasDb;
                DrawStatusIndicator(dl, ref currentX, statusY,
                    hasDb ? StatusConnectedColor : StatusDisconnectedColor,
                    hasDb ? "Database: Connected" : "Database: Unavailable");
                currentX += StatusSpacing;
            }
            
            if (_webSocketService != null)
            {
                var isWsConnected = _webSocketService.IsConnected;
                DrawStatusIndicator(dl, ref currentX, statusY,
                    isWsConnected ? StatusConnectedColor : StatusWarningColor,
                    isWsConnected ? "Universalis: Connected" : "Universalis: Disconnected");
                if (_autoRetainerService != null)
                    currentX += StatusSpacing;
            }
            
            if (_autoRetainerService != null)
            {
                var isArAvailable = _autoRetainerService.IsAvailable;
                DrawStatusIndicator(dl, ref currentX, statusY,
                    isArAvailable ? StatusConnectedColor : StatusWarningColor,
                    isArAvailable ? "AutoRetainer: Available" : "AutoRetainer: Unavailable");
            }
        }

        return true;
    }
    
    /// <summary>Inclusive rectangle hit-test against the current mouse position.</summary>
    private static bool IsMouseOverRect(Vector2 min, Vector2 max)
    {
        var mousePos = ImGui.GetMousePos();
        return mousePos.X >= min.X && mousePos.X <= max.X &&
               mousePos.Y >= min.Y && mousePos.Y <= max.Y;
    }

    private void DrawStatusIndicator(ImDrawListPtr dl, ref float x, float y, uint color, string tooltip)
    {
        var center = new Vector2(x + StatusIndicatorSize / 2f, y + StatusIndicatorSize / 2f);
        var radius = StatusIndicatorSize / 2f;

        dl.AddCircleFilled(center, radius, color, 12);
        dl.AddCircle(center, radius, UiColors.OutlineSubtleU32, 12, 1f);

        var minPos = new Vector2(x, y);
        var maxPos = new Vector2(x + StatusIndicatorSize, y + StatusIndicatorSize);
        if (IsMouseOverRect(minPos, maxPos))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }

        x += StatusIndicatorSize;
    }

    private void DrawPinButton(ImDrawListPtr dl, ref float x, float y)
    {
        var iconColor = _isPinned ? PinActiveColor : PinInactiveColor;
        DrawIconButtonCore(dl, ref x, y, FontAwesomeIcon.Thumbtack,
            _isPinned ? "Unpin (hide when CTRL+ALT released)" : "Pin (keep visible)",
            _isPinned, false, iconColor, positionTooltipAtButton: false,
            () => _isPinned = !_isPinned);
    }

    private void DrawIconButton(ImDrawListPtr dl, ref float x, float y, FontAwesomeIcon icon, string tooltip, bool isActive, bool isSaveButton, Action onClick)
    {
        var iconColor = isSaveButton
            ? SaveIconColor
            : (isActive ? UiColors.IconActiveU32 : UiColors.IconDefaultU32);
        DrawIconButtonCore(dl, ref x, y, icon, tooltip, isActive, isSaveButton, iconColor,
            positionTooltipAtButton: true, onClick);
    }

    /// <summary>
    /// Shared primitive for a fixed-size icon button: draws the hover/active/save background,
    /// centers the icon glyph, and handles hit-testing, click, and tooltip.
    /// </summary>
    private void DrawIconButtonCore(ImDrawListPtr dl, ref float x, float y, FontAwesomeIcon icon,
        string tooltip, bool isActive, bool isSaveButton, uint iconColor, bool positionTooltipAtButton, Action onClick)
    {
        var buttonMin = new Vector2(x, y);
        var buttonMax = buttonMin + new Vector2(ButtonWidth, ButtonWidth);
        var isHovered = IsMouseOverRect(buttonMin, buttonMax);

        uint bgColor;
        if (isSaveButton)
            bgColor = isHovered ? SaveButtonHoverColor : SaveButtonColor;
        else if (isActive)
            bgColor = UiColors.ButtonActiveU32;
        else if (isHovered)
            bgColor = UiColors.ButtonHoverU32;
        else
            bgColor = UiColors.TransparentU32;

        if (bgColor != UiColors.TransparentU32)
            dl.AddRectFilled(buttonMin, buttonMax, bgColor, 4f);

        var iconText = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        try
        {
            var textSize = ImGui.CalcTextSize(iconText);
            var textPos = buttonMin + (new Vector2(ButtonWidth, ButtonWidth) - textSize) / 2f;
            dl.AddText(textPos, iconColor, iconText);
        }
        finally
        {
            ImGui.PopFont();
        }

        if (isHovered)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                onClick();

            if (positionTooltipAtButton)
                ImGui.SetNextWindowPos(buttonMin);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }

        x += ButtonWidth;
    }
    
    /// <summary>
    /// Gets the display text for the current FPS setting.
    /// </summary>
    private string GetCurrentFpsDisplayText()
    {
        if (_frameLimiterService == null)
            return "FPS";
            
        if (!_frameLimiterService.IsEnabled)
            return "FPS: Off";
            
        var fps = _frameLimiterService.TargetFramerate;
        
        return $"FPS: {fps}";
    }
    
    /// <summary>
    /// Gets the current selection index in the FPS dropdown.
    /// </summary>
    private int GetCurrentFpsIndex()
    {
        if (_frameLimiterService == null || !_frameLimiterService.IsEnabled)
            return 7; // Off
            
        if (_configurationService?.Config.FrameLimiterUseCustom == true)
            return 0; // Custom
            
        var fps = _frameLimiterService.TargetFramerate;
        return fps switch
        {
            240 => 1,
            144 => 2,
            90 => 3,
            75 => 4,
            60 => 5,
            30 => 6,
            _ => 0 // Custom
        };
    }
    
    /// <summary>
    /// Applies the selected FPS preset.
    /// </summary>
    private void ApplyFpsSelection(int index)
    {
        if (_frameLimiterService == null || _configurationService == null)
            return;
            
        var value = FpsValues[index];
        
        if (value == 0)
        {
            // Off
            _frameLimiterService.IsEnabled = false;
            _configurationService.Config.FrameLimiterUseCustom = false;
            _configurationService.Save();
        }
        else if (value == -1)
        {
            // Custom - enable with current FPS
            _configurationService.Config.FrameLimiterUseCustom = true;
            _frameLimiterService.IsEnabled = true;
            _configurationService.Save();
        }
        else
        {
            // Preset value
            _configurationService.Config.FrameLimiterUseCustom = false;
            _frameLimiterService.TargetFramerate = value;
            _frameLimiterService.IsEnabled = true;
            _configurationService.Save();
        }
    }
}
