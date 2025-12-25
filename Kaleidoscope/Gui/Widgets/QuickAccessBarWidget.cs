using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Kaleidoscope;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// A floating quick-access toolbar that appears when CTRL+ALT is held.
/// Provides buttons for Edit, Lock, Fullscreen, Save (when dirty), and integration status indicators.
/// </summary>
public sealed class QuickAccessBarWidget
{
    private readonly StateService _stateService;
    private readonly LayoutEditingService _layoutEditingService;
    private readonly ConfigurationService? _configurationService;
    private readonly SamplerService? _samplerService;
    private readonly UniversalisWebSocketService? _webSocketService;
    private readonly AutoRetainerIpcService? _autoRetainerService;
    private readonly Action? _onFullscreenToggle;
    private readonly Action? _onSave;
    private readonly Action? _onOpenSettings;
    private readonly Func<bool>? _onExitEditModeWithDirtyCheck;
    private readonly Action<string>? _onLayoutChanged;

    private const float BarHeight = 32f;
    private const float ButtonWidth = 28f;
    private const float ButtonSpacing = 4f;
    private const float BarPadding = 8f;
    private const float StatusIndicatorSize = 10f;
    private const float StatusSpacing = 6f;
    private const float SeparatorWidth = 1f;
    private const float SeparatorMargin = 8f;
    private const uint BarBackgroundColor = 0xDD1A1A1A; // Dark semi-transparent
    private const uint ButtonHoverColor = 0xFF3A3A3A;
    private const uint ButtonActiveColor = 0xFF505050;
    private const uint SaveButtonColor = 0xFF2A5A2A; // Green tint for save when dirty
    private const uint SaveButtonHoverColor = 0xFF3A7A3A;
    private const uint StatusConnectedColor = 0xFF00CC00; // Green
    private const uint StatusDisconnectedColor = 0xFF0000CC; // Red
    private const uint StatusWarningColor = 0xFF00AAFF; // Orange/Yellow
    private const uint SeparatorColor = 0xFF505050;
    private const uint PinActiveColor = 0xFF00CC00; // Green when pinned
    private const uint PinInactiveColor = 0xFF808080; // Gray when not pinned
    private const float AnimationDuration = 0.1f; // 0.1 second dropdown animation
    private const float TopOffset = 2f; // Reduced spacing from top

    // Pin and animation state
    private bool _isPinned = false;
    private float _animationProgress = 0f;
    private DateTime _animationStartTime = DateTime.MinValue;
    private bool _isAnimatingIn = false;
    private bool _isAnimatingOut = false;
    private bool _wasVisible = false;

    /// <summary>
    /// Whether the bar is pinned (stays visible without holding CTRL+ALT).
    /// </summary>
    public bool IsPinned
    {
        get => _isPinned;
        set => _isPinned = value;
    }

    /// <summary>
    /// Creates a new quick access bar widget.
    /// </summary>
    /// <param name="stateService">State service for edit/lock/fullscreen state.</param>
    /// <param name="layoutEditingService">Layout editing service for dirty state.</param>
    /// <param name="configurationService">Configuration service for layout access (optional).</param>
    /// <param name="samplerService">Sampler service for database status (optional).</param>
    /// <param name="webSocketService">WebSocket service for Universalis connection status (optional).</param>
    /// <param name="autoRetainerService">AutoRetainer IPC service for plugin integration status (optional).</param>
    /// <param name="onFullscreenToggle">Callback to toggle fullscreen mode.</param>
    /// <param name="onSave">Callback to save the layout.</param>
    /// <param name="onOpenSettings">Callback to open settings window.</param>
    /// <param name="onExitEditModeWithDirtyCheck">Callback when toggling edit mode off with dirty state. Returns true if handled (e.g., showing dialog).</param>
    /// <param name="onLayoutChanged">Callback when user selects a different layout from the dropdown.</param>
    public QuickAccessBarWidget(
        StateService stateService,
        LayoutEditingService layoutEditingService,
        ConfigurationService? configurationService = null,
        SamplerService? samplerService = null,
        UniversalisWebSocketService? webSocketService = null,
        AutoRetainerIpcService? autoRetainerService = null,
        Action? onFullscreenToggle = null,
        Action? onSave = null,
        Action? onOpenSettings = null,
        Func<bool>? onExitEditModeWithDirtyCheck = null,
        Action<string>? onLayoutChanged = null)
    {
        _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        _layoutEditingService = layoutEditingService ?? throw new ArgumentNullException(nameof(layoutEditingService));
        _configurationService = configurationService;
        _samplerService = samplerService;
        _webSocketService = webSocketService;
        _autoRetainerService = autoRetainerService;
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
        var now = DateTime.Now;
        
        // Show when CTRL+ALT is held (but not SHIFT) OR when pinned
        var keyComboHeld = io.KeyCtrl && io.KeyAlt && !io.KeyShift;
        var shouldBeVisible = keyComboHeld || _isPinned;
        
        // Handle animation state transitions
        if (shouldBeVisible && !_wasVisible)
        {
            // Start animating in
            _isAnimatingIn = true;
            _isAnimatingOut = false;
            _animationStartTime = now;
        }
        else if (!shouldBeVisible && _wasVisible)
        {
            // Start animating out
            _isAnimatingOut = true;
            _isAnimatingIn = false;
            _animationStartTime = now;
        }
        
        // Update animation progress
        if (_isAnimatingIn)
        {
            var elapsed = (float)(now - _animationStartTime).TotalSeconds;
            _animationProgress = Math.Min(1f, elapsed / AnimationDuration);
            if (_animationProgress >= 1f)
            {
                _isAnimatingIn = false;
                _animationProgress = 1f;
            }
        }
        else if (_isAnimatingOut)
        {
            var elapsed = (float)(now - _animationStartTime).TotalSeconds;
            _animationProgress = Math.Max(0f, 1f - (elapsed / AnimationDuration));
            if (_animationProgress <= 0f)
            {
                _isAnimatingOut = false;
                _animationProgress = 0f;
            }
        }
        else if (shouldBeVisible)
        {
            _animationProgress = 1f;
        }
        else
        {
            _animationProgress = 0f;
        }
        
        // Only track the shouldBeVisible state, not animation progress
        // This prevents the animation from restarting every frame
        _wasVisible = shouldBeVisible;
        
        // Don't draw if fully hidden
        if (_animationProgress <= 0f)
            return false;

        // Calculate bar dimensions
        var isDirty = _layoutEditingService.IsDirty;
        var buttonCount = isDirty ? 6 : 5; // Pin, Edit, Lock, Fullscreen, Settings, and optionally Save
        
        // Count status indicators (only show if services are provided)
        var statusCount = 0;
        if (_samplerService != null) statusCount++;
        if (_webSocketService != null) statusCount++;
        if (_autoRetainerService != null) statusCount++;
        
        // Get character name or titlescreen text
        var isLoggedIn = GameStateService.PlayerContentId != 0;
        var characterText = isLoggedIn 
            ? (GameStateService.LocalPlayerName ?? "Unknown") 
            : "In Titlescreen";
        var characterTextSize = ImGui.CalcTextSize(characterText);
        
        // Get layout dropdown width if config service is available
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
        
        var buttonsWidth = (ButtonWidth * buttonCount) + (ButtonSpacing * (buttonCount - 1));
        var statusWidth = statusCount > 0 ? (StatusIndicatorSize * statusCount) + (StatusSpacing * (statusCount - 1)) : 0f;
        var characterWidth = characterTextSize.X;
        var separatorSpace = SeparatorMargin * 2 + SeparatorWidth;
        
        // Calculate total bar width: buttons | layout | character | indicators (with separators between each)
        var layoutSectionWidth = (hasLayoutDropdown && filteredLayouts.Count > 0) ? layoutDropdownWidth + separatorSpace : 0f;
        var characterSectionWidth = characterWidth + separatorSpace;
        var statusSectionWidth = statusCount > 0 ? statusWidth + separatorSpace : 0f;
        var barWidth = buttonsWidth + layoutSectionWidth + characterSectionWidth + statusSectionWidth + (BarPadding * 2);
        
        // Position at top center of the current window with reduced spacing
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var contentMin = ImGui.GetWindowContentRegionMin();
        
        // Apply ease-out animation for dropdown effect
        var easeProgress = 1f - (float)Math.Pow(1f - _animationProgress, 2);
        var animationOffset = BarHeight * (1f - easeProgress);
        
        var barPos = new Vector2(
            windowPos.X + (windowSize.X - barWidth) / 2f, 
            windowPos.Y + contentMin.Y + TopOffset - animationOffset);
        
        // Draw bar background using the window's draw list
        var dl = ImGui.GetWindowDrawList();
        var barMin = barPos;
        var barMax = barPos + new Vector2(barWidth, BarHeight);
        dl.AddRectFilled(barMin, barMax, BarBackgroundColor, 6f);
        dl.AddRect(barMin, barMax, 0xFF404040, 6f, ImDrawFlags.None, 1f);

        // Button positions
        var buttonY = barPos.Y + (BarHeight - ButtonWidth) / 2f;
        var currentX = barPos.X + BarPadding;

        // Pin Button
        DrawPinButton(dl, ref currentX, buttonY);
        currentX += ButtonSpacing;

        // Edit Mode Button
        DrawIconButton(dl, ref currentX, buttonY, 
            _stateService.IsEditMode ? FontAwesomeIcon.Edit : FontAwesomeIcon.Edit,
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

        // Lock Button
        DrawIconButton(dl, ref currentX, buttonY,
            _stateService.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            _stateService.IsLocked ? "Unlock Window" : "Lock Window",
            _stateService.IsLocked,
            false,
            () => _stateService.ToggleLocked());

        currentX += ButtonSpacing;

        // Fullscreen Button
        DrawIconButton(dl, ref currentX, buttonY,
            _stateService.IsFullscreen ? FontAwesomeIcon.Compress : FontAwesomeIcon.Expand,
            _stateService.IsFullscreen ? "Exit Fullscreen" : "Enter Fullscreen",
            _stateService.IsFullscreen,
            false,
            () => _onFullscreenToggle?.Invoke());

        currentX += ButtonSpacing;

        // Settings Button
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

        // Draw separator and character/status indicators
        currentX += SeparatorMargin;
        
        // Draw vertical separator
        var separatorTop = barPos.Y + 6f;
        var separatorBottom = barPos.Y + BarHeight - 6f;
        
        // Layout dropdown (if configuration service is available and layouts exist)
        if (hasLayoutDropdown && filteredLayouts.Count > 0)
        {
            // Draw separator before layout dropdown
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
        
        // Draw separator before character name
        dl.AddLine(
            new Vector2(currentX, separatorTop),
            new Vector2(currentX, separatorBottom),
            SeparatorColor,
            SeparatorWidth);
        currentX += SeparatorWidth + SeparatorMargin;
        
        // Draw character name or titlescreen text
        var textY = barPos.Y + (BarHeight - characterTextSize.Y) / 2f;
        var textColor = isLoggedIn ? 0xFF80FF80u : 0xFF80FF80u; // Light green for both states
        dl.AddText(new Vector2(currentX, textY), textColor, characterText);
        currentX += characterTextSize.X;
        
        // Status indicators (if any services are provided)
        if (statusCount > 0)
        {
            // Draw separator before status indicators
            currentX += SeparatorMargin;
            dl.AddLine(
                new Vector2(currentX, separatorTop),
                new Vector2(currentX, separatorBottom),
                SeparatorColor,
                SeparatorWidth);
            currentX += SeparatorWidth + SeparatorMargin;
            // Status indicator Y position (centered vertically)
            var statusY = barPos.Y + (BarHeight - StatusIndicatorSize) / 2f;
            
            // Database status
            if (_samplerService != null)
            {
                var hasDb = _samplerService.HasDb;
                DrawStatusIndicator(dl, ref currentX, statusY,
                    hasDb ? StatusConnectedColor : StatusDisconnectedColor,
                    hasDb ? "Database: Connected" : "Database: Unavailable");
                currentX += StatusSpacing;
            }
            
            // WebSocket status
            if (_webSocketService != null)
            {
                var isWsConnected = _webSocketService.IsConnected;
                DrawStatusIndicator(dl, ref currentX, statusY,
                    isWsConnected ? StatusConnectedColor : StatusWarningColor,
                    isWsConnected ? "Universalis: Connected" : "Universalis: Disconnected");
                if (_autoRetainerService != null)
                    currentX += StatusSpacing;
            }
            
            // AutoRetainer status
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
    
    private void DrawStatusIndicator(ImDrawListPtr dl, ref float x, float y, uint color, string tooltip)
    {
        var center = new Vector2(x + StatusIndicatorSize / 2f, y + StatusIndicatorSize / 2f);
        var radius = StatusIndicatorSize / 2f;
        
        // Draw filled circle
        dl.AddCircleFilled(center, radius, color, 12);
        
        // Draw subtle border
        dl.AddCircle(center, radius, 0xFF606060, 12, 1f);
        
        // Check for hover and show tooltip
        var mousePos = ImGui.GetMousePos();
        var minPos = new Vector2(x, y);
        var maxPos = new Vector2(x + StatusIndicatorSize, y + StatusIndicatorSize);
        var isHovered = mousePos.X >= minPos.X && mousePos.X <= maxPos.X &&
                        mousePos.Y >= minPos.Y && mousePos.Y <= maxPos.Y;
        
        if (isHovered)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }
        
        x += StatusIndicatorSize;
    }

    private void DrawPinButton(ImDrawListPtr dl, ref float x, float y)
    {
        var buttonMin = new Vector2(x, y);
        var buttonMax = buttonMin + new Vector2(ButtonWidth, ButtonWidth);
        var mousePos = ImGui.GetMousePos();
        var isHovered = mousePos.X >= buttonMin.X && mousePos.X <= buttonMax.X &&
                        mousePos.Y >= buttonMin.Y && mousePos.Y <= buttonMax.Y;

        // Determine button color
        uint bgColor;
        if (_isPinned)
            bgColor = ButtonActiveColor;
        else if (isHovered)
            bgColor = ButtonHoverColor;
        else
            bgColor = 0x00000000; // Transparent when not hovered

        // Draw button background
        if (bgColor != 0)
            dl.AddRectFilled(buttonMin, buttonMax, bgColor, 4f);

        // Draw pin icon using FontAwesome
        var icon = _isPinned ? FontAwesomeIcon.Thumbtack : FontAwesomeIcon.Thumbtack;
        var iconText = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        try
        {
            var textSize = ImGui.CalcTextSize(iconText);
            var textPos = buttonMin + (new Vector2(ButtonWidth, ButtonWidth) - textSize) / 2f;
            var textColor = _isPinned ? PinActiveColor : PinInactiveColor;
            dl.AddText(textPos, textColor, iconText);
        }
        finally
        {
            ImGui.PopFont();
        }

        // Handle click
        if (isHovered)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _isPinned = !_isPinned;
            }

            // Show tooltip
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(_isPinned ? "Unpin (hide when CTRL+ALT released)" : "Pin (keep visible)");
            ImGui.EndTooltip();
        }

        x += ButtonWidth;
    }

    private void DrawIconButton(ImDrawListPtr dl, ref float x, float y, FontAwesomeIcon icon, string tooltip, bool isActive, bool isSaveButton, Action onClick)
    {
        var buttonMin = new Vector2(x, y);
        var buttonMax = buttonMin + new Vector2(ButtonWidth, ButtonWidth);
        var mousePos = ImGui.GetMousePos();
        var isHovered = mousePos.X >= buttonMin.X && mousePos.X <= buttonMax.X &&
                        mousePos.Y >= buttonMin.Y && mousePos.Y <= buttonMax.Y;

        // Determine button color
        uint bgColor;
        if (isSaveButton)
            bgColor = isHovered ? SaveButtonHoverColor : SaveButtonColor;
        else if (isActive)
            bgColor = ButtonActiveColor;
        else if (isHovered)
            bgColor = ButtonHoverColor;
        else
            bgColor = 0x00000000; // Transparent when not hovered

        // Draw button background
        if (bgColor != 0)
            dl.AddRectFilled(buttonMin, buttonMax, bgColor, 4f);

        // Draw icon
        var iconText = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        try
        {
            var textSize = ImGui.CalcTextSize(iconText);
            var textPos = buttonMin + (new Vector2(ButtonWidth, ButtonWidth) - textSize) / 2f;
            var textColor = isActive ? 0xFF00FF00u : 0xFFFFFFFFu; // Green if active, white otherwise
            if (isSaveButton)
                textColor = 0xFF80FF80u; // Light green for save icon
            dl.AddText(textPos, textColor, iconText);
        }
        finally
        {
            ImGui.PopFont();
        }

        // Handle click
        if (isHovered)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                onClick();

            // Show tooltip using invisible button for tooltip support
            ImGui.SetNextWindowPos(buttonMin);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }

        x += ButtonWidth;
    }
}
