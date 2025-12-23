using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Profiler category for the config window.
/// Shows draw time statistics for debugging and performance analysis.
/// Only visible when CTRL+ALT are held while the config window is focused.
/// </summary>
public class ProfilerCategory
{
    private readonly ProfilerService _profilerService;
    private readonly ConfigurationService _configService;

    public ProfilerCategory(ProfilerService profilerService, ConfigurationService configService)
    {
        _profilerService = profilerService;
        _configService = configService;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Profiler");
        ImGui.Separator();

        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f), "Developer Tool - CTRL+ALT to access");
        ImGui.Spacing();

        // Enable/disable checkbox
        var enabled = _profilerService.IsEnabled;
        if (ImGui.Checkbox("Enable Profiling", ref enabled))
        {
            _profilerService.IsEnabled = enabled;
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset All Stats"))
        {
            _profilerService.ResetAll();
        }

        if (!enabled)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "Profiling is disabled. Enable to collect draw time statistics.");
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Window stats section
        if (ImGui.CollapsingHeader("Window Draw Times", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawStatsTable("Windows", new[]
            {
                _profilerService.MainWindowStats,
                _profilerService.FullscreenWindowStats
            });
        }

        ImGui.Spacing();

        // Tool stats section
        if (ImGui.CollapsingHeader("Tool Draw Times", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var toolStats = _profilerService.ToolStats;
            if (toolStats.Count == 0)
            {
                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "No tool draw times recorded yet.");
            }
            else
            {
                DrawStatsTable("Tools", toolStats.Values.ToArray());
            }
        }
    }

    private void DrawStatsTable(string label, ProfilerService.ProfileStats[] stats)
    {
        if (stats.Length == 0) return;

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (ImGui.BeginTable($"##{label}_stats_table", 6, tableFlags))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2f);
            ImGui.TableSetupColumn("Last (ms)", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableSetupColumn("Min (ms)", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableSetupColumn("Max (ms)", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableSetupColumn("Avg (ms)", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableSetupColumn("Samples", ImGuiTableColumnFlags.None, 1f);
            ImGui.TableHeadersRow();

            foreach (var stat in stats.OrderBy(s => s.Name))
            {
                if (stat.SampleCount == 0) continue;

                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(stat.Name);

                ImGui.TableNextColumn();
                DrawTimeValue(stat.LastDrawTimeMs);

                ImGui.TableNextColumn();
                DrawTimeValue(stat.MinDrawTimeMs == double.MaxValue ? 0 : stat.MinDrawTimeMs);

                ImGui.TableNextColumn();
                DrawTimeValue(stat.MaxDrawTimeMs == double.MinValue ? 0 : stat.MaxDrawTimeMs);

                ImGui.TableNextColumn();
                DrawTimeValue(stat.AverageDrawTimeMs);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(stat.SampleCount.ToString("N0"));
            }

            ImGui.EndTable();
        }
    }

    private void DrawTimeValue(double ms)
    {
        // Color code: green < 1ms, yellow 1-5ms, orange 5-16ms, red > 16ms
        var color = ms switch
        {
            < 1.0 => new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f),   // Green
            < 5.0 => new System.Numerics.Vector4(1f, 1f, 0.2f, 1f),     // Yellow
            < 16.67 => new System.Numerics.Vector4(1f, 0.6f, 0.2f, 1f), // Orange (< 60fps)
            _ => new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f)        // Red
        };

        ImGui.TextColored(color, $"{ms:F3}");
    }
}
