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
    
    /// <summary>
    /// Which stats view to show (Basic, Percentiles, Rolling, All).
    /// </summary>
    private int _selectedStatsView;
    private static readonly string[] StatsViewOptions = { "Basic", "Percentiles", "Rolling", "All" };
    
    /// <summary>
    /// Whether to show the histogram panel.
    /// </summary>
    private bool _showHistogram;
    
    /// <summary>
    /// Selected tool for histogram display.
    /// </summary>
    private string? _selectedHistogramTool;
    
    /// <summary>
    /// Whether to expand child scopes in tool stats.
    /// </summary>
    private bool _showChildScopes = true;

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

        // Keep developer mode enabled checkbox
        var devModeEnabled = _configService.Config.DeveloperModeEnabled;
        if (ImGui.Checkbox("Keep Developer Mode Enabled", ref devModeEnabled))
        {
            _configService.Config.DeveloperModeEnabled = devModeEnabled;
            _configService.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When enabled, the Developer section stays visible without holding CTRL+ALT");
        }

        ImGui.Spacing();
        ImGui.Separator();
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

        // Stats view selector
        ImGui.SetNextItemWidth(150);
        ImGui.Combo("Stats View", ref _selectedStatsView, StatsViewOptions, StatsViewOptions.Length);
        
        ImGui.SameLine();
        ImGui.Checkbox("Show Histogram", ref _showHistogram);
        
        ImGui.SameLine();
        ImGui.Checkbox("Show Child Scopes", ref _showChildScopes);

        ImGui.Spacing();

        // Memory stats section
        DrawMemoryStats();

        ImGui.Separator();
        ImGui.Spacing();

        // Window stats section
        if (ImGui.CollapsingHeader("Window Draw Times", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawStatsTable("Windows", new[]
            {
                _profilerService.MainWindowStats,
                _profilerService.FullscreenWindowStats
            }, false);
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
                DrawStatsTable("Tools", toolStats.Values.ToArray(), _showChildScopes);
            }
        }

        // Histogram panel
        if (_showHistogram)
        {
            ImGui.Spacing();
            DrawHistogramPanel();
        }
    }

    private void DrawMemoryStats()
    {
        var (gen0, gen1, gen2) = _profilerService.GetGcCollectionCounts();
        var totalMemory = _profilerService.GetTotalManagedMemory();
        var memoryMb = totalMemory / (1024.0 * 1024.0);

        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "Memory:");
        ImGui.SameLine();
        ImGui.TextUnformatted($"{memoryMb:F2} MB");
        ImGui.SameLine();
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "|");
        ImGui.SameLine();
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1f, 1f), "GC:");
        ImGui.SameLine();
        ImGui.TextUnformatted($"Gen0: {gen0}  Gen1: {gen1}  Gen2: {gen2}");
    }

    private void DrawStatsTable(string label, ProfilerService.ProfileStats[] stats, bool showChildScopes)
    {
        if (stats.Length == 0) return;

        var columnCount = _selectedStatsView switch
        {
            0 => 7,  // Basic: Name, Last, Min, Max, Avg, StdDev, Samples
            1 => 6,  // Percentiles: Name, P50, P90, P95, P99, Samples
            2 => 6,  // Rolling: Name, 1s, 5s, FPS, Jitter, Samples
            _ => 12  // All: Everything
        };

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable 
                       | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollX;
        
        if (ImGui.BeginTable($"##{label}_stats_table", columnCount, tableFlags))
        {
            SetupTableColumns();
            ImGui.TableHeadersRow();

            foreach (var stat in stats.OrderBy(s => s.Name))
            {
                if (stat.SampleCount == 0) continue;

                DrawStatsRow(stat, 0);

                // Draw child scopes if enabled
                if (showChildScopes && stat.ChildScopes.Count > 0)
                {
                    foreach (var child in stat.ChildScopes.Values.OrderBy(c => c.Name))
                    {
                        if (child.SampleCount == 0) continue;
                        DrawStatsRow(child, 1);
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    private void SetupTableColumns()
    {
        switch (_selectedStatsView)
        {
            case 0: // Basic
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2f);
                ImGui.TableSetupColumn("Last (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("Min (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("Max (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("Avg (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("StdDev", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("Samples", ImGuiTableColumnFlags.None, 1f);
                break;

            case 1: // Percentiles
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2f);
                ImGui.TableSetupColumn("P50 (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("P90 (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("P95 (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("P99 (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("Samples", ImGuiTableColumnFlags.None, 1f);
                break;

            case 2: // Rolling
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2f);
                ImGui.TableSetupColumn("1s Avg (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("5s Avg (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("Rate/s", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("Jitter (ms)", ImGuiTableColumnFlags.None, 1f);
                ImGui.TableSetupColumn("Samples", ImGuiTableColumnFlags.None, 1f);
                break;

            default: // All
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2f);
                ImGui.TableSetupColumn("Last", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("Min", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("Max", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("Avg", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("StdDev", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("P50", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("P95", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("P99", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("1s Avg", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("Rate/s", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupColumn("Samples", ImGuiTableColumnFlags.None, 0.9f);
                break;
        }
    }

    private void DrawStatsRow(ProfilerService.ProfileStats stat, int indentLevel)
    {
        ImGui.TableNextRow();

        // Name column with optional indent
        ImGui.TableNextColumn();
        if (indentLevel > 0)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f), new string(' ', indentLevel * 2) + "└ " + stat.Name);
        }
        else
        {
            ImGui.TextUnformatted(stat.Name);
        }

        switch (_selectedStatsView)
        {
            case 0: // Basic
                ImGui.TableNextColumn();
                DrawTimeValue(stat.LastDrawTimeMs);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.MinDrawTimeMs == double.MaxValue ? 0 : stat.MinDrawTimeMs);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.MaxDrawTimeMs == double.MinValue ? 0 : stat.MaxDrawTimeMs);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.AverageDrawTimeMs);
                ImGui.TableNextColumn();
                DrawStdDevValue(stat.StandardDeviationMs);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(stat.SampleCount.ToString("N0"));
                break;

            case 1: // Percentiles
                ImGui.TableNextColumn();
                DrawTimeValue(stat.P50Ms);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.P90Ms);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.P95Ms);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.P99Ms);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(stat.SampleCount.ToString("N0"));
                break;

            case 2: // Rolling
                ImGui.TableNextColumn();
                DrawTimeValue(stat.Rolling1SecMs);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.Rolling5SecMs);
                ImGui.TableNextColumn();
                DrawRateValue(stat.SamplesPerSecond);
                ImGui.TableNextColumn();
                DrawJitterValue(stat.JitterMs);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(stat.SampleCount.ToString("N0"));
                break;

            default: // All
                ImGui.TableNextColumn();
                DrawTimeValue(stat.LastDrawTimeMs);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.MinDrawTimeMs == double.MaxValue ? 0 : stat.MinDrawTimeMs);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.MaxDrawTimeMs == double.MinValue ? 0 : stat.MaxDrawTimeMs);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.AverageDrawTimeMs);
                ImGui.TableNextColumn();
                DrawStdDevValue(stat.StandardDeviationMs);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.P50Ms);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.P95Ms);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.P99Ms);
                ImGui.TableNextColumn();
                DrawTimeValue(stat.Rolling1SecMs);
                ImGui.TableNextColumn();
                DrawRateValue(stat.SamplesPerSecond);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(stat.SampleCount.ToString("N0"));
                break;
        }
    }

    private void DrawHistogramPanel()
    {
        if (ImGui.CollapsingHeader("Sample Distribution Histogram"))
        {
            var toolStats = _profilerService.ToolStats;
            var toolNames = toolStats.Keys.ToArray();

            if (toolNames.Length == 0)
            {
                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "No tool stats available for histogram.");
                return;
            }

            // Tool selector
            var selectedIndex = Array.IndexOf(toolNames, _selectedHistogramTool);
            if (selectedIndex < 0) selectedIndex = 0;
            
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("Select Tool", ref selectedIndex, toolNames, toolNames.Length))
            {
                _selectedHistogramTool = toolNames[selectedIndex];
            }

            _selectedHistogramTool ??= toolNames[0];

            if (toolStats.TryGetValue(_selectedHistogramTool, out var stat))
            {
                DrawHistogram(stat);
            }
        }
    }

    private void DrawHistogram(ProfilerService.ProfileStats stat)
    {
        var samples = stat.GetRecentSamples();
        if (samples.Length == 0)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "No recent samples for histogram.");
            return;
        }

        // Create histogram buckets (0-1ms, 1-2ms, 2-5ms, 5-10ms, 10-16ms, 16+ms)
        var buckets = new int[6];
        var bucketLabels = new[] { "0-1ms", "1-2ms", "2-5ms", "5-10ms", "10-16ms", "16+ms" };

        foreach (var sample in samples)
        {
            var index = sample switch
            {
                < 1.0 => 0,
                < 2.0 => 1,
                < 5.0 => 2,
                < 10.0 => 3,
                < 16.67 => 4,
                _ => 5
            };
            buckets[index]++;
        }

        var maxBucket = buckets.Max();
        if (maxBucket == 0) maxBucket = 1;

        ImGui.Text($"Distribution of {samples.Length} recent samples:");
        ImGui.Spacing();

        // Draw horizontal bar chart
        var availWidth = ImGui.GetContentRegionAvail().X - 100;
        for (var i = 0; i < buckets.Length; i++)
        {
            var pct = buckets[i] / (float)samples.Length * 100f;
            var barWidth = (buckets[i] / (float)maxBucket) * availWidth;

            var color = i switch
            {
                0 => new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f),   // Green
                1 => new System.Numerics.Vector4(0.6f, 1f, 0.2f, 1f),   // Light green
                2 => new System.Numerics.Vector4(1f, 1f, 0.2f, 1f),     // Yellow
                3 => new System.Numerics.Vector4(1f, 0.8f, 0.2f, 1f),   // Orange
                4 => new System.Numerics.Vector4(1f, 0.6f, 0.2f, 1f),   // Dark orange
                _ => new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f)    // Red
            };

            ImGui.TextUnformatted($"{bucketLabels[i],-8}");
            ImGui.SameLine();
            
            var cursorPos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(
                cursorPos,
                new System.Numerics.Vector2(cursorPos.X + barWidth, cursorPos.Y + 14),
                ImGui.GetColorU32(color));
            
            ImGui.Dummy(new System.Numerics.Vector2(availWidth, 14));
            ImGui.SameLine();
            ImGui.TextUnformatted($"{buckets[i],4} ({pct:F1}%)");
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

    private void DrawStdDevValue(double stdDev)
    {
        // Color code based on consistency: green < 0.5ms, yellow < 2ms, red >= 2ms
        var color = stdDev switch
        {
            < 0.5 => new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f),   // Green (consistent)
            < 2.0 => new System.Numerics.Vector4(1f, 1f, 0.2f, 1f),     // Yellow (moderate variance)
            _ => new System.Numerics.Vector4(1f, 0.6f, 0.2f, 1f)        // Orange (high variance)
        };

        ImGui.TextColored(color, $"±{stdDev:F2}");
    }

    private void DrawRateValue(double rate)
    {
        // Color code: green >= 60, yellow >= 30, red < 30
        var color = rate switch
        {
            >= 60.0 => new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f),  // Green
            >= 30.0 => new System.Numerics.Vector4(1f, 1f, 0.2f, 1f),    // Yellow
            _ => new System.Numerics.Vector4(1f, 0.2f, 0.2f, 1f)         // Red
        };

        ImGui.TextColored(color, $"{rate:F0}");
    }

    private void DrawJitterValue(double jitter)
    {
        // Color code: green < 2ms, yellow < 10ms, red >= 10ms
        var color = jitter switch
        {
            < 2.0 => new System.Numerics.Vector4(0.2f, 1f, 0.2f, 1f),    // Green (stable)
            < 10.0 => new System.Numerics.Vector4(1f, 1f, 0.2f, 1f),     // Yellow (some variance)
            _ => new System.Numerics.Vector4(1f, 0.6f, 0.2f, 1f)         // Orange (unstable)
        };

        ImGui.TextColored(color, $"{jitter:F2}");
    }
}
