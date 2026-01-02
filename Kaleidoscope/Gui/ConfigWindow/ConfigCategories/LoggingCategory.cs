using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;
using System.Numerics;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Developer category for configuring log output filtering.
/// Allows enabling/disabling logging for specific code sections/categories.
/// </summary>
public sealed class LoggingCategory
{
    private readonly ConfigurationService _configService;
    
    private static readonly Vector4 HeaderColor = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 EnabledColor = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 DisabledColor = new(0.6f, 0.6f, 0.6f, 1f);

    /// <summary>
    /// Metadata for each log category to display in the UI.
    /// </summary>
    private static readonly (LogCategory Category, string Name, string Description)[] CategoryInfo =
    {
        (LogCategory.Database, "Database", "SQLite operations, migrations, queries"),
        (LogCategory.Cache, "Cache", "Time-series and data caching (hit/miss logging)"),
        (LogCategory.GameState, "Game State", "Inventory, retainer, and currency access"),
        (LogCategory.PriceTracking, "Price Tracking", "Universalis price storage and updates"),
        (LogCategory.Universalis, "Universalis API", "API requests and WebSocket communication"),
        (LogCategory.AutoRetainer, "AutoRetainer IPC", "AutoRetainer plugin integration"),
        (LogCategory.CurrencyTracker, "Currency Tracker", "Currency and data tracking service"),
        (LogCategory.Inventory, "Inventory", "Inventory scanning and caching"),
        (LogCategory.Character, "Character", "Character data and name resolution"),
        (LogCategory.Layout, "Layout", "Layout persistence and editing"),
        (LogCategory.UI, "UI", "Tool rendering and widget operations"),
        (LogCategory.Listings, "Listings", "Market listings service"),
        (LogCategory.Config, "Configuration", "Settings loading and saving"),
    };

    public LoggingCategory(ConfigurationService configService)
    {
        _configService = configService;
    }

    public void Draw()
    {
        ImGui.TextColored(HeaderColor, "Developer Tool - Logging Configuration");
        ImGui.Separator();
        ImGui.Spacing();

        DrawMasterSwitch();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawQuickActions();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawCategoryToggles();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawUsageInfo();
    }

    private void DrawMasterSwitch()
    {
        var enabled = _configService.Config.LogCategoryFilteringEnabled;
        if (ImGui.Checkbox("Enable Category Filtering", ref enabled))
        {
            _configService.Config.LogCategoryFilteringEnabled = enabled;
            _configService.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "When enabled, only logs from selected categories will be output.\n" +
                "When disabled, all logs pass through (default Dalamud behavior).");
        }

        if (!enabled)
        {
            ImGui.TextColored(DisabledColor, "Category filtering is disabled. All logs will be output.");
        }
        else
        {
            var enabledCount = CountEnabledCategories();
            var totalCount = CategoryInfo.Length;
            ImGui.TextColored(EnabledColor, $"Filtering active: {enabledCount}/{totalCount} categories enabled");
        }
    }

    private void DrawQuickActions()
    {
        ImGui.TextUnformatted("Quick Actions:");
        ImGui.SameLine();
        
        if (ImGui.Button("Enable All"))
        {
            _configService.Config.EnabledLogCategories = LogCategory.All;
            _configService.Save();
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Disable All"))
        {
            _configService.Config.EnabledLogCategories = LogCategory.None;
            _configService.Save();
        }
        
        ImGui.SameLine();
        
        if (ImGui.Button("Essential Only"))
        {
            // Enable only error-prone categories that are most useful for debugging
            _configService.Config.EnabledLogCategories = 
                LogCategory.Database | 
                LogCategory.PriceTracking | 
                LogCategory.Universalis;
            _configService.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Enable Database, Price Tracking, and Universalis categories");
        }
    }

    private void DrawCategoryToggles()
    {
        ImGui.TextUnformatted("Log Categories:");
        ImGui.Spacing();

        var config = _configService.Config;
        var changed = false;

        // Use columns for better layout
        if (ImGui.BeginTable("##log_categories", 2, ImGuiTableFlags.None))
        {
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 200f);
            ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);

            foreach (var (category, name, description) in CategoryInfo)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var isEnabled = (config.EnabledLogCategories & category) != 0;
                if (ImGui.Checkbox($"##{name}", ref isEnabled))
                {
                    if (isEnabled)
                        config.EnabledLogCategories |= category;
                    else
                        config.EnabledLogCategories &= ~category;
                    changed = true;
                }
                ImGui.SameLine();
                
                // Color the name based on enabled state
                var nameColor = isEnabled ? EnabledColor : DisabledColor;
                ImGui.TextColored(nameColor, name);

                ImGui.TableNextColumn();
                ImGui.TextDisabled(description);
            }

            ImGui.EndTable();
        }

        if (changed)
        {
            _configService.Save();
        }
    }

    private void DrawUsageInfo()
    {
        if (ImGui.CollapsingHeader("Usage Information"))
        {
            ImGui.Indent();
            ImGui.TextWrapped(
                "Log category filtering helps reduce noise in the Dalamud log when debugging specific issues. " +
                "Enable only the categories relevant to what you're investigating.");
            ImGui.Spacing();
            ImGui.TextWrapped(
                "Note: Some logs may not yet use category filtering. Error and warning logs are generally " +
                "always output regardless of category settings to ensure critical issues are not missed.");
            ImGui.Spacing();
            ImGui.TextDisabled("Tip: Use 'Essential Only' for a good starting point when troubleshooting.");
            ImGui.Unindent();
        }
    }

    private int CountEnabledCategories()
    {
        var count = 0;
        var enabled = _configService.Config.EnabledLogCategories;
        
        foreach (var (category, _, _) in CategoryInfo)
        {
            if ((enabled & category) != 0)
                count++;
        }
        
        return count;
    }
}
