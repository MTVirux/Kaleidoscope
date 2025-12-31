using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.MainWindow;
using Kaleidoscope.Services;
using MTGui.Graph;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Customization configuration category in the config window.
/// Allows users to customize default colors for all UI elements.
/// </summary>
public sealed class CustomizationCategory
{
    private readonly Kaleidoscope.Configuration config;
    private readonly Action saveConfig;
    private readonly LayoutEditingService _layoutEditingService;
    
    // Default ImGui theme background color
    private static readonly Vector4 DefaultBackgroundColor = new(0.06f, 0.06f, 0.06f, 0.94f);

    public CustomizationCategory(Kaleidoscope.Configuration config, Action saveConfig, LayoutEditingService layoutEditingService)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        _layoutEditingService = layoutEditingService;
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
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Tool Internal Padding (per-layout setting)
            ImGui.TextDisabled("Layout-specific tool padding (affects current layout).");
            ImGui.Spacing();
            
            var currentLayout = GetCurrentLayout();
            if (currentLayout != null)
            {
                var toolPadding = currentLayout.ToolInternalPaddingPx;
                
                ImGui.TextUnformatted("Tool Internal Padding");
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Padding in pixels inside each tool.\nHigher values create more space around tool content.\n0 = no padding.");
                }
                
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 70f);
                if (ImGui.SliderInt("##toolpadding", ref toolPadding, 0, 32))
                {
                    currentLayout.ToolInternalPaddingPx = toolPadding;
                    // Also update the working grid settings so the change takes effect immediately
                    if (_layoutEditingService.WorkingGridSettings != null)
                    {
                        _layoutEditingService.WorkingGridSettings.ToolInternalPaddingPx = toolPadding;
                    }
                    saveConfig();
                }
            }
            else
            {
                ImGui.TextDisabled("No layout loaded.");
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

        // === GRAPH CUSTOMIZATION ===
        if (ImGui.CollapsingHeader("Graph Customization"))
        {
            ImGui.Indent();
            ImGui.Spacing();
            
            ImGui.TextDisabled("Customize colors, spacing, and styling for all graph widgets.");
            ImGui.Spacing();
            
            // === GRAPH COLORS ===
            if (ImGui.TreeNode("Colors"))
            {
                ImGui.Spacing();
                
                var graphColors = config.GraphStyle.Colors;
                
                // Backgrounds
                ImGui.TextUnformatted("Backgrounds");
                ImGui.Separator();
                
                var plotBg = graphColors.PlotBackground;
                if (DrawColorRow("Plot Background", ref plotBg, new(0.08f, 0.08f, 0.10f, 1f)))
                {
                    graphColors.PlotBackground = plotBg;
                }
                
                var frameBg = graphColors.FrameBackground;
                if (DrawColorRow("Frame Background", ref frameBg, new(0.06f, 0.06f, 0.08f, 1f)))
                {
                    graphColors.FrameBackground = frameBg;
                }
                
                ImGui.Spacing();
                ImGui.TextUnformatted("Grid & Axes");
                ImGui.Separator();
                
                var gridLine = graphColors.GridLine;
                if (DrawColorRow("Grid Lines", ref gridLine, new(0.25f, 0.25f, 0.28f, 0.4f)))
            {
                graphColors.GridLine = gridLine;
            }
            
            var axisLine = graphColors.AxisLine;
            if (DrawColorRow("Axis Lines", ref axisLine, new(0.40f, 0.40f, 0.45f, 1f)))
            {
                graphColors.AxisLine = axisLine;
            }
            
            ImGui.Spacing();
            ImGui.TextUnformatted("Trend Colors");
            ImGui.Separator();
            
            var bullish = graphColors.Bullish;
            if (DrawColorRow("Bullish (Positive)", ref bullish, new(0.10f, 0.85f, 0.45f, 1f)))
            {
                graphColors.Bullish = bullish;
            }
            
            var bearish = graphColors.Bearish;
            if (DrawColorRow("Bearish (Negative)", ref bearish, new(0.95f, 0.25f, 0.30f, 1f)))
            {
                graphColors.Bearish = bearish;
            }
            
            var neutral = graphColors.Neutral;
            if (DrawColorRow("Neutral", ref neutral, new(1f, 0.85f, 0.25f, 1f)))
            {
                graphColors.Neutral = neutral;
            }
            
            ImGui.Spacing();
            ImGui.TextUnformatted("Text");
            ImGui.Separator();
            
            var textPrimary = graphColors.TextPrimary;
            if (DrawColorRow("Primary Text", ref textPrimary, new(0.95f, 0.95f, 0.98f, 1f)))
            {
                graphColors.TextPrimary = textPrimary;
            }
            
            var textSecondary = graphColors.TextSecondary;
            if (DrawColorRow("Secondary Text", ref textSecondary, new(0.65f, 0.65f, 0.70f, 1f)))
            {
                graphColors.TextSecondary = textSecondary;
            }
            
            ImGui.Spacing();
            ImGui.TextUnformatted("Tooltips & Overlays");
            ImGui.Separator();
            
            var crosshair = graphColors.Crosshair;
            if (DrawColorRow("Crosshair", ref crosshair, new(0.7f, 0.7f, 0.75f, 0.6f)))
            {
                graphColors.Crosshair = crosshair;
                }
                
                var tooltipBg = graphColors.TooltipBackground;
                if (DrawColorRow("Tooltip Background", ref tooltipBg, new(0.10f, 0.10f, 0.12f, 0.95f)))
                {
                    graphColors.TooltipBackground = tooltipBg;
                }
                
                var tooltipBorder = graphColors.TooltipBorder;
                if (DrawColorRow("Tooltip Border", ref tooltipBorder, new(0.35f, 0.35f, 0.40f, 0.8f)))
                {
                    graphColors.TooltipBorder = tooltipBorder;
                }
                
                var priceLine = graphColors.CurrentPriceLine;
                if (DrawColorRow("Current Price Line", ref priceLine, new(1f, 0.85f, 0.25f, 0.9f)))
                {
                    graphColors.CurrentPriceLine = priceLine;
                }
                
                ImGui.TreePop();
            }
            
            // === GRAPH LINE STYLES ===
            if (ImGui.TreeNode("Line Styles"))
            {
                ImGui.Spacing();
                
                var lineWeight = config.GraphStyle.LineWeight;
                if (DrawFloatRow("Line Weight", ref lineWeight, 2f, 0.5f, 5f))
                {
                    config.GraphStyle.LineWeight = lineWeight;
                }
                
                var fillAlpha = config.GraphStyle.FillAlpha;
                if (DrawFloatRow("Fill Alpha", ref fillAlpha, 0.35f, 0f, 1f))
                {
                    config.GraphStyle.FillAlpha = fillAlpha;
                }
                
                var multiSeriesFillAlpha = config.GraphStyle.MultiSeriesFillAlpha;
                if (DrawFloatRow("Multi-Series Fill Alpha", ref multiSeriesFillAlpha, 0.55f, 0f, 1f))
                {
                    config.GraphStyle.MultiSeriesFillAlpha = multiSeriesFillAlpha;
                }
                
                ImGui.TreePop();
            }
            
            // === GRAPH CROSSHAIR & PRICE LINE ===
            if (ImGui.TreeNode("Crosshair & Price Line"))
            {
                ImGui.Spacing();
                
                ImGui.TextUnformatted("Crosshair");
                ImGui.Separator();
                
                var crosshairDash = config.GraphStyle.CrosshairDashLength;
                if (DrawFloatRow("Dash Length", ref crosshairDash, 4f, 1f, 20f))
                {
                    config.GraphStyle.CrosshairDashLength = crosshairDash;
                }
                
                var crosshairGap = config.GraphStyle.CrosshairGapLength;
                if (DrawFloatRow("Gap Length", ref crosshairGap, 3f, 1f, 20f))
                {
                    config.GraphStyle.CrosshairGapLength = crosshairGap;
                }
                
                var crosshairThickness = config.GraphStyle.CrosshairThickness;
                if (DrawFloatRow("Thickness", ref crosshairThickness, 1f, 0.5f, 3f))
                {
                    config.GraphStyle.CrosshairThickness = crosshairThickness;
                }
                
                ImGui.Spacing();
                ImGui.TextUnformatted("Current Price Line");
                ImGui.Separator();
                
                var priceLineDash = config.GraphStyle.PriceLineDashLength;
                if (DrawFloatRow("Dash Length", ref priceLineDash, 6f, 1f, 20f))
                {
                    config.GraphStyle.PriceLineDashLength = priceLineDash;
                }
                
                var priceLineGap = config.GraphStyle.PriceLineGapLength;
                if (DrawFloatRow("Gap Length", ref priceLineGap, 4f, 1f, 20f))
                {
                    config.GraphStyle.PriceLineGapLength = priceLineGap;
                }
                
                var priceLineThickness = config.GraphStyle.PriceLineThickness;
                if (DrawFloatRow("Thickness", ref priceLineThickness, 1.5f, 0.5f, 5f))
                {
                    config.GraphStyle.PriceLineThickness = priceLineThickness;
                }
                
                ImGui.TreePop();
            }
            
            // === GRAPH TOOLTIP STYLES ===
            if (ImGui.TreeNode("Tooltip Styles"))
            {
                ImGui.Spacing();
                
                var tooltipPadding = config.GraphStyle.TooltipPadding;
                if (DrawFloatRow("Padding", ref tooltipPadding, 8f, 2f, 20f))
                {
                    config.GraphStyle.TooltipPadding = tooltipPadding;
                }
                
                var tooltipRounding = config.GraphStyle.TooltipRounding;
                if (DrawFloatRow("Corner Rounding", ref tooltipRounding, 4f, 0f, 12f))
                {
                    config.GraphStyle.TooltipRounding = tooltipRounding;
                }
                
                var tooltipAccent = config.GraphStyle.TooltipAccentWidth;
                if (DrawFloatRow("Accent Bar Width", ref tooltipAccent, 3f, 0f, 10f))
                {
                    config.GraphStyle.TooltipAccentWidth = tooltipAccent;
                }
                
                var tooltipOffset = config.GraphStyle.TooltipOffsetX;
                if (DrawFloatRow("Offset from Cursor", ref tooltipOffset, 12f, 0f, 30f))
                {
                    config.GraphStyle.TooltipOffsetX = tooltipOffset;
                }
                
                ImGui.TreePop();
            }
            
            // === GRAPH LEGEND STYLES ===
            if (ImGui.TreeNode("Legend Styles"))
            {
                ImGui.Spacing();
                
                var legendIndicatorSize = config.GraphStyle.LegendIndicatorSize;
                if (DrawFloatRow("Indicator Size", ref legendIndicatorSize, 10f, 4f, 20f))
                {
                    config.GraphStyle.LegendIndicatorSize = legendIndicatorSize;
                }
                
                var legendRowHeight = config.GraphStyle.LegendRowHeight;
                if (DrawFloatRow("Row Height", ref legendRowHeight, 18f, 12f, 30f))
                {
                    config.GraphStyle.LegendRowHeight = legendRowHeight;
                }
                
                var legendPadding = config.GraphStyle.LegendPadding;
                if (DrawFloatRow("Padding", ref legendPadding, 8f, 2f, 20f))
                {
                    config.GraphStyle.LegendPadding = legendPadding;
                }
                
                var legendScrollbarWidth = config.GraphStyle.LegendScrollbarWidth;
                if (DrawFloatRow("Scrollbar Width", ref legendScrollbarWidth, 6f, 2f, 12f))
                {
                    config.GraphStyle.LegendScrollbarWidth = legendScrollbarWidth;
                }
                
                var legendRounding = config.GraphStyle.LegendRounding;
                if (DrawFloatRow("Corner Rounding", ref legendRounding, 4f, 0f, 12f))
                {
                    config.GraphStyle.LegendRounding = legendRounding;
                }
                
                var legendHiddenAlpha = config.GraphStyle.LegendHiddenAlpha;
                if (DrawFloatRow("Hidden Series Alpha", ref legendHiddenAlpha, 0.35f, 0.1f, 0.8f))
                {
                    config.GraphStyle.LegendHiddenAlpha = legendHiddenAlpha;
                }
                
                var legendMargin = config.GraphStyle.LegendMargin;
                if (DrawFloatRow("Inside Legend Margin", ref legendMargin, 10f, 0f, 30f))
                {
                    config.GraphStyle.LegendMargin = legendMargin;
                }
                
                var legendIndicatorTextGap = config.GraphStyle.LegendIndicatorTextGap;
                if (DrawFloatRow("Indicator-Text Gap", ref legendIndicatorTextGap, 6f, 2f, 15f))
                {
                    config.GraphStyle.LegendIndicatorTextGap = legendIndicatorTextGap;
                }
                
                ImGui.TreePop();
            }
            
            // === GRAPH VALUE LABEL STYLES ===
            if (ImGui.TreeNode("Value Label Styles"))
            {
                ImGui.Spacing();
                
                var valueLabelPadding = config.GraphStyle.ValueLabelPadding;
                if (DrawFloatRow("Padding", ref valueLabelPadding, 4f, 1f, 12f))
                {
                    config.GraphStyle.ValueLabelPadding = valueLabelPadding;
                }
                
                var valueLabelRounding = config.GraphStyle.ValueLabelRounding;
                if (DrawFloatRow("Corner Rounding", ref valueLabelRounding, 3f, 0f, 10f))
                {
                    config.GraphStyle.ValueLabelRounding = valueLabelRounding;
                }
                
                var valueLabelLineThickness = config.GraphStyle.ValueLabelLineThickness;
                if (DrawFloatRow("Line Thickness", ref valueLabelLineThickness, 1.5f, 0.5f, 4f))
                {
                    config.GraphStyle.ValueLabelLineThickness = valueLabelLineThickness;
                }
                
                var valueLabelMinSpacing = config.GraphStyle.ValueLabelMinSpacing;
                if (DrawFloatRow("Min Vertical Spacing", ref valueLabelMinSpacing, 2f, 0f, 10f))
                {
                    config.GraphStyle.ValueLabelMinSpacing = valueLabelMinSpacing;
                }
            
            var valueLabelHorizontalOffset = config.GraphStyle.ValueLabelHorizontalOffset;
            if (DrawFloatRow("Horizontal Offset", ref valueLabelHorizontalOffset, 6f, 0f, 20f))
            {
                    config.GraphStyle.ValueLabelHorizontalOffset = valueLabelHorizontalOffset;
                }
                
                var valueLabelBgAlpha = config.GraphStyle.ValueLabelBackgroundAlpha;
                if (DrawFloatRow("Background Alpha", ref valueLabelBgAlpha, 0.85f, 0.3f, 1f))
                {
                    config.GraphStyle.ValueLabelBackgroundAlpha = valueLabelBgAlpha;
                }
                
                var valueLabelLineAlpha = config.GraphStyle.ValueLabelLineAlpha;
                if (DrawFloatRow("Connecting Line Alpha", ref valueLabelLineAlpha, 0.4f, 0.1f, 1f))
                {
                    config.GraphStyle.ValueLabelLineAlpha = valueLabelLineAlpha;
                }
                
                var valueLabelBorderAlpha = config.GraphStyle.ValueLabelBorderAlpha;
                if (DrawFloatRow("Border Alpha", ref valueLabelBorderAlpha, 0.7f, 0.2f, 1f))
                {
                    config.GraphStyle.ValueLabelBorderAlpha = valueLabelBorderAlpha;
                }
                
                var valueLabelMaxVisible = config.GraphStyle.ValueLabelMaxVisible;
                if (DrawIntRow("Max Visible Labels", ref valueLabelMaxVisible, 30, 5, 100))
                {
                    config.GraphStyle.ValueLabelMaxVisible = valueLabelMaxVisible;
                }
                
                ImGui.TreePop();
            }
            
            // === GRAPH CONTROLS DRAWER ===
            if (ImGui.TreeNode("Controls Drawer"))
            {
                ImGui.Spacing();
                
                var toggleButtonWidth = config.GraphStyle.ToggleButtonWidth;
                if (DrawFloatRow("Toggle Button Width", ref toggleButtonWidth, 24f, 16f, 40f))
                {
                    config.GraphStyle.ToggleButtonWidth = toggleButtonWidth;
                }
                
                var toggleButtonHeight = config.GraphStyle.ToggleButtonHeight;
                if (DrawFloatRow("Toggle Button Height", ref toggleButtonHeight, 20f, 14f, 32f))
                {
                    config.GraphStyle.ToggleButtonHeight = toggleButtonHeight;
                }
                
                var drawerWidth = config.GraphStyle.DrawerWidth;
                if (DrawFloatRow("Drawer Width", ref drawerWidth, 160f, 100f, 250f))
                {
                    config.GraphStyle.DrawerWidth = drawerWidth;
                }
                
                var drawerPadding = config.GraphStyle.DrawerPadding;
                if (DrawFloatRow("Drawer Padding", ref drawerPadding, 8f, 2f, 16f))
            {
                config.GraphStyle.DrawerPadding = drawerPadding;
            }
            
            var drawerRowHeight = config.GraphStyle.DrawerRowHeight;
                if (DrawFloatRow("Row Height", ref drawerRowHeight, 22f, 16f, 32f))
                {
                    config.GraphStyle.DrawerRowHeight = drawerRowHeight;
                }
                
                var drawerMargin = config.GraphStyle.DrawerMargin;
                if (DrawFloatRow("Margin from Plot Edge", ref drawerMargin, 10f, 2f, 25f))
                {
                    config.GraphStyle.DrawerMargin = drawerMargin;
                }
                
                var drawerRounding = config.GraphStyle.DrawerRounding;
                if (DrawFloatRow("Corner Rounding", ref drawerRounding, 3f, 0f, 10f))
                {
                    config.GraphStyle.DrawerRounding = drawerRounding;
                }
                
                ImGui.TreePop();
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
            config.GraphStyle = new MTGraphStyleConfig();
            saveConfig();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Reset all customization colors and graph styles to their default values.");
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

    /// <summary>
    /// Draws a float editor row with label, slider, and reset button.
    /// Returns true if the value was changed.
    /// </summary>
    private bool DrawFloatRow(string label, ref float value, float defaultValue, float min, float max)
    {
        var changed = false;
        
        ImGui.TextUnformatted(label);
        
        ImGui.SameLine(180f);
        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderFloat($"##{label}", ref value, min, max, "%.2f"))
        {
            saveConfig();
            changed = true;
        }
        
        ImGui.SameLine();
        if (ImGui.Button($"Reset##{label}"))
        {
            value = defaultValue;
            saveConfig();
            changed = true;
        }
        
        return changed;
    }

    /// <summary>
    /// Draws an integer editor row with label, slider, and reset button.
    /// Returns true if the value was changed.
    /// </summary>
    private bool DrawIntRow(string label, ref int value, int defaultValue, int min, int max)
    {
        var changed = false;
        
        ImGui.TextUnformatted(label);
        
        ImGui.SameLine(180f);
        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderInt($"##{label}", ref value, min, max))
        {
            saveConfig();
            changed = true;
        }
        
        ImGui.SameLine();
        if (ImGui.Button($"Reset##{label}"))
        {
            value = defaultValue;
            saveConfig();
            changed = true;
        }
        
        return changed;
    }

    /// <summary>
    /// Gets the currently active layout from configuration.
    /// </summary>
    private ContentLayoutState? GetCurrentLayout()
    {
        var layoutName = _layoutEditingService.CurrentLayoutName;
        var layoutType = _layoutEditingService.CurrentLayoutType;
        
        if (string.IsNullOrEmpty(layoutName))
            return null;
            
        return config.Layouts?.Find(x => x.Name == layoutName && x.Type == layoutType);
    }
}
