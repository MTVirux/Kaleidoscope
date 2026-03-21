using System.Numerics;
using System.Reflection;
using Kaleidoscope.Gui.MainWindow;
using Kaleidoscope.Gui.MainWindow.Tools.Data;
using Kaleidoscope.Services;
using Microsoft.Extensions.DependencyInjection;
using OtterGui.Services;

namespace Kaleidoscope.Gui.MainWindow;

/// <summary>
/// Describes a single tool registration discovered from <see cref="ToolTypeAttribute"/>.
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>Unique tool identifier (from attribute).</summary>
    public required string Id { get; init; }

    /// <summary>Display label for menus.</summary>
    public required string Label { get; init; }

    /// <summary>Category path for context menu grouping.</summary>
    public required string Category { get; init; }

    /// <summary>Tooltip description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The concrete tool class to instantiate.</summary>
    public required Type ToolType { get; init; }

    /// <summary>Optional variant key for post-creation configuration (e.g. "Graph", "Table").</summary>
    public string? Variant { get; init; }

    /// <summary>Service types that must be non-null for this tool to be available.</summary>
    public Type[] RequiredServices { get; init; } = [];
}

/// <summary>
/// Creates tool instances via DI, replacing <c>ToolCreationContext</c> and manual factory methods.
/// Discovers tool types by scanning for <see cref="ToolTypeAttribute"/> at construction time.
/// </summary>
public sealed class ToolFactory : IService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigurationService _configService;
    private readonly Dictionary<string, ToolDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ToolDefinition>> _byCategory = new(StringComparer.OrdinalIgnoreCase);

    // Post-create actions keyed by variant string (e.g. "Graph" → set ViewMode)
    private readonly Dictionary<string, Action<ToolComponent>> _variantActions = new(StringComparer.OrdinalIgnoreCase);

    // Legacy ID mappings for layout migration
    private readonly Dictionary<string, string> _legacyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TopItems"] = "TopInventoryValueItems",
    };

    public ToolFactory(ServiceManager serviceManager, ConfigurationService configService)
    {
        _serviceProvider = serviceManager.Provider
            ?? throw new InvalidOperationException("ServiceProvider not yet created");
        _configService = configService;

        RegisterVariantActions();
        DiscoverToolTypes();
    }

    /// <summary>
    /// Creates a tool instance by ID, resolving constructor dependencies from the DI container.
    /// Returns null if the tool is unavailable (missing required services) or creation fails.
    /// </summary>
    public ToolComponent? Create(string id, Vector2? position = null)
    {
        // Resolve legacy IDs
        if (_legacyIds.TryGetValue(id, out var resolvedId))
            id = resolvedId;

        if (!_definitions.TryGetValue(id, out var def))
        {
            LogService.Debug(LogCategory.UI, $"ToolFactory.Create: unknown tool id '{id}'");
            return null;
        }

        if (!AreRequiredServicesAvailable(def))
        {
            LogService.Debug(LogCategory.UI, $"ToolFactory.Create: required services missing for '{id}'");
            return null;
        }

        try
        {
            var tool = (ToolComponent)ActivatorUtilities.CreateInstance(_serviceProvider, def.ToolType);

            if (position.HasValue)
                tool.Position = position.Value;

            // Apply variant-specific configuration
            if (def.Variant != null && _variantActions.TryGetValue(def.Variant, out var action))
                action(tool);

            ApplyDefaultColors(tool);
            return tool;
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, $"ToolFactory.Create: failed to create '{id}'", ex);
            return null;
        }
    }

    /// <summary>Gets a tool definition by ID, or null if not found.</summary>
    public ToolDefinition? GetDefinition(string id)
    {
        if (_legacyIds.TryGetValue(id, out var resolvedId))
            id = resolvedId;
        return _definitions.GetValueOrDefault(id);
    }

    /// <summary>Gets all registered tool definitions.</summary>
    public IReadOnlyDictionary<string, ToolDefinition> GetAllDefinitions() => _definitions;

    /// <summary>Gets tool definitions grouped by category.</summary>
    public IReadOnlyDictionary<string, List<ToolDefinition>> GetDefinitionsByCategory() => _byCategory;

    /// <summary>Gets all available tool definitions (whose required services are present).</summary>
    public IReadOnlyDictionary<string, List<ToolDefinition>> GetAvailableDefinitionsByCategory()
    {
        var result = new Dictionary<string, List<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, defs) in _byCategory)
        {
            var available = defs.Where(AreRequiredServicesAvailable).ToList();
            if (available.Count > 0)
                result[category] = available;
        }
        return result;
    }

    /// <summary>Checks whether a tool type name matches any known definition.</summary>
    public ToolDefinition? FindDefinitionByTypeName(string typeFullName)
    {
        foreach (var def in _definitions.Values)
        {
            if (def.ToolType.FullName == typeFullName)
                return def;
        }
        return null;
    }

    // ── Private ─────────────────────────────────────────────────────────

    private void RegisterVariantActions()
    {
        _variantActions["Graph"] = tool =>
        {
            if (tool is DataTool dt)
                dt.ConfigureSettings(s => s.ViewMode = DataToolViewMode.Graph);
        };
        _variantActions["Table"] = tool =>
        {
            if (tool is DataTool dt)
                dt.ConfigureSettings(s => s.ViewMode = DataToolViewMode.Table);
        };
    }

    private void DiscoverToolTypes()
    {
        var assembly = typeof(ToolComponent).Assembly;
        foreach (var type in assembly.ExportedTypes)
        {
            if (type.IsAbstract || type.IsInterface)
                continue;
            if (!typeof(ToolComponent).IsAssignableFrom(type))
                continue;

            var attrs = type.GetCustomAttributes<ToolTypeAttribute>(false);
            foreach (var attr in attrs)
            {
                var def = new ToolDefinition
                {
                    Id = attr.Id,
                    Label = attr.Label,
                    Category = attr.Category,
                    Description = attr.Description,
                    ToolType = type,
                    Variant = attr.Variant,
                    RequiredServices = attr.RequiredServices,
                };

                if (_definitions.TryGetValue(def.Id, out var existing))
                {
                    LogService.Error(LogCategory.UI,
                        $"ToolFactory: duplicate tool id '{def.Id}' on {type.Name} (already registered by {existing.ToolType.Name})");
                    continue;
                }

                _definitions[def.Id] = def;

                if (!_byCategory.TryGetValue(def.Category, out var list))
                {
                    list = new List<ToolDefinition>();
                    _byCategory[def.Category] = list;
                }
                list.Add(def);
            }
        }

        LogService.Debug(LogCategory.UI,
            $"ToolFactory: discovered {_definitions.Count} tool definitions across {_byCategory.Count} categories");
    }

    private bool AreRequiredServicesAvailable(ToolDefinition def)
    {
        foreach (var serviceType in def.RequiredServices)
        {
            if (_serviceProvider.GetService(serviceType) == null)
                return false;
        }
        return true;
    }

    private void ApplyDefaultColors(ToolComponent tool)
    {
        var colors = _configService.Config.UIColors;
        tool.BackgroundColor = colors.ToolBackground;
    }
}
