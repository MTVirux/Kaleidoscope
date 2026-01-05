using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Preset options for the frame limiter dropdown.
/// </summary>
public enum FrameLimiterPreset
{
    Disabled = 0,
    Fps30 = 30,
    Fps60 = 60,
    Fps75 = 75,
    Fps90 = 90,
    Fps144 = 144,
    Fps240 = 240,
    Custom = -1
}

/// <summary>
/// General settings category in the config window.
/// </summary>
public sealed class GeneralCategory
{
    private readonly ConfigurationService _configService;
    private readonly FrameLimiterService _frameLimiterService;
    private readonly IUiBuilder _uiBuilder;

    private Configuration Config => _configService.Config;
    
    // Custom FPS input buffer (not applied until user clicks Apply)
    private int _customFpsInput = 60;
    private bool _customFpsInputInitialized = false;
    
    // Dropdown items in display order
    private static readonly string[] FrameLimiterOptions = 
    {
        "Custom",
        "240 FPS",
        "144 FPS",
        "90 FPS",
        "75 FPS",
        "60 FPS",
        "30 FPS",
        "Disabled"
    };
    
    // Map dropdown index to preset value
    private static readonly FrameLimiterPreset[] PresetValues =
    {
        FrameLimiterPreset.Custom,
        FrameLimiterPreset.Fps240,
        FrameLimiterPreset.Fps144,
        FrameLimiterPreset.Fps90,
        FrameLimiterPreset.Fps75,
        FrameLimiterPreset.Fps60,
        FrameLimiterPreset.Fps30,
        FrameLimiterPreset.Disabled
    };

    public GeneralCategory(ConfigurationService configService, FrameLimiterService frameLimiterService, IUiBuilder uiBuilder)
    {
        _configService = configService;
        _frameLimiterService = frameLimiterService;
        _uiBuilder = uiBuilder;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("General");
        ImGui.Separator();
        var showOnStart = Config.ShowOnStart;
        if (ImGui.Checkbox("Show on start", ref showOnStart))
        {
            Config.ShowOnStart = showOnStart;
            _configService.MarkDirty();
        }

        var exclusiveFs = Config.ExclusiveFullscreen;
        if (ImGui.Checkbox("Exclusive fullscreen", ref exclusiveFs))
        {
            Config.ExclusiveFullscreen = exclusiveFs;
            _configService.MarkDirty();
        }

        var showDuringCutscenes = Config.ShowDuringCutscenes;
        if (ImGui.Checkbox("Show during cutscenes", ref showDuringCutscenes))
        {
            Config.ShowDuringCutscenes = showDuringCutscenes;
            _configService.MarkDirty();
            // Apply immediately - set Dalamud's UI hide settings
            _uiBuilder.DisableCutsceneUiHide = showDuringCutscenes;
            _uiBuilder.DisableGposeUiHide = showDuringCutscenes;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Keep the Kaleidoscope window visible during cutscenes and GPose.\nOverrides Dalamud's \"Hide plugin UI during cutscenes\" setting for this plugin.");
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Frame Limiter section
        ImGui.TextUnformatted("Frame Limiter");
        ImGui.Separator();
        
        // Determine current selection index
        var currentIndex = GetCurrentPresetIndex();
        
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("Target FPS##FrameLimiter", ref currentIndex, FrameLimiterOptions, FrameLimiterOptions.Length))
        {
            ApplyPreset(PresetValues[currentIndex]);
        }
        ImGui.SameLine();
        HelpMarker("Limits the game's framerate to reduce GPU usage and heat.\nDisables ChillFrames automatically when enabled.");
        
        // Show custom FPS input when Custom is selected
        if (currentIndex == 0) // Custom
        {
            // Initialize input buffer from current value on first draw
            if (!_customFpsInputInitialized)
            {
                _customFpsInput = _frameLimiterService.TargetFramerate;
                _customFpsInputInitialized = true;
            }
            
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##CustomFPS", ref _customFpsInput);
            _customFpsInput = Math.Clamp(_customFpsInput, 10, 1000);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Enter a custom FPS target (10-1000)");
            }
            
            ImGui.SameLine();
            var hasChanges = _customFpsInput != _frameLimiterService.TargetFramerate;
            if (!hasChanges)
            {
                ImGui.BeginDisabled();
            }
            if (ImGui.Button("Apply##CustomFPS"))
            {
                _frameLimiterService.TargetFramerate = _customFpsInput;
            }
            if (!hasChanges)
            {
                ImGui.EndDisabled();
            }
        }
        else
        {
            // Reset initialization flag when not on Custom
            _customFpsInputInitialized = false;
        }
        
        // Show current status
        if (_frameLimiterService.IsEnabled)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f), 
                $"Active: {_frameLimiterService.CurrentFps:F0} FPS (Target: {_frameLimiterService.TargetFramerate})");
            
            if (_frameLimiterService.IsChillFramesAvailable)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(ChillFrames disabled)");;
            }
        }
    }
    
    /// <summary>
    /// Gets the dropdown index for the current frame limiter state.
    /// </summary>
    private int GetCurrentPresetIndex()
    {
        if (!_frameLimiterService.IsEnabled)
            return 7; // Disabled
        
        // If user explicitly selected Custom, show Custom
        if (Config.FrameLimiterUseCustom)
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
    /// Applies a frame limiter preset.
    /// </summary>
    private void ApplyPreset(FrameLimiterPreset preset)
    {
        if (preset == FrameLimiterPreset.Disabled)
        {
            _frameLimiterService.IsEnabled = false;
            Config.FrameLimiterUseCustom = false;
            _configService.MarkDirty();
        }
        else if (preset == FrameLimiterPreset.Custom)
        {
            // Enable Custom mode - keep current FPS or default to 60
            Config.FrameLimiterUseCustom = true;
            if (!_frameLimiterService.IsEnabled)
            {
                _frameLimiterService.TargetFramerate = _frameLimiterService.TargetFramerate > 0 
                    ? _frameLimiterService.TargetFramerate 
                    : 60;
            }
            _frameLimiterService.IsEnabled = true;
            _configService.MarkDirty();
        }
        else
        {
            // Selecting a preset clears Custom mode
            Config.FrameLimiterUseCustom = false;
            _frameLimiterService.TargetFramerate = (int)preset;
            _frameLimiterService.IsEnabled = true;
            _configService.MarkDirty();
        }
    }

    private static void HelpMarker(string desc)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
