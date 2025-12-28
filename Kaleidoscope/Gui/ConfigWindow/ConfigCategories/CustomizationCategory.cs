using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Customization configuration category in the config window.
/// Allows users to customize default colors for all UI elements.
/// </summary>
public class CustomizationCategory
{
    private readonly Kaleidoscope.Configuration config;
    private readonly Action saveConfig;
    
    // Default ImGui theme background color
    private static readonly Vector4 DefaultBackgroundColor = new(0.06f, 0.06f, 0.06f, 0.94f);

    public CustomizationCategory(Kaleidoscope.Configuration config, Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
    }

    public void Draw()
    {
        // Ensure UIColors is initialized
        config.UIColors ??= new UIColors();
        
        var windowWidth = ImGui.GetContentRegionAvail().X;
        var resetButtonWidth = 60f;
        var colorWidth = windowWidth - resetButtonWidth - 20f;
        
        // === WINDOW BACKGROUNDS ===
        if (ImGui.CollapsingHeader("Window Backgrounds", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            ImGui.Spacing();

            var mainBg = config.MainWindowBackgroundColor;
            if (DrawColorRow("Main Window", ref mainBg, DefaultBackgroundColor))
            {
                config.MainWindowBackgroundColor = mainBg;
                config.UIColors.MainWindowBackground = mainBg;
            }
            
            var fsBg = config.FullscreenBackgroundColor;
            if (DrawColorRow("Fullscreen", ref fsBg, DefaultBackgroundColor))
            {
                config.FullscreenBackgroundColor = fsBg;
                config.UIColors.FullscreenBackground = fsBg;
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
        }

        // === TOOL DEFAULTS ===
        if (ImGui.CollapsingHeader("Tool Defaults"))
        {
            ImGui.Indent();
            ImGui.Spacing();
            
            ImGui.TextDisabled("Default colors for new tools added to layouts.");
            ImGui.Spacing();

            var toolBg = config.UIColors.ToolBackground;
            if (DrawColorRow("Tool Background", ref toolBg, new(211f / 255f, 58f / 255f, 58f / 255f, 0.5f)))
            {
                config.UIColors.ToolBackground = toolBg;
            }
            
            var toolHeader = config.UIColors.ToolHeaderText;
            if (DrawColorRow("Tool Header Text", ref toolHeader, new(1f, 1f, 1f, 1f)))
            {
                config.UIColors.ToolHeaderText = toolHeader;
            }
            
            var toolBorder = config.UIColors.ToolBorder;
            if (DrawColorRow("Tool Border (Edit Mode)", ref toolBorder, new(0.43f, 0.43f, 0.5f, 0.5f)))
            {
                config.UIColors.ToolBorder = toolBorder;
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
        }

        // === TABLE COLORS ===
        if (ImGui.CollapsingHeader("Table Colors"))
        {
            ImGui.Indent();
            ImGui.Spacing();
            
            ImGui.TextDisabled("Default colors for table widgets.");
            ImGui.Spacing();

            var tableHeader = config.UIColors.TableHeader;
            if (DrawColorRow("Header Row", ref tableHeader, new(0.26f, 0.26f, 0.28f, 1f)))
            {
                config.UIColors.TableHeader = tableHeader;
            }
            
            var tableEven = config.UIColors.TableRowEven;
            if (DrawColorRow("Even Rows", ref tableEven, new(0f, 0f, 0f, 0f)))
            {
                config.UIColors.TableRowEven = tableEven;
            }
            
            var tableOdd = config.UIColors.TableRowOdd;
            if (DrawColorRow("Odd Rows", ref tableOdd, new(0.1f, 0.1f, 0.1f, 0.3f)))
            {
                config.UIColors.TableRowOdd = tableOdd;
            }
            
            var tableTotal = config.UIColors.TableTotalRow;
            if (DrawColorRow("Total Row", ref tableTotal, new(0.3f, 0.3f, 0.3f, 0.5f)))
            {
                config.UIColors.TableTotalRow = tableTotal;
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
        }

        // === TEXT COLORS ===
        if (ImGui.CollapsingHeader("Text Colors"))
        {
            ImGui.Indent();
            ImGui.Spacing();
            
            ImGui.TextDisabled("Default text colors throughout the UI.");
            ImGui.Spacing();

            var textPrimary = config.UIColors.TextPrimary;
            if (DrawColorRow("Primary Text", ref textPrimary, new(1f, 1f, 1f, 1f)))
            {
                config.UIColors.TextPrimary = textPrimary;
            }
            
            var textSecondary = config.UIColors.TextSecondary;
            if (DrawColorRow("Secondary Text", ref textSecondary, new(0.7f, 0.7f, 0.7f, 1f)))
            {
                config.UIColors.TextSecondary = textSecondary;
            }
            
            var textDisabled = config.UIColors.TextDisabled;
            if (DrawColorRow("Disabled Text", ref textDisabled, new(0.5f, 0.5f, 0.5f, 1f)))
            {
                config.UIColors.TextDisabled = textDisabled;
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
        }

        // === ACCENT COLORS ===
        if (ImGui.CollapsingHeader("Accent Colors"))
        {
            ImGui.Indent();
            ImGui.Spacing();
            
            ImGui.TextDisabled("Accent colors for highlights, status indicators, and feedback.");
            ImGui.Spacing();

            var accentPrimary = config.UIColors.AccentPrimary;
            if (DrawColorRow("Primary Accent", ref accentPrimary, new(0.26f, 0.59f, 0.98f, 1f)))
            {
                config.UIColors.AccentPrimary = accentPrimary;
            }
            
            var accentSuccess = config.UIColors.AccentSuccess;
            if (DrawColorRow("Success (Positive)", ref accentSuccess, new(0.2f, 0.8f, 0.2f, 1f)))
            {
                config.UIColors.AccentSuccess = accentSuccess;
            }
            
            var accentWarning = config.UIColors.AccentWarning;
            if (DrawColorRow("Warning", ref accentWarning, new(1f, 0.7f, 0.3f, 1f)))
            {
                config.UIColors.AccentWarning = accentWarning;
            }
            
            var accentError = config.UIColors.AccentError;
            if (DrawColorRow("Error (Negative)", ref accentError, new(0.9f, 0.2f, 0.2f, 1f)))
            {
                config.UIColors.AccentError = accentError;
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
        }

        // === QUICK ACCESS BAR ===
        if (ImGui.CollapsingHeader("Quick Access Bar"))
        {
            ImGui.Indent();
            ImGui.Spacing();

            var qabBackground = config.UIColors.QuickAccessBarBackground;
            if (DrawColorRow("Bar Background", ref qabBackground, new(0.1f, 0.1f, 0.1f, 0.87f)))
            {
                config.UIColors.QuickAccessBarBackground = qabBackground;
            }
            
            var qabSeparator = config.UIColors.QuickAccessBarSeparator;
            if (DrawColorRow("Separator", ref qabSeparator, new(0.31f, 0.31f, 0.31f, 1f)))
            {
                config.UIColors.QuickAccessBarSeparator = qabSeparator;
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
        }

        // === GRAPH COLORS ===
        if (ImGui.CollapsingHeader("Graph Colors"))
        {
            ImGui.Indent();
            ImGui.Spacing();

            var graphDefault = config.UIColors.GraphDefault;
            if (DrawColorRow("Default Line/Fill", ref graphDefault, new(0.4f, 0.6f, 0.9f, 1f)))
            {
                config.UIColors.GraphDefault = graphDefault;
            }
            
            var graphAxis = config.UIColors.GraphAxis;
            if (DrawColorRow("Axis & Grid Lines", ref graphAxis, new(0.5f, 0.5f, 0.5f, 0.5f)))
            {
                config.UIColors.GraphAxis = graphAxis;
            }
            
            ImGui.Unindent();
            ImGui.Spacing();
        }

        // === RESET ALL ===
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        if (ImGui.Button("Reset All to Defaults"))
        {
            config.MainWindowBackgroundColor = DefaultBackgroundColor;
            config.FullscreenBackgroundColor = DefaultBackgroundColor;
            config.UIColors.ResetToDefaults();
            saveConfig();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Reset all customization colors to their default values.");
        }
    }

    /// <summary>
    /// Draws a color editor row with label, color picker, and reset button.
    /// Returns true if the color was changed.
    /// </summary>
    private bool DrawColorRow(string label, ref Vector4 color, Vector4 defaultValue)
    {
        var changed = false;
        
        ImGui.TextUnformatted(label);
        
        ImGui.SameLine(180f);
        ImGui.SetNextItemWidth(200f);
        if (ImGui.ColorEdit4($"##{label}", ref color, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar))
        {
            saveConfig();
            changed = true;
        }
        
        ImGui.SameLine();
        if (ImGui.Button($"Reset##{label}"))
        {
            color = defaultValue;
            saveConfig();
            changed = true;
        }
        
        return changed;
    }
}
