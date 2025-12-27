using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Sampler configuration category in the config window.
/// Controls sampling interval, enable/disable state, and which data types to track.
/// </summary>
public class SamplerCategory
{
    private readonly SamplerService _samplerService;
    private readonly ConfigurationService _configService;
    private readonly TrackedDataRegistry _registry;

    public SamplerCategory(SamplerService samplerService, ConfigurationService configService, TrackedDataRegistry registry)
    {
        _samplerService = samplerService;
        _configService = configService;
        _registry = registry;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Sampler Settings");
        ImGui.Separator();

        var enabled = _samplerService.Enabled;
        if (ImGui.Checkbox("Enable sampler", ref enabled))
        {
            _samplerService.Enabled = enabled;
            _configService.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When enabled, the plugin will periodically sample and record tracked data.");
        }

        var interval = _samplerService.IntervalMs;
        if (ImGui.InputInt("Sampler interval (ms)", ref interval))
        {
            if (interval < ConfigStatic.MinSamplerIntervalMs) 
                interval = ConfigStatic.MinSamplerIntervalMs;
            _samplerService.IntervalMs = interval;
            _configService.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"How often to sample data. Minimum: {ConfigStatic.MinSamplerIntervalMs}ms");
        }

        ImGui.Spacing();
        ImGui.Spacing();
        DrawTrackedDataTypes();
    }

    private void DrawTrackedDataTypes()
    {
        ImGui.TextUnformatted("Tracked Data Types");
        ImGui.Separator();
        ImGui.TextDisabled("Select which currencies and resources to track over time.");
        ImGui.Spacing();

        var config = _configService.Config;
        var enabledTypes = config.EnabledTrackedDataTypes;
        var anyChanged = false;

        // Group by category
        var categories = Enum.GetValues<TrackedDataCategory>();
        foreach (var category in categories)
        {
            var definitions = _registry.GetByCategory(category).ToList();
            if (definitions.Count == 0) continue;

            var categoryName = GetCategoryDisplayName(category);
            if (ImGui.CollapsingHeader(categoryName, ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                foreach (var def in definitions)
                {
                    var isEnabled = enabledTypes.Contains(def.Type);
                    if (ImGui.Checkbox(def.DisplayName, ref isEnabled))
                    {
                        if (isEnabled)
                            enabledTypes.Add(def.Type);
                        else
                            enabledTypes.Remove(def.Type);
                        anyChanged = true;
                    }
                    if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(def.Description))
                    {
                        ImGui.SetTooltip(def.Description);
                    }
                }
                ImGui.Unindent();
            }
        }

        if (anyChanged)
        {
            _configService.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        
        // Quick actions
        if (ImGui.Button("Enable All"))
        {
            foreach (var def in _registry.Definitions.Values)
            {
                enabledTypes.Add(def.Type);
            }
            _configService.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Disable All"))
        {
            enabledTypes.Clear();
            _configService.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset to Defaults"))
        {
            enabledTypes.Clear();
            foreach (var def in _registry.Definitions.Values)
            {
                if (def.EnabledByDefault)
                    enabledTypes.Add(def.Type);
            }
            _configService.Save();
        }
    }

    private static string GetCategoryDisplayName(TrackedDataCategory category)
    {
        return category switch
        {
            TrackedDataCategory.Gil => "Gil",
            TrackedDataCategory.Tomestone => "Tomestones",
            TrackedDataCategory.Scrip => "Scrips",
            TrackedDataCategory.GrandCompany => "Grand Company",
            TrackedDataCategory.PvP => "PvP",
            TrackedDataCategory.Hunt => "Hunts",
            TrackedDataCategory.GoldSaucer => "Gold Saucer",
            TrackedDataCategory.Tribal => "Tribal / FATE",
            TrackedDataCategory.Crafting => "Crafting / Gathering",
            TrackedDataCategory.Inventory => "Inventory",
            TrackedDataCategory.Universalis => "Universalis / Value",
            _ => category.ToString()
        };
    }
}
