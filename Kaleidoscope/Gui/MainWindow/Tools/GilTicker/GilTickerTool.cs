using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.MainWindow.Tools.DataTracker;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.GilTicker;

/// <summary>
/// Tool component wrapper for the Gil Ticker feature.
/// Displays a scrolling ticker of character gil values.
/// Uses DataTrackerHelper with TrackedDataType.Gil for data access.
/// </summary>
public class GilTickerTool : ToolComponent
{
    private readonly GilTickerComponent _inner;
    private readonly DataTrackerHelper _helper;
    private readonly ConfigurationService _configService;
    private readonly TimeSeriesCacheService _cacheService;
    
    // Instance-specific settings (overrides global config)
    private float _scrollSpeed = 30f;
    private HashSet<ulong> _disabledCharacters = new();

    private Configuration Config => _configService.Config;
    
    /// <summary>
    /// Gets the scroll speed for this instance.
    /// </summary>
    public float ScrollSpeed
    {
        get => _scrollSpeed;
        set => _scrollSpeed = value;
    }
    
    /// <summary>
    /// Gets the set of disabled character IDs for this instance.
    /// </summary>
    public HashSet<ulong> DisabledCharacters => _disabledCharacters;

    public GilTickerTool(GilTickerComponent inner, DataTrackerHelper helper, ConfigurationService configService, TimeSeriesCacheService cacheService)
    {
        _inner = inner;
        _helper = helper;
        _configService = configService;
        _cacheService = cacheService;
        Title = "Gil Ticker";
        Size = new System.Numerics.Vector2(400, 30);
        HeaderVisible = false;
        // Default to 6 subunits height (1.5 cells at default 4 subdivisions)
        GridRowSpan = 1.5f;
        
        // Initialize from global config as default
        _scrollSpeed = Config.GilTickerScrollSpeed;
        _disabledCharacters = new HashSet<ulong>(Config.GilTickerDisabledCharacters);
    }

    public override void DrawContent()
    {
        // Pass instance-specific settings to inner component
        _inner.Draw(_scrollSpeed, _disabledCharacters);
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        try
        {
            if (!ImGui.CollapsingHeader("Ticker Settings", ImGuiTreeNodeFlags.DefaultOpen))
                return;

            var speed = _scrollSpeed;
            if (ImGui.SliderFloat("Scroll speed", ref speed, 5f, 100f, "%.0f px/s"))
            {
                _scrollSpeed = speed;
                _configService.Save();
            }
            ShowSettingTooltip("How fast the ticker scrolls in pixels per second.", "30");

            ImGui.Spacing();

            // Character enable/disable section
            ImGui.TextUnformatted("Characters");
            ImGui.Separator();

            var availableChars = _helper.AvailableCharacters;
            if (availableChars.Count == 0)
            {
                ImGui.TextDisabled("No characters available.");
            }
            else
            {
                foreach (var charId in availableChars)
                {
                    var charName = _cacheService.GetFormattedCharacterName(charId) 
                        ?? Kaleidoscope.Libs.CharacterLib.GetCharacterName(charId);
                    if (string.IsNullOrEmpty(charName))
                        charName = $"Character {charId}";

                    var isEnabled = !_disabledCharacters.Contains(charId);
                    if (ImGui.Checkbox(charName, ref isEnabled))
                    {
                        if (isEnabled)
                        {
                            _disabledCharacters.Remove(charId);
                        }
                        else
                        {
                            _disabledCharacters.Add(charId);
                        }
                        _configService.Save();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Error drawing GilTicker settings", ex);
        }
    }
    
    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["ScrollSpeed"] = _scrollSpeed,
            ["DisabledCharacters"] = _disabledCharacters.ToList()
        };
    }
    
    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;
        
        _scrollSpeed = GetSetting(settings, "ScrollSpeed", _scrollSpeed);
        
        var disabledChars = GetSetting<List<ulong>>(settings, "DisabledCharacters", null);
        if (disabledChars != null)
        {
            _disabledCharacters = new HashSet<ulong>(disabledChars);
        }
    }
}
