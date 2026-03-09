using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Color configuration for graph styling.
/// Provides customizable colors with vibrant, professional defaults inspired by financial charting platforms.
/// </summary>
public class MTGraphColorConfig
{
    #region Background Colors
    
    /// <summary>Plot background color - dark charcoal by default.</summary>
    public Vector4 PlotBackground { get; set; } = new(0.08f, 0.09f, 0.10f, 1f);
    
    /// <summary>Frame background color - slightly darker than plot by default.</summary>
    public Vector4 FrameBackground { get; set; } = new(0.06f, 0.07f, 0.08f, 1f);
    
    #endregion
    
    #region Grid and Axis Colors
    
    /// <summary>Grid line color with transparency.</summary>
    public Vector4 GridLine { get; set; } = new(0.18f, 0.20f, 0.22f, 0.6f);
    
    /// <summary>Axis line color.</summary>
    public Vector4 AxisLine { get; set; } = new(0.25f, 0.28f, 0.30f, 1f);
    
    #endregion
    
    #region Price Movement Colors
    
    /// <summary>Bullish/positive trend color - bright green by default.</summary>
    public Vector4 Bullish { get; set; } = new(0.20f, 0.90f, 0.40f, 1f);
    
    /// <summary>Bullish fill color - top of gradient.</summary>
    public Vector4 BullishFillTop { get; set; } = new(0.20f, 0.90f, 0.40f, 0.60f);
    
    /// <summary>Bullish fill color - bottom of gradient (very transparent).</summary>
    public Vector4 BullishFillBottom { get; set; } = new(0.20f, 0.90f, 0.40f, 0.05f);
    
    /// <summary>Bearish/negative trend color - bright red by default.</summary>
    public Vector4 Bearish { get; set; } = new(1.0f, 0.25f, 0.25f, 1f);
    
    /// <summary>Bearish fill color - top of gradient.</summary>
    public Vector4 BearishFillTop { get; set; } = new(1.0f, 0.25f, 0.25f, 0.60f);
    
    /// <summary>Bearish fill color - bottom of gradient (very transparent).</summary>
    public Vector4 BearishFillBottom { get; set; } = new(1.0f, 0.25f, 0.25f, 0.05f);
    
    /// <summary>Neutral/no change color - bright yellow by default.</summary>
    public Vector4 Neutral { get; set; } = new(1.0f, 0.85f, 0.0f, 1f);
    
    #endregion
    
    #region Crosshair and Tooltip Colors
    
    /// <summary>Crosshair color with transparency.</summary>
    public Vector4 Crosshair { get; set; } = new(0.55f, 0.58f, 0.62f, 0.8f);
    
    /// <summary>Tooltip background color.</summary>
    public Vector4 TooltipBackground { get; set; } = new(0.12f, 0.14f, 0.16f, 0.95f);
    
    /// <summary>Tooltip border color.</summary>
    public Vector4 TooltipBorder { get; set; } = new(0.30f, 0.32f, 0.35f, 1f);
    
    #endregion
    
    #region Current Price Line
    
    /// <summary>Current price horizontal line color - bright yellow by default.</summary>
    public Vector4 CurrentPriceLine { get; set; } = new(1.0f, 0.85f, 0.0f, 0.9f);
    
    #endregion
    
    #region Text Colors
    
    /// <summary>Primary text color - bright white/gray by default.</summary>
    public Vector4 TextPrimary { get; set; } = new(0.90f, 0.92f, 0.94f, 1f);
    
    /// <summary>Secondary text color - dimmed gray by default.</summary>
    public Vector4 TextSecondary { get; set; } = new(0.55f, 0.58f, 0.62f, 1f);
    
    #endregion
    
    #region Series Color Palette
    
    /// <summary>
    /// Default series color palette for multi-series graphs.
    /// Vibrant, distinguishable colors for up to 8 series.
    /// </summary>
    public Vector3[] SeriesPalette { get; set; } =
    {
        new(1.0f, 0.25f, 0.25f),   // Bright Red
        new(0.25f, 0.50f, 1.0f),   // Bright Blue
        new(0.20f, 0.90f, 0.40f),  // Bright Green
        new(0.75f, 0.30f, 0.90f),  // Bright Purple
        new(1.0f, 0.45f, 0.70f),   // Bright Pink
        new(1.0f, 0.85f, 0.0f),    // Bright Yellow
        new(1.0f, 0.55f, 0.0f),    // Bright Orange
        new(0.0f, 0.85f, 0.85f),   // Bright Cyan
    };
    
    #endregion
    
    #region Static Default Instance
    
    /// <summary>
    /// Default color configuration instance. Shared; do not mutate.
    /// </summary>
    public static readonly MTGraphColorConfig Default = new();
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Gets colors for the specified number of series.
    /// </summary>
    /// <param name="count">Number of series.</param>
    /// <returns>Array of colors cycling through the palette.</returns>
    public Vector3[] GetSeriesColors(int count)
    {
        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
            result[i] = SeriesPalette[i % SeriesPalette.Length];
        return result;
    }
    
    #endregion
}

/// <summary>
/// Static utility class for applying chart styles.
/// Provides methods for pushing/popping ImPlot style configurations.
/// </summary>
/// <remarks>
/// For color customization, use <see cref="GraphStyleConfig.Colors"/> property.
/// The static <see cref="DefaultColors"/> property provides backward compatibility.
/// </remarks>
public static class ChartColors
{
    #region Backward Compatibility - Static Color Properties
    
    /// <summary>
    /// Default color configuration instance for backward compatibility.
    /// Use <see cref="GraphStyleConfig.Colors"/> for customization.
    /// </summary>
    public static MTGraphColorConfig DefaultColors { get; } = new();
    
    /// <summary>Plot background color - dark charcoal.</summary>
    public static Vector4 PlotBackground => DefaultColors.PlotBackground;
    
    /// <summary>Frame background color - slightly darker than plot.</summary>
    public static Vector4 FrameBackground => DefaultColors.FrameBackground;
    
    /// <summary>Grid line color with transparency.</summary>
    public static Vector4 GridLine => DefaultColors.GridLine;
    
    /// <summary>Axis line color.</summary>
    public static Vector4 AxisLine => DefaultColors.AxisLine;
    
    /// <summary>Bullish/positive trend color - bright green.</summary>
    public static Vector4 Bullish => DefaultColors.Bullish;
    
    /// <summary>Bullish fill color - top of gradient.</summary>
    public static Vector4 BullishFillTop => DefaultColors.BullishFillTop;
    
    /// <summary>Bullish fill color - bottom of gradient (very transparent).</summary>
    public static Vector4 BullishFillBottom => DefaultColors.BullishFillBottom;
    
    /// <summary>Bearish/negative trend color - bright red.</summary>
    public static Vector4 Bearish => DefaultColors.Bearish;
    
    /// <summary>Bearish fill color - top of gradient.</summary>
    public static Vector4 BearishFillTop => DefaultColors.BearishFillTop;
    
    /// <summary>Bearish fill color - bottom of gradient (very transparent).</summary>
    public static Vector4 BearishFillBottom => DefaultColors.BearishFillBottom;
    
    /// <summary>Neutral/no change color - bright yellow.</summary>
    public static Vector4 Neutral => DefaultColors.Neutral;
    
    /// <summary>Crosshair color with transparency.</summary>
    public static Vector4 Crosshair => DefaultColors.Crosshair;
    
    /// <summary>Tooltip background color.</summary>
    public static Vector4 TooltipBackground => DefaultColors.TooltipBackground;
    
    /// <summary>Tooltip border color.</summary>
    public static Vector4 TooltipBorder => DefaultColors.TooltipBorder;
    
    /// <summary>Current price horizontal line color - bright yellow.</summary>
    public static Vector4 CurrentPriceLine => DefaultColors.CurrentPriceLine;
    
    /// <summary>Primary text color - bright white/gray.</summary>
    public static Vector4 TextPrimary => DefaultColors.TextPrimary;
    
    /// <summary>Secondary text color - dimmed gray.</summary>
    public static Vector4 TextSecondary => DefaultColors.TextSecondary;
    
    /// <summary>
    /// Default series color palette for multi-series graphs.
    /// </summary>
    public static Vector3[] SeriesPalette => DefaultColors.SeriesPalette;
    
    #endregion
    
    #region Style Push/Pop Methods
    
    /// <summary>
    /// Number of ImPlot style colors pushed by PushChartStyle.
    /// </summary>
    private const int StyleColorCount = 5;
    
    /// <summary>
    /// Number of ImPlot style variables pushed by PushChartStyle.
    /// </summary>
    private const int StyleVarCount = 2;
    
    /// <summary>
    /// Applies trading platform style colors to the plot.
    /// Must be called before BeginPlot and matched with PopChartStyle after EndPlot.
    /// </summary>
    /// <param name="style">Optional style configuration for customization.</param>
    public static void PushChartStyle(GraphStyleConfig? style = null)
    {
        style ??= GraphStyleConfig.Default;
        var colors = style.Colors;
        
        // Plot frame and background
        ImPlot.PushStyleColor(ImPlotCol.Bg, colors.PlotBackground);
        ImPlot.PushStyleColor(ImPlotCol.FrameBg, colors.FrameBackground);
        
        // Line styling - bullish green by default
        ImPlot.PushStyleColor(ImPlotCol.Line, colors.Bullish);
        ImPlot.PushStyleColor(ImPlotCol.Fill, colors.BullishFillTop);
        
        // Crosshair
        ImPlot.PushStyleColor(ImPlotCol.Crosshairs, colors.Crosshair);
        
        // Style variables
        ImPlot.PushStyleVar(ImPlotStyleVar.LineWeight, style.LineWeight);
        ImPlot.PushStyleVar(ImPlotStyleVar.FillAlpha, style.FillAlpha);
    }
    
    /// <summary>
    /// Pops the trading platform style colors.
    /// Must be called after EndPlot to match a previous PushChartStyle call.
    /// </summary>
    public static void PopChartStyle()
    {
        ImPlot.PopStyleVar(StyleVarCount);
        ImPlot.PopStyleColor(StyleColorCount);
    }
    
    /// <summary>
    /// Gets colors for the specified number of series.
    /// </summary>
    /// <param name="count">Number of series.</param>
    /// <param name="colors">Optional color configuration. Uses default if null.</param>
    /// <returns>Array of colors cycling through the palette.</returns>
    public static Vector3[] GetSeriesColors(int count, MTGraphColorConfig? colors = null)
    {
        colors ??= DefaultColors;
        return colors.GetSeriesColors(count);
    }
    
    #endregion
}

/// <summary>
/// Style configuration for ImPlot graphs.
/// Contains all configurable style values that can be customized.
/// </summary>
public class GraphStyleConfig
{
    #region Color Configuration
    
    /// <summary>
    /// Color configuration for the graph.
    /// Allows customization of all graph colors including backgrounds, text, and series colors.
    /// </summary>
    public MTGraphColorConfig Colors { get; set; } = new();
    
    #endregion
    
    #region Line and Fill Styles
    
    /// <summary>Line weight/thickness for plot lines. Default: 2.0</summary>
    public float LineWeight { get; set; } = 2f;
    
    /// <summary>Fill alpha for area charts. Default: 0.35</summary>
    public float FillAlpha { get; set; } = 0.35f;
    
    /// <summary>Fill alpha for multi-series area charts. Default: 0.55</summary>
    public float MultiSeriesFillAlpha { get; set; } = 0.55f;
    
    #endregion
    
    #region Crosshair Styles
    
    /// <summary>Crosshair dash length in pixels. Default: 4</summary>
    public float CrosshairDashLength { get; set; } = 4f;
    
    /// <summary>Crosshair gap length in pixels. Default: 3</summary>
    public float CrosshairGapLength { get; set; } = 3f;
    
    /// <summary>Crosshair line thickness. Default: 1</summary>
    public float CrosshairThickness { get; set; } = 1f;
    
    #endregion
    
    #region Price Line Styles
    
    /// <summary>Current price line dash length. Default: 6</summary>
    public float PriceLineDashLength { get; set; } = 6f;
    
    /// <summary>Current price line gap length. Default: 4</summary>
    public float PriceLineGapLength { get; set; } = 4f;
    
    /// <summary>Current price line thickness. Default: 1.5</summary>
    public float PriceLineThickness { get; set; } = 1.5f;
    
    #endregion
    
    #region Tooltip Styles
    
    /// <summary>Tooltip padding in pixels. Default: 8</summary>
    public float TooltipPadding { get; set; } = 8f;
    
    /// <summary>Tooltip corner rounding. Default: 4</summary>
    public float TooltipRounding { get; set; } = 4f;
    
    /// <summary>Tooltip accent bar width. Default: 3</summary>
    public float TooltipAccentWidth { get; set; } = 3f;
    
    /// <summary>Tooltip offset from cursor X. Default: 12</summary>
    public float TooltipOffsetX { get; set; } = 12f;
    
    #endregion
    
    #region Legend Styles
    
    /// <summary>Legend indicator size (colored square). Default: 10</summary>
    public float LegendIndicatorSize { get; set; } = 10f;
    
    /// <summary>Legend row height. Default: 18</summary>
    public float LegendRowHeight { get; set; } = 18f;
    
    /// <summary>Legend padding. Default: 8</summary>
    public float LegendPadding { get; set; } = 8f;
    
    /// <summary>Legend scrollbar width. Default: 6</summary>
    public float LegendScrollbarWidth { get; set; } = 6f;
    
    /// <summary>Legend corner rounding. Default: 4</summary>
    public float LegendRounding { get; set; } = 4f;
    
    /// <summary>Alpha for hidden series in legend. Default: 0.35</summary>
    public float LegendHiddenAlpha { get; set; } = 0.35f;
    
    /// <summary>Gap between legend indicator and text. Default: 6</summary>
    public float LegendIndicatorTextGap { get; set; } = 6f;
    
    /// <summary>Height of separator between groups and series in legend. Default: 8</summary>
    public float LegendSeparatorHeight { get; set; } = 8f;
    
    /// <summary>Margin from plot edges for inside legend. Default: 10</summary>
    public float LegendMargin { get; set; } = 10f;
    
    /// <summary>Indicator rounding for series (smaller than groups). Default: 2</summary>
    public float LegendIndicatorRounding { get; set; } = 2f;
    
    /// <summary>Indicator rounding for groups (larger than series). Default: 3</summary>
    public float LegendGroupIndicatorRounding { get; set; } = 3f;
    
    /// <summary>Border thickness for hidden series indicators. Default: 2</summary>
    public float LegendIndicatorBorderThickness { get; set; } = 2f;
    
    #endregion
    
    #region Value Label Styles
    
    /// <summary>Value label background padding. Default: 4</summary>
    public float ValueLabelPadding { get; set; } = 4f;
    
    /// <summary>Value label corner rounding. Default: 3</summary>
    public float ValueLabelRounding { get; set; } = 3f;
    
    /// <summary>Value label connecting line thickness. Default: 1.5</summary>
    public float ValueLabelLineThickness { get; set; } = 1.5f;
    
    /// <summary>Minimum vertical spacing between labels to avoid overlap. Default: 2</summary>
    public float ValueLabelMinSpacing { get; set; } = 2f;
    
    /// <summary>Horizontal offset from the data point for the label. Default: 6</summary>
    public float ValueLabelHorizontalOffset { get; set; } = 6f;
    
    /// <summary>Maximum number of labels to show before hiding some. Default: 30</summary>
    public int ValueLabelMaxVisible { get; set; } = 30;
    
    /// <summary>Alpha for label background. Default: 0.85</summary>
    public float ValueLabelBackgroundAlpha { get; set; } = 0.85f;
    
    /// <summary>Border thickness for value labels. Default: 1</summary>
    public float ValueLabelBorderThickness { get; set; } = 1f;
    
    /// <summary>Alpha for connecting line from data point to label. Default: 0.4</summary>
    public float ValueLabelLineAlpha { get; set; } = 0.4f;
    
    /// <summary>Alpha for label border. Default: 0.7</summary>
    public float ValueLabelBorderAlpha { get; set; } = 0.7f;
    
    /// <summary>Right margin from plot edge for value labels. Default: 4</summary>
    public float ValueLabelRightMargin { get; set; } = 4f;
    
    /// <summary>Stair pattern steps per row for label positioning. Higher = finer steps. Default: 8</summary>
    public int ValueLabelStepsPerRow { get; set; } = 8;
    
    /// <summary>Maximum iterations for label collision resolution. Default: 20</summary>
    public int ValueLabelMaxIterations { get; set; } = 20;

    #endregion
    
    #region Controls Drawer Styles
    
    /// <summary>Toggle button width. Default: 24</summary>
    public float ToggleButtonWidth { get; set; } = 24f;
    
    /// <summary>Toggle button height. Default: 20</summary>
    public float ToggleButtonHeight { get; set; } = 20f;

    /// <summary>Controls drawer width. Default: 160</summary>
    public float DrawerWidth { get; set; } = 160f;
    
    /// <summary>Controls drawer padding. Default: 8</summary>
    public float DrawerPadding { get; set; } = 8f;
    
    /// <summary>Controls drawer row height. Default: 22</summary>
    public float DrawerRowHeight { get; set; } = 22f;
    
    /// <summary>Controls checkbox size. Default: 14</summary>
    public float DrawerCheckboxSize { get; set; } = 14f;
    
    /// <summary>Margin from plot edges for controls drawer. Default: 10</summary>
    public float DrawerMargin { get; set; } = 10f;
    
    /// <summary>Spacing between elements in the drawer. Default: 4</summary>
    public float DrawerElementSpacing { get; set; } = 4f;
    
    /// <summary>Width of value input box. Default: 50</summary>
    public float DrawerValueBoxWidth { get; set; } = 50f;
    
    /// <summary>Width of small +/- buttons. Default: 22</summary>
    public float DrawerSmallButtonWidth { get; set; } = 22f;
    
    /// <summary>Height of slider track. Default: 8</summary>
    public float DrawerSliderHeight { get; set; } = 8f;
    
    /// <summary>Width of slider handle. Default: 12</summary>
    public float DrawerSliderHandleWidth { get; set; } = 12f;
    
    /// <summary>Height of slider handle. Default: 16</summary>
    public float DrawerSliderHandleHeight { get; set; } = 16f;
    
    /// <summary>Radius of the settings icon. Default: 6</summary>
    public float DrawerIconRadius { get; set; } = 6f;
    
    /// <summary>Inner padding for checkmark drawing. Default: 3</summary>
    public float DrawerCheckPadding { get; set; } = 3f;
    
    /// <summary>Corner rounding for drawer and buttons. Default: 3</summary>
    public float DrawerRounding { get; set; } = 3f;
    
    /// <summary>Corner rounding for slider track. Default: 4</summary>
    public float DrawerSliderRounding { get; set; } = 4f;
    
    #endregion
    
    /// <summary>
    /// Default style configuration instance. Shared; do not mutate.
    /// </summary>
    public static readonly GraphStyleConfig Default = new();
}
