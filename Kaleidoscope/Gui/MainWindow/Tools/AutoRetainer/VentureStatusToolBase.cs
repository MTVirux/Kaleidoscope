using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Gui.Widgets;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// Sort order options for the venture status tools.
/// </summary>
public enum VentureSortOrder
{
    Alphabetical,
    ReverseAlphabetical,
    CharacterAlphabetical,
    CharacterReverseAlphabetical,
    AutoRetainerOrder,
    EarliestToComplete,
    LatestToComplete
}

/// <summary>
/// Represents an entity that can be on a venture/voyage (retainer or vessel).
/// </summary>
public interface IVentureEntity
{
    /// <summary>The name of the entity.</summary>
    string Name { get; }
    
    /// <summary>Whether the entity is currently on a venture/voyage.</summary>
    bool IsOnVenture { get; }
    
    /// <summary>Unix timestamp when the venture/voyage ends (0 if not on venture).</summary>
    long EndTime { get; }
}

/// <summary>
/// Adapter for retainer venture data.
/// </summary>
public readonly struct RetainerVentureAdapter : IVentureEntity
{
    private readonly AutoRetainerRetainerData _data;
    
    public RetainerVentureAdapter(AutoRetainerRetainerData data) => _data = data;
    
    public string Name => _data.Name;
    public bool IsOnVenture => _data.HasVenture;
    public long EndTime => _data.VentureEndsAt;
}

/// <summary>
/// Adapter for vessel voyage data.
/// </summary>
public readonly struct VesselVoyageAdapter : IVentureEntity
{
    private readonly AutoRetainerVesselData _data;
    
    public VesselVoyageAdapter(AutoRetainerVesselData data) => _data = data;
    
    public string Name => _data.Name;
    public bool IsOnVenture => _data.ReturnTime > 0;
    public long EndTime => _data.ReturnTime;
}

/// <summary>
/// Base class for venture/voyage status tools that share common functionality.
/// Provides unified handling for retainer ventures and submersible voyages.
/// </summary>
public abstract class VentureStatusToolBase : ToolComponent
{
    protected readonly AutoRetainerIpcService? AutoRetainerIpc;
    protected readonly ConfigurationService? ConfigService;

    // Cached state
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMilliseconds(100);
    protected List<AutoRetainerCharacterData>? Characters;

    // Default colors
    private static readonly Vector4 DefaultReadyColor = new(0.4f, 1.0f, 0.4f, 1f);
    private static readonly Vector4 DefaultActiveColor = new(1.0f, 1.0f, 1.0f, 1f);
    private static readonly Vector4 DefaultDisabledColor = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 DefaultNoVentureColor = new(0.6f, 0.6f, 0.6f, 1f);

    // Configurable colors
    public Vector4 ReadyColor { get; set; } = DefaultReadyColor;
    public Vector4 ActiveColor { get; set; } = DefaultActiveColor;
    public Vector4 DisabledColor { get; set; } = DefaultDisabledColor;
    public Vector4 NoVentureColor { get; set; } = DefaultNoVentureColor;

    /// <summary>Whether to group entities by character.</summary>
    public bool GroupByCharacter { get; set; } = true;

    /// <summary>Whether to hide character names when not grouping.</summary>
    public bool HideCharacterName { get; set; } = false;

    /// <summary>The sort order for displaying entities.</summary>
    public VentureSortOrder SortOrder { get; set; } = VentureSortOrder.EarliestToComplete;

    /// <summary>Set of character IDs that are hidden.</summary>
    public HashSet<ulong> HiddenCharacters { get; set; } = new();

    /// <summary>Set of entity identifiers (CID_Name) that are hidden.</summary>
    public HashSet<string> HiddenEntities { get; set; } = new();

    /// <summary>Whether ready entities should appear at the top.</summary>
    public bool ReadyOnTop { get; set; } = true;

    // Abstract properties for subclass customization
    protected abstract string EntityNameSingular { get; }  // "Retainer" or "Submersible"
    protected abstract string EntityNamePlural { get; }    // "Retainers" or "Submersibles"
    protected abstract string VentureNameSingular { get; } // "Venture" or "Voyage"
    protected abstract string HiddenEntitiesSettingsKey { get; } // "HiddenRetainers" or "HiddenSubmersibles"
    protected abstract string NoVentureColorSettingsKey { get; } // "NoVentureColor" or "NoVoyageColor"

    protected VentureStatusToolBase(AutoRetainerIpcService? autoRetainerIpc, ConfigurationService? configService)
    {
        AutoRetainerIpc = autoRetainerIpc;
        ConfigService = configService;
        Size = new Vector2(300, 400);
    }

    /// <summary>
    /// Gets the entities for a character that this tool should display.
    /// </summary>
    protected abstract IEnumerable<IVentureEntity> GetEntities(AutoRetainerCharacterData character);

    /// <summary>
    /// Gets the formatted character name based on settings.
    /// </summary>
    protected string GetFormattedCharacterName(AutoRetainerCharacterData character)
    {
        var format = ConfigService?.Config.CharacterNameFormat ?? CharacterNameFormat.FullName;
        return TimeSeriesCacheService.FormatName(character.Name, format) ?? character.Name;
    }

    public override void RenderToolContent()
    {
        try
        {
            RefreshCachedState();

            if (AutoRetainerIpc == null || !AutoRetainerIpc.IsAvailable)
            {
                ImGui.TextColored(DisabledColor, "AutoRetainer not available");
                return;
            }

            if (Characters == null || Characters.Count == 0)
            {
                ImGui.TextColored(DisabledColor, "No characters registered");
                return;
            }

            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            var nowMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            if (GroupByCharacter)
            {
                DrawGroupedByCharacter(now, nowMs);
            }
            else
            {
                DrawFlatList(now, nowMs);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[{GetType().Name}] Draw error: {ex.Message}");
        }
    }

    private void RefreshCachedState()
    {
        if (AutoRetainerIpc == null || !AutoRetainerIpc.IsAvailable) return;

        var now = DateTime.Now;
        if (now - _lastRefresh < _refreshInterval) return;

        _lastRefresh = now;

        try
        {
            Characters = AutoRetainerIpc.GetAllFullCharacterData();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[{GetType().Name}] Refresh error: {ex.Message}");
        }
    }

    private void DrawGroupedByCharacter(long nowUnix, long nowMs)
    {
        var sortedCharacters = GetSortedCharacters(nowUnix);

        foreach (var character in sortedCharacters)
        {
            if (HiddenCharacters.Contains(character.CID))
                continue;

            var entities = GetEntities(character).ToList();
            if (entities.Count == 0)
                continue;

            ImGui.PushID((int)character.CID);

            var headerLabel = $"{GetFormattedCharacterName(character)} @ {character.World}";
            if (ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawCharacterContextMenu(character.CID);

                var visibleEntities = entities.Where(e => !IsEntityHidden(character.CID, e.Name)).ToList();
                var sortedEntities = GetSortedEntities(visibleEntities, nowUnix);

                var tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.Borders;

                if (ImGui.BeginTable($"EntityTable_{character.CID}", 2, tableFlags))
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);

                    ImGui.TableHeadersRow();

                    foreach (var entity in sortedEntities)
                    {
                        DrawEntityTableRow(entity, character.CID, nowUnix, nowMs);
                    }

                    ImGui.EndTable();
                }
            }
            else
            {
                DrawCharacterContextMenu(character.CID);
            }

            ImGui.PopID();
        }
    }

    private void DrawCharacterContextMenu(ulong characterCid)
    {
        if (ImGui.BeginPopupContextItem($"CharContext_{characterCid}"))
        {
            if (ImGui.MenuItem("Hide Character"))
            {
                HiddenCharacters.Add(characterCid);
                NotifyToolSettingsChanged();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawFlatList(long nowUnix, long nowMs)
    {
        var allEntities = new List<(IVentureEntity Entity, AutoRetainerCharacterData Character)>();

        foreach (var character in Characters!)
        {
            if (HiddenCharacters.Contains(character.CID))
                continue;

            foreach (var entity in GetEntities(character))
            {
                if (IsEntityHidden(character.CID, entity.Name))
                    continue;
                    
                allEntities.Add((entity, character));
            }
        }

        allEntities = SortFlatEntityList(allEntities, nowUnix);

        var showCharacter = !HideCharacterName;
        var columnCount = showCharacter ? 3 : 2;

        var tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY;
        var tableHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginTable("EntityFlatTable", columnCount, tableFlags, new Vector2(0, tableHeight)))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            if (showCharacter)
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);

            ImGui.TableHeadersRow();

            foreach (var (entity, character) in allEntities)
            {
                DrawEntityTableRowWithCharacter(entity, character, nowUnix, nowMs, showCharacter);
            }

            ImGui.EndTable();
        }
    }

    private void DrawEntityTableRow(IVentureEntity entity, ulong characterCid, long nowUnix, long nowMs)
    {
        ImGui.TableNextRow();
        
        ImGui.TableNextColumn();
        var (color, _) = GetEntityDisplayInfo(entity, nowUnix);
        ImGui.TextColored(color, entity.Name);
        
        DrawEntityContextMenu(characterCid, entity.Name);

        ImGui.TableNextColumn();
        DrawEntityStatus(entity, nowUnix, nowMs);
    }

    private void DrawEntityTableRowWithCharacter(IVentureEntity entity, AutoRetainerCharacterData character, long nowUnix, long nowMs, bool showCharacter)
    {
        ImGui.TableNextRow();
        
        ImGui.TableNextColumn();
        var (color, _) = GetEntityDisplayInfo(entity, nowUnix);
        ImGui.TextColored(color, entity.Name);

        DrawEntityContextMenu(character.CID, entity.Name);

        if (showCharacter)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(DisabledColor, GetFormattedCharacterName(character));
        }

        ImGui.TableNextColumn();
        DrawEntityStatus(entity, nowUnix, nowMs);
    }

    private void DrawEntityContextMenu(ulong characterCid, string entityName)
    {
        if (ImGui.BeginPopupContextItem($"EntityContext_{characterCid}_{entityName}"))
        {
            if (ImGui.MenuItem($"Hide {EntityNameSingular}"))
            {
                HiddenEntities.Add(GetEntityKey(characterCid, entityName));
                NotifyToolSettingsChanged();
            }
            ImGui.EndPopup();
        }
    }

    private (Vector4 Color, bool IsReady) GetEntityDisplayInfo(IVentureEntity entity, long nowUnix)
    {
        if (!entity.IsOnVenture)
            return (NoVentureColor, false);
        
        var isReady = entity.EndTime <= nowUnix;
        return (isReady ? ReadyColor : ActiveColor, isReady);
    }

    private void DrawEntityStatus(IVentureEntity entity, long nowUnix, long nowMs)
    {
        if (!entity.IsOnVenture)
        {
            ImGui.TextColored(DisabledColor, $"No {VentureNameSingular.ToLowerInvariant()}");
        }
        else if (entity.EndTime <= nowUnix)
        {
            ImGui.TextColored(ReadyColor, "Ready!");
        }
        else
        {
            var timeRemaining = FormatUtils.FormatCountdownPrecise(entity.EndTime, nowUnix, nowMs);
            ImGui.TextColored(ActiveColor, timeRemaining);
        }
    }

    protected static string GetEntityKey(ulong characterCid, string entityName) => $"{characterCid}_{entityName}";

    private bool IsEntityHidden(ulong characterCid, string entityName) 
        => HiddenEntities.Contains(GetEntityKey(characterCid, entityName));

    protected string GetEntityDisplayName(string entityKey)
    {
        var parts = entityKey.Split('_', 2);
        if (parts.Length != 2 || !ulong.TryParse(parts[0], out var cid))
            return entityKey;

        var entityName = parts[1];
        var characterName = "Unknown";
        
        if (Characters != null)
        {
            var character = Characters.FirstOrDefault(c => c.CID == cid);
            if (character != null)
            {
                characterName = GetFormattedCharacterName(character);
            }
        }

        return $"{entityName} ({characterName})";
    }

    private List<AutoRetainerCharacterData> GetSortedCharacters(long nowUnix)
    {
        if (Characters == null) return new List<AutoRetainerCharacterData>();

        return SortOrder switch
        {
            VentureSortOrder.CharacterAlphabetical or VentureSortOrder.Alphabetical =>
                Characters.OrderBy(c => c.Name).ToList(),
            VentureSortOrder.CharacterReverseAlphabetical or VentureSortOrder.ReverseAlphabetical =>
                Characters.OrderByDescending(c => c.Name).ToList(),
            VentureSortOrder.EarliestToComplete =>
                Characters.OrderBy(c => GetEntities(c)
                    .Where(e => e.IsOnVenture)
                    .Select(e => e.EndTime)
                    .DefaultIfEmpty(long.MaxValue).Min()).ToList(),
            VentureSortOrder.LatestToComplete =>
                Characters.OrderByDescending(c => GetEntities(c)
                    .Where(e => e.IsOnVenture)
                    .Select(e => e.EndTime)
                    .DefaultIfEmpty(0).Max()).ToList(),
            _ => Characters.ToList()
        };
    }

    private List<IVentureEntity> GetSortedEntities(List<IVentureEntity> entities, long nowUnix)
    {
        var sorted = SortOrder switch
        {
            VentureSortOrder.Alphabetical or VentureSortOrder.CharacterAlphabetical =>
                entities.OrderBy(e => e.Name).ToList(),
            VentureSortOrder.ReverseAlphabetical or VentureSortOrder.CharacterReverseAlphabetical =>
                entities.OrderByDescending(e => e.Name).ToList(),
            VentureSortOrder.EarliestToComplete =>
                entities.OrderBy(e => e.IsOnVenture ? e.EndTime : long.MaxValue).ToList(),
            VentureSortOrder.LatestToComplete =>
                entities.OrderByDescending(e => e.IsOnVenture ? e.EndTime : 0).ToList(),
            _ => entities.ToList()
        };

        return ApplyReadyOrdering(sorted, e => e.IsOnVenture && e.EndTime <= nowUnix);
    }

    private List<(IVentureEntity Entity, AutoRetainerCharacterData Character)> SortFlatEntityList(
        List<(IVentureEntity Entity, AutoRetainerCharacterData Character)> list, long nowUnix)
    {
        var sorted = SortOrder switch
        {
            VentureSortOrder.Alphabetical =>
                list.OrderBy(x => x.Entity.Name).ToList(),
            VentureSortOrder.ReverseAlphabetical =>
                list.OrderByDescending(x => x.Entity.Name).ToList(),
            VentureSortOrder.CharacterAlphabetical =>
                list.OrderBy(x => x.Character.Name).ThenBy(x => x.Entity.Name).ToList(),
            VentureSortOrder.CharacterReverseAlphabetical =>
                list.OrderByDescending(x => x.Character.Name).ThenBy(x => x.Entity.Name).ToList(),
            VentureSortOrder.EarliestToComplete =>
                list.OrderBy(x => x.Entity.IsOnVenture ? x.Entity.EndTime : long.MaxValue).ToList(),
            VentureSortOrder.LatestToComplete =>
                list.OrderByDescending(x => x.Entity.IsOnVenture ? x.Entity.EndTime : 0).ToList(),
            _ => list
        };

        return ApplyReadyOrdering(sorted, x => x.Entity.IsOnVenture && x.Entity.EndTime <= nowUnix);
    }

    private List<T> ApplyReadyOrdering<T>(List<T> items, Func<T, bool> isReady)
    {
        var ready = items.Where(isReady).ToList();
        var notReady = items.Where(x => !isReady(x)).ToList();

        return ReadyOnTop
            ? ready.Concat(notReady).ToList()
            : notReady.Concat(ready).ToList();
    }

    public override bool HasSettings => true;
    protected override bool HasToolSettings => true;

    protected override void DrawToolSettings()
    {
        var groupByCharacter = GroupByCharacter;
        if (ImGui.Checkbox("Group by Character", ref groupByCharacter))
        {
            GroupByCharacter = groupByCharacter;
            NotifyToolSettingsChanged();
        }

        if (!GroupByCharacter)
        {
            var hideCharacterName = HideCharacterName;
            if (ImGui.Checkbox("Hide Character Name", ref hideCharacterName))
            {
                HideCharacterName = hideCharacterName;
                NotifyToolSettingsChanged();
            }
        }

        ImGui.Spacing();

        ImGui.TextUnformatted("Sort Order:");
        var sortOrder = (int)SortOrder;
        var sortOrderNames = new[]
        {
            "Alphabetical",
            "Reverse Alphabetical",
            "Character (A-Z)",
            "Character (Z-A)",
            "AutoRetainer Order",
            "Earliest to Complete",
            "Latest to Complete"
        };

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.Combo("##SortOrder", ref sortOrder, sortOrderNames, sortOrderNames.Length))
        {
            SortOrder = (VentureSortOrder)sortOrder;
            NotifyToolSettingsChanged();
        }

        ImGui.TextUnformatted("Ready Position:");
        var readyPosition = ReadyOnTop ? 0 : 1;
        var readyPositionNames = new[] { "Top", "Bottom" };
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.Combo("##ReadyPosition", ref readyPosition, readyPositionNames, readyPositionNames.Length))
        {
            ReadyOnTop = readyPosition == 0;
            NotifyToolSettingsChanged();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawHiddenCharactersSection();
        DrawHiddenEntitiesSection();
        DrawColorsSection();
    }

    private void DrawHiddenCharactersSection()
    {
        if (ImGui.CollapsingHeader("Hidden Characters"))
        {
            ImGui.Indent();

            if (HiddenCharacters.Count == 0)
            {
                ImGui.TextColored(DisabledColor, "No hidden characters");
            }
            else
            {
                if (ImGui.Button("Unhide All##Characters"))
                {
                    HiddenCharacters.Clear();
                    NotifyToolSettingsChanged();
                }
                ImGui.Spacing();

                ulong? characterToUnhide = null;
                foreach (var cid in HiddenCharacters)
                {
                    var characterName = "Unknown";
                    if (Characters != null)
                    {
                        var character = Characters.FirstOrDefault(c => c.CID == cid);
                        if (character != null)
                        {
                            characterName = $"{GetFormattedCharacterName(character)} @ {character.World}";
                        }
                    }

                    ImGui.TextUnformatted(characterName);
                    ImGui.SameLine();
                    ImGui.PushID((int)cid);
                    if (ImGui.SmallButton("Unhide"))
                    {
                        characterToUnhide = cid;
                    }
                    ImGui.PopID();
                }

                if (characterToUnhide.HasValue)
                {
                    HiddenCharacters.Remove(characterToUnhide.Value);
                    NotifyToolSettingsChanged();
                }
            }

            ImGui.Unindent();
        }
    }

    private void DrawHiddenEntitiesSection()
    {
        if (ImGui.CollapsingHeader($"Hidden {EntityNamePlural}"))
        {
            ImGui.Indent();

            if (HiddenEntities.Count == 0)
            {
                ImGui.TextColored(DisabledColor, $"No hidden {EntityNamePlural.ToLowerInvariant()}");
                ImGui.TextColored(DisabledColor, $"Right-click a {EntityNameSingular.ToLowerInvariant()} to hide it.");
            }
            else
            {
                if (ImGui.Button($"Unhide All##{EntityNamePlural}"))
                {
                    HiddenEntities.Clear();
                    NotifyToolSettingsChanged();
                }
                ImGui.Spacing();

                string? entityToUnhide = null;
                foreach (var entityKey in HiddenEntities)
                {
                    var displayName = GetEntityDisplayName(entityKey);

                    ImGui.TextUnformatted(displayName);
                    ImGui.SameLine();
                    ImGui.PushID(entityKey);
                    if (ImGui.SmallButton("Unhide"))
                    {
                        entityToUnhide = entityKey;
                    }
                    ImGui.PopID();
                }

                if (entityToUnhide != null)
                {
                    HiddenEntities.Remove(entityToUnhide);
                    NotifyToolSettingsChanged();
                }
            }

            ImGui.Unindent();
        }
    }

    private void DrawColorsSection()
    {
        if (ImGui.CollapsingHeader("Colors"))
        {
            ImGui.Indent();

            var (readyChanged, newReady) = ImGuiHelpers.ColorPickerWithReset(
                "##ready", ReadyColor, DefaultReadyColor, "Ready");
            if (readyChanged)
            {
                ReadyColor = newReady;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Ready");

            var (activeChanged, newActive) = ImGuiHelpers.ColorPickerWithReset(
                "##active", ActiveColor, DefaultActiveColor, "Active");
            if (activeChanged)
            {
                ActiveColor = newActive;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Active");

            var (disabledChanged, newDisabled) = ImGuiHelpers.ColorPickerWithReset(
                "##disabled", DisabledColor, DefaultDisabledColor, "Disabled/Character");
            if (disabledChanged)
            {
                DisabledColor = newDisabled;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Disabled/Character");

            var (noVentureChanged, newNoVenture) = ImGuiHelpers.ColorPickerWithReset(
                "##noventure", NoVentureColor, DefaultNoVentureColor, $"No {VentureNameSingular}");
            if (noVentureChanged)
            {
                NoVentureColor = newNoVenture;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted($"No {VentureNameSingular}");

            ImGui.Unindent();
        }
    }

    /// <summary>
    /// Exports tool-specific settings for layout persistence.
    /// </summary>
    public override Dictionary<string, object?>? ExportToolSettings()
    {
        return new Dictionary<string, object?>
        {
            ["GroupByCharacter"] = GroupByCharacter,
            ["HideCharacterName"] = HideCharacterName,
            ["SortOrder"] = (int)SortOrder,
            ["ReadyOnTop"] = ReadyOnTop,
            ["HiddenCharacters"] = HiddenCharacters.ToList(),
            [HiddenEntitiesSettingsKey] = HiddenEntities.ToList(),
            ["ReadyColorR"] = ReadyColor.X,
            ["ReadyColorG"] = ReadyColor.Y,
            ["ReadyColorB"] = ReadyColor.Z,
            ["ReadyColorA"] = ReadyColor.W,
            ["ActiveColorR"] = ActiveColor.X,
            ["ActiveColorG"] = ActiveColor.Y,
            ["ActiveColorB"] = ActiveColor.Z,
            ["ActiveColorA"] = ActiveColor.W,
            ["DisabledColorR"] = DisabledColor.X,
            ["DisabledColorG"] = DisabledColor.Y,
            ["DisabledColorB"] = DisabledColor.Z,
            ["DisabledColorA"] = DisabledColor.W,
            [$"{NoVentureColorSettingsKey}R"] = NoVentureColor.X,
            [$"{NoVentureColorSettingsKey}G"] = NoVentureColor.Y,
            [$"{NoVentureColorSettingsKey}B"] = NoVentureColor.Z,
            [$"{NoVentureColorSettingsKey}A"] = NoVentureColor.W
        };
    }

    /// <summary>
    /// Imports tool-specific settings from a layout.
    /// </summary>
    public override void ImportToolSettings(Dictionary<string, object?>? settings)
    {
        if (settings == null) return;

        GroupByCharacter = GetSetting(settings, "GroupByCharacter", GroupByCharacter);
        HideCharacterName = GetSetting(settings, "HideCharacterName", HideCharacterName);
        SortOrder = (VentureSortOrder)GetSetting(settings, "SortOrder", (int)SortOrder);
        ReadyOnTop = GetSetting(settings, "ReadyOnTop", ReadyOnTop);

        var hiddenChars = GetSetting<List<ulong>>(settings, "HiddenCharacters", null);
        if (hiddenChars != null)
        {
            HiddenCharacters = new HashSet<ulong>(hiddenChars);
        }

        var hiddenEntities = GetSetting<List<string>>(settings, HiddenEntitiesSettingsKey, null);
        if (hiddenEntities != null)
        {
            HiddenEntities = new HashSet<string>(hiddenEntities);
        }

        ReadyColor = new Vector4(
            GetSetting(settings, "ReadyColorR", ReadyColor.X),
            GetSetting(settings, "ReadyColorG", ReadyColor.Y),
            GetSetting(settings, "ReadyColorB", ReadyColor.Z),
            GetSetting(settings, "ReadyColorA", ReadyColor.W));

        ActiveColor = new Vector4(
            GetSetting(settings, "ActiveColorR", ActiveColor.X),
            GetSetting(settings, "ActiveColorG", ActiveColor.Y),
            GetSetting(settings, "ActiveColorB", ActiveColor.Z),
            GetSetting(settings, "ActiveColorA", ActiveColor.W));

        DisabledColor = new Vector4(
            GetSetting(settings, "DisabledColorR", DisabledColor.X),
            GetSetting(settings, "DisabledColorG", DisabledColor.Y),
            GetSetting(settings, "DisabledColorB", DisabledColor.Z),
            GetSetting(settings, "DisabledColorA", DisabledColor.W));

        NoVentureColor = new Vector4(
            GetSetting(settings, $"{NoVentureColorSettingsKey}R", NoVentureColor.X),
            GetSetting(settings, $"{NoVentureColorSettingsKey}G", NoVentureColor.Y),
            GetSetting(settings, $"{NoVentureColorSettingsKey}B", NoVentureColor.Z),
            GetSetting(settings, $"{NoVentureColorSettingsKey}A", NoVentureColor.W));
    }

    public override void Dispose()
    {
        // No resources to dispose
    }
}
