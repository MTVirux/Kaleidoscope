using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Gui.MainWindow.Tools.GilTracker;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.GilTicker;

/// <summary>
/// Tool component wrapper for the Gil Ticker feature.
/// Displays a scrolling ticker of character gil values.
/// </summary>
public class GilTickerTool : ToolComponent
{
    private readonly GilTickerComponent _inner;
    private readonly GilTrackerHelper _helper;
    private readonly ConfigurationService _configService;

    private Configuration Config => _configService.Config;

    public GilTickerTool(GilTickerComponent inner, GilTrackerHelper helper, ConfigurationService configService)
    {
        _inner = inner;
        _helper = helper;
        _configService = configService;
        Title = "Gil Ticker";
        Size = new System.Numerics.Vector2(400, 30);
        HeaderVisible = false;
        // Default to 6 subunits height (1.5 cells at default 4 subdivisions)
        GridRowSpan = 1.5f;
    }

    public override void DrawContent()
    {
        _inner.Draw();
    }

    public override bool HasSettings => true;

    public override void DrawSettings()
    {
        try
        {
            // Speed setting
            ImGui.TextUnformatted("Ticker Settings");
            ImGui.Separator();

            var speed = Config.GilTickerScrollSpeed;
            if (ImGui.SliderFloat("Scroll speed", ref speed, 5f, 100f, "%.0f px/s"))
            {
                Config.GilTickerScrollSpeed = speed;
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
                    var charName = Kaleidoscope.Libs.CharacterLib.GetCharacterName(charId);
                    if (string.IsNullOrEmpty(charName))
                        charName = $"Character {charId}";

                    var isEnabled = !Config.GilTickerDisabledCharacters.Contains(charId);
                    if (ImGui.Checkbox(charName, ref isEnabled))
                    {
                        if (isEnabled)
                        {
                            Config.GilTickerDisabledCharacters.Remove(charId);
                        }
                        else
                        {
                            if (!Config.GilTickerDisabledCharacters.Contains(charId))
                                Config.GilTickerDisabledCharacters.Add(charId);
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
}
