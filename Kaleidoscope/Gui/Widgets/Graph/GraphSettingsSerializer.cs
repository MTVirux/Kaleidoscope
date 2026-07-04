using Kaleidoscope.Gui.Helpers;
using Kaleidoscope.Models;

namespace Kaleidoscope.Gui.Widgets.Graph;

/// <summary>
/// Centralizes export/import of the shared graph-display settings surface
/// (<see cref="IGraphWidgetSettings"/>) so tools that embed a graph do not
/// hand-serialize the same keys twice. Key names are kept identical to the
/// legacy per-tool serialization so existing exports/layouts import unchanged.
///
/// Number format is intentionally excluded: tools serialize it under different
/// key names (e.g. "NumberFormatStyle" vs "GraphNumberFormatStyle"), so it stays
/// local to each tool. The retired "LegendWidth" key is also excluded; older
/// blobs that still contain it are simply ignored on import.
/// </summary>
public static class GraphSettingsSerializer
{
    /// <summary>
    /// Writes the shared graph settings into an export dictionary using the
    /// canonical key names.
    /// </summary>
    public static void Export(IGraphWidgetSettings settings, Dictionary<string, object?> dict)
    {
        dict["ColorMode"] = (int)settings.ColorMode;
        dict["LegendHeightPercent"] = settings.LegendHeightPercent;
        dict["ShowLegend"] = settings.ShowLegend;
        dict["LegendCollapsed"] = settings.LegendCollapsed;
        dict["LegendPosition"] = (int)settings.LegendPosition;
        dict["GraphType"] = (int)settings.GraphType;
        dict["ShowXAxisTimestamps"] = settings.ShowXAxisTimestamps;
        dict["ShowCrosshair"] = settings.ShowCrosshair;
        dict["ShowGridLines"] = settings.ShowGridLines;
        dict["ShowCurrentPriceLine"] = settings.ShowCurrentPriceLine;
        dict["ShowValueLabel"] = settings.ShowValueLabel;
        dict["ValueLabelOffsetX"] = settings.ValueLabelOffsetX;
        dict["ValueLabelOffsetY"] = settings.ValueLabelOffsetY;
        dict["AutoScrollEnabled"] = settings.AutoScrollEnabled;
        dict["AutoScrollTimeValue"] = settings.AutoScrollTimeValue;
        dict["AutoScrollTimeUnit"] = (int)settings.AutoScrollTimeUnit;
        dict["AutoScrollNowPosition"] = settings.AutoScrollNowPosition;
        dict["ShowControlsDrawer"] = settings.ShowControlsDrawer;
        dict["TimeRangeValue"] = settings.TimeRangeValue;
        dict["TimeRangeUnit"] = (int)settings.TimeRangeUnit;
    }

    /// <summary>
    /// Reads the shared graph settings from an import dictionary, falling back to
    /// each setting's current value when a key is absent. Unknown keys are ignored.
    /// </summary>
    public static void Import(IGraphWidgetSettings settings, Dictionary<string, object?> dict)
    {
        settings.ColorMode = (GraphColorMode)SettingsImportHelper.GetSetting(dict, "ColorMode", (int)settings.ColorMode);
        settings.LegendHeightPercent = SettingsImportHelper.GetSetting(dict, "LegendHeightPercent", settings.LegendHeightPercent);
        settings.ShowLegend = SettingsImportHelper.GetSetting(dict, "ShowLegend", settings.ShowLegend);
        settings.LegendCollapsed = SettingsImportHelper.GetSetting(dict, "LegendCollapsed", settings.LegendCollapsed);
        settings.LegendPosition = (LegendPosition)SettingsImportHelper.GetSetting(dict, "LegendPosition", (int)settings.LegendPosition);
        settings.GraphType = (GraphType)SettingsImportHelper.GetSetting(dict, "GraphType", (int)settings.GraphType);
        settings.ShowXAxisTimestamps = SettingsImportHelper.GetSetting(dict, "ShowXAxisTimestamps", settings.ShowXAxisTimestamps);
        settings.ShowCrosshair = SettingsImportHelper.GetSetting(dict, "ShowCrosshair", settings.ShowCrosshair);
        settings.ShowGridLines = SettingsImportHelper.GetSetting(dict, "ShowGridLines", settings.ShowGridLines);
        settings.ShowCurrentPriceLine = SettingsImportHelper.GetSetting(dict, "ShowCurrentPriceLine", settings.ShowCurrentPriceLine);
        settings.ShowValueLabel = SettingsImportHelper.GetSetting(dict, "ShowValueLabel", settings.ShowValueLabel);
        settings.ValueLabelOffsetX = SettingsImportHelper.GetSetting(dict, "ValueLabelOffsetX", settings.ValueLabelOffsetX);
        settings.ValueLabelOffsetY = SettingsImportHelper.GetSetting(dict, "ValueLabelOffsetY", settings.ValueLabelOffsetY);
        settings.AutoScrollEnabled = SettingsImportHelper.GetSetting(dict, "AutoScrollEnabled", settings.AutoScrollEnabled);
        settings.AutoScrollTimeValue = SettingsImportHelper.GetSetting(dict, "AutoScrollTimeValue", settings.AutoScrollTimeValue);
        settings.AutoScrollTimeUnit = (TimeUnit)SettingsImportHelper.GetSetting(dict, "AutoScrollTimeUnit", (int)settings.AutoScrollTimeUnit);
        settings.AutoScrollNowPosition = SettingsImportHelper.GetSetting(dict, "AutoScrollNowPosition", settings.AutoScrollNowPosition);
        settings.ShowControlsDrawer = SettingsImportHelper.GetSetting(dict, "ShowControlsDrawer", settings.ShowControlsDrawer);
        settings.TimeRangeValue = SettingsImportHelper.GetSetting(dict, "TimeRangeValue", settings.TimeRangeValue);
        settings.TimeRangeUnit = (TimeUnit)SettingsImportHelper.GetSetting(dict, "TimeRangeUnit", (int)settings.TimeRangeUnit);
    }
}
