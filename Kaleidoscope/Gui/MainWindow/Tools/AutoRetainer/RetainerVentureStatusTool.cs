using Dalamud.Bindings.ImGui;
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
/// A tool that displays retainer venture status with precise timers.
/// </summary>
public class RetainerVentureStatusTool : ToolComponent
{
    private readonly AutoRetainerIpcService? _autoRetainerIpc;
    private readonly ConfigurationService? _configService;

    // Cached state
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMilliseconds(100); // Fast refresh for ms precision
    private List<AutoRetainerCharacterData>? _characters;

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

    /// <summary>
    /// Whether to group retainers by character.
    /// </summary>
    public bool GroupByCharacter { get; set; } = true;

    /// <summary>
    /// Whether to hide character names when not grouping by character.
    /// </summary>
    public bool HideCharacterName { get; set; } = false;

    /// <summary>
    /// The sort order for displaying retainers.
    /// </summary>
    public VentureSortOrder SortOrder { get; set; } = VentureSortOrder.EarliestToComplete;

    /// <summary>
    /// Set of character IDs that are hidden from the list.
    /// </summary>
    public HashSet<ulong> HiddenCharacters { get; set; } = new();

    /// <summary>
    /// Set of retainer identifiers (CID_RetainerName) that are hidden from the list.
    /// </summary>
    public HashSet<string> HiddenRetainers { get; set; } = new();

    /// <summary>
    /// Whether ready retainers should appear at the top (true) or bottom (false) of the list.
    /// </summary>
    public bool ReadyOnTop { get; set; } = true;

    public RetainerVentureStatusTool(AutoRetainerIpcService? autoRetainerIpc = null, ConfigurationService? configService = null)
    {
        _autoRetainerIpc = autoRetainerIpc;
        _configService = configService;

        Title = "Retainer Ventures";
        Size = new Vector2(300, 400);
    }

    /// <summary>
    /// Gets the formatted character name based on the current name format setting.
    /// </summary>
    private string GetFormattedCharacterName(AutoRetainerCharacterData character)
    {
        var format = _configService?.Config.CharacterNameFormat ?? CharacterNameFormat.FullName;
        return TimeSeriesCacheService.FormatName(character.Name, format) ?? character.Name;
    }

    public override void DrawContent()
    {
        try
        {
            RefreshCachedState();

            if (_autoRetainerIpc == null || !_autoRetainerIpc.IsAvailable)
            {
                ImGui.TextColored(DisabledColor, "AutoRetainer not available");
                return;
            }

            if (_characters == null || _characters.Count == 0)
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
            LogService.Debug($"[RetainerVentureStatusTool] Draw error: {ex.Message}");
        }
    }

    private void RefreshCachedState()
    {
        if (_autoRetainerIpc == null || !_autoRetainerIpc.IsAvailable) return;

        var now = DateTime.Now;
        if (now - _lastRefresh < _refreshInterval) return;

        _lastRefresh = now;

        try
        {
            _characters = _autoRetainerIpc.GetAllFullCharacterData();
        }
        catch (Exception ex)
        {
            LogService.Debug($"[RetainerVentureStatusTool] Refresh error: {ex.Message}");
        }
    }

    private void DrawGroupedByCharacter(long nowUnix, long nowMs)
    {
        var sortedCharacters = GetSortedCharacters();

        foreach (var character in sortedCharacters)
        {
            if (HiddenCharacters.Contains(character.CID))
                continue;

            if (character.Retainers.Count == 0)
                continue;

            var readyCount = character.Retainers.Count(r => r.HasVenture && r.VentureEndsAt <= nowUnix);
            var totalWithVenture = character.Retainers.Count(r => r.HasVenture);

            ImGui.PushID((int)character.CID);

            var headerLabel = $"{GetFormattedCharacterName(character)} @ {character.World}";
            if (ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Right-click to hide
                if (ImGui.BeginPopupContextItem($"CharContext_{character.CID}"))
                {
                    if (ImGui.MenuItem("Hide Character"))
                    {
                        HiddenCharacters.Add(character.CID);
                        NotifyToolSettingsChanged();
                    }
                    ImGui.EndPopup();
                }

                // Filter out hidden retainers
                var visibleRetainers = character.Retainers.Where(r => !IsRetainerHidden(character.CID, r.Name)).ToList();
                var sortedRetainers = GetSortedRetainers(visibleRetainers, character.Name);

                var isEditMode = StateService.IsEditModeStatic;
                var tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.Borders;

                if (ImGui.BeginTable($"RetainerTable_{character.CID}", 2, tableFlags))
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);

                    ImGui.TableHeadersRow();

                    foreach (var retainer in sortedRetainers)
                    {
                        DrawRetainerTableRow(retainer, character.CID, nowUnix, nowMs);
                    }

                    ImGui.EndTable();
                }
            }
            else
            {
                // Right-click to hide (when collapsed)
                if (ImGui.BeginPopupContextItem($"CharContext_{character.CID}"))
                {
                    if (ImGui.MenuItem("Hide Character"))
                    {
                        HiddenCharacters.Add(character.CID);
                        NotifyToolSettingsChanged();
                    }
                    ImGui.EndPopup();
                }
            }

            ImGui.PopID();
        }
    }

    private void DrawFlatList(long nowUnix, long nowMs)
    {
        // Build flat list of all retainers with character info
        var allRetainers = new List<(AutoRetainerRetainerData Retainer, AutoRetainerCharacterData Character)>();

        foreach (var character in _characters!)
        {
            if (HiddenCharacters.Contains(character.CID))
                continue;

            foreach (var retainer in character.Retainers)
            {
                // Skip hidden retainers
                if (IsRetainerHidden(character.CID, retainer.Name))
                    continue;
                    
                allRetainers.Add((retainer, character));
            }
        }

        // Sort the flat list
        allRetainers = SortFlatRetainerList(allRetainers);

        var showCharacter = !HideCharacterName;
        var columnCount = showCharacter ? 3 : 2;

        var isEditMode = StateService.IsEditModeStatic;
        var tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY;
        var tableHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginTable("RetainerFlatTable", columnCount, tableFlags, new Vector2(0, tableHeight)))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            if (showCharacter)
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);

            ImGui.TableHeadersRow();

            foreach (var (retainer, character) in allRetainers)
            {
                DrawRetainerTableRowWithCharacter(retainer, character, nowUnix, nowMs, showCharacter);
            }

            ImGui.EndTable();
        }
    }

    private void DrawRetainerTableRow(AutoRetainerRetainerData retainer, ulong characterCid, long nowUnix, long nowMs)
    {
        ImGui.TableNextRow();
        
        // Name column
        ImGui.TableNextColumn();
        if (!retainer.HasVenture)
        {
            ImGui.TextColored(NoVentureColor, retainer.Name);
        }
        else
        {
            var isReady = retainer.VentureEndsAt <= nowUnix;
            var color = isReady ? ReadyColor : ActiveColor;
            ImGui.TextColored(color, retainer.Name);
        }
        
        // Right-click to hide retainer
        if (ImGui.BeginPopupContextItem($"RetainerContext_{characterCid}_{retainer.Name}"))
        {
            if (ImGui.MenuItem("Hide Retainer"))
            {
                HiddenRetainers.Add(GetRetainerKey(characterCid, retainer.Name));
                NotifyToolSettingsChanged();
            }
            ImGui.EndPopup();
        }

        // Status column
        ImGui.TableNextColumn();
        if (!retainer.HasVenture)
        {
            ImGui.TextColored(DisabledColor, "No venture");
        }
        else if (retainer.VentureEndsAt <= nowUnix)
        {
            ImGui.TextColored(ReadyColor, "Ready!");
        }
        else
        {
            var timeRemaining = FormatTimeRemaining(retainer.VentureEndsAt, nowUnix, nowMs);
            ImGui.TextColored(ActiveColor, timeRemaining);
        }
    }

    private void DrawRetainerTableRowWithCharacter(AutoRetainerRetainerData retainer, AutoRetainerCharacterData character, long nowUnix, long nowMs, bool showCharacter)
    {
        ImGui.TableNextRow();
        
        // Name column
        ImGui.TableNextColumn();
        if (!retainer.HasVenture)
        {
            ImGui.TextColored(NoVentureColor, retainer.Name);
        }
        else
        {
            var isReady = retainer.VentureEndsAt <= nowUnix;
            var color = isReady ? ReadyColor : ActiveColor;
            ImGui.TextColored(color, retainer.Name);
        }

        // Right-click to hide retainer
        if (ImGui.BeginPopupContextItem($"RetainerContext_{character.CID}_{retainer.Name}"))
        {
            if (ImGui.MenuItem("Hide Retainer"))
            {
                HiddenRetainers.Add(GetRetainerKey(character.CID, retainer.Name));
                NotifyToolSettingsChanged();
            }
            ImGui.EndPopup();
        }

        // Character column (optional)
        if (showCharacter)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(DisabledColor, GetFormattedCharacterName(character));
        }

        // Status column
        ImGui.TableNextColumn();
        if (!retainer.HasVenture)
        {
            ImGui.TextColored(DisabledColor, "No venture");
        }
        else if (retainer.VentureEndsAt <= nowUnix)
        {
            ImGui.TextColored(ReadyColor, "Ready!");
        }
        else
        {
            var timeRemaining = FormatTimeRemaining(retainer.VentureEndsAt, nowUnix, nowMs);
            ImGui.TextColored(ActiveColor, timeRemaining);
        }
    }

    /// <summary>
    /// Gets a unique key for a retainer (CID_RetainerName).
    /// </summary>
    private static string GetRetainerKey(ulong characterCid, string retainerName) => $"{characterCid}_{retainerName}";

    /// <summary>
    /// Checks if a retainer is hidden.
    /// </summary>
    private bool IsRetainerHidden(ulong characterCid, string retainerName) => HiddenRetainers.Contains(GetRetainerKey(characterCid, retainerName));

    /// <summary>
    /// Gets a display name for a hidden retainer key.
    /// </summary>
    private string GetRetainerDisplayName(string retainerKey)
    {
        var parts = retainerKey.Split('_', 2);
        if (parts.Length != 2 || !ulong.TryParse(parts[0], out var cid))
            return retainerKey;

        var retainerName = parts[1];
        var characterName = "Unknown";
        
        if (_characters != null)
        {
            var character = _characters.FirstOrDefault(c => c.CID == cid);
            if (character != null)
            {
                characterName = GetFormattedCharacterName(character);
            }
        }

        return $"{retainerName} ({characterName})";
    }

    private string FormatTimeRemaining(long endTimeUnix, long nowUnix, long nowMs)
    {
        var endTimeMs = endTimeUnix * 1000;
        var remainingMs = endTimeMs - nowMs;

        if (remainingMs <= 0)
            return "Ready!";

        var span = TimeSpan.FromMilliseconds(remainingMs);

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}.{span.Milliseconds:D3}";
        }
        else if (span.TotalMinutes >= 1)
        {
            return $"{span.Minutes}:{span.Seconds:D2}.{span.Milliseconds:D3}";
        }
        else
        {
            return $"{span.Seconds}.{span.Milliseconds:D3}";
        }
    }

    private List<AutoRetainerCharacterData> GetSortedCharacters()
    {
        if (_characters == null) return new List<AutoRetainerCharacterData>();

        return SortOrder switch
        {
            VentureSortOrder.CharacterAlphabetical or VentureSortOrder.Alphabetical =>
                _characters.OrderBy(c => c.Name).ToList(),
            VentureSortOrder.CharacterReverseAlphabetical or VentureSortOrder.ReverseAlphabetical =>
                _characters.OrderByDescending(c => c.Name).ToList(),
            VentureSortOrder.EarliestToComplete =>
                _characters.OrderBy(c => c.Retainers.Where(r => r.HasVenture).Select(r => r.VentureEndsAt).DefaultIfEmpty(long.MaxValue).Min()).ToList(),
            VentureSortOrder.LatestToComplete =>
                _characters.OrderByDescending(c => c.Retainers.Where(r => r.HasVenture).Select(r => r.VentureEndsAt).DefaultIfEmpty(0).Max()).ToList(),
            _ => _characters.ToList() // AutoRetainerOrder - use original order
        };
    }

    private List<AutoRetainerRetainerData> GetSortedRetainers(List<AutoRetainerRetainerData> retainers, string characterName)
    {
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        var sorted = SortOrder switch
        {
            VentureSortOrder.Alphabetical or VentureSortOrder.CharacterAlphabetical =>
                retainers.OrderBy(r => r.Name).ToList(),
            VentureSortOrder.ReverseAlphabetical or VentureSortOrder.CharacterReverseAlphabetical =>
                retainers.OrderByDescending(r => r.Name).ToList(),
            VentureSortOrder.EarliestToComplete =>
                retainers.OrderBy(r => r.HasVenture ? r.VentureEndsAt : long.MaxValue).ToList(),
            VentureSortOrder.LatestToComplete =>
                retainers.OrderByDescending(r => r.HasVenture ? r.VentureEndsAt : 0).ToList(),
            _ => retainers.ToList() // AutoRetainerOrder
        };

        // Apply ready on top/bottom
        return ApplyReadyOrdering(sorted, r => r.HasVenture && r.VentureEndsAt <= now);
    }

    private List<(AutoRetainerRetainerData Retainer, AutoRetainerCharacterData Character)> SortFlatRetainerList(
        List<(AutoRetainerRetainerData Retainer, AutoRetainerCharacterData Character)> list)
    {
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        var sorted = SortOrder switch
        {
            VentureSortOrder.Alphabetical =>
                list.OrderBy(x => x.Retainer.Name).ToList(),
            VentureSortOrder.ReverseAlphabetical =>
                list.OrderByDescending(x => x.Retainer.Name).ToList(),
            VentureSortOrder.CharacterAlphabetical =>
                list.OrderBy(x => x.Character.Name).ThenBy(x => x.Retainer.Name).ToList(),
            VentureSortOrder.CharacterReverseAlphabetical =>
                list.OrderByDescending(x => x.Character.Name).ThenBy(x => x.Retainer.Name).ToList(),
            VentureSortOrder.EarliestToComplete =>
                list.OrderBy(x => x.Retainer.HasVenture ? x.Retainer.VentureEndsAt : long.MaxValue).ToList(),
            VentureSortOrder.LatestToComplete =>
                list.OrderByDescending(x => x.Retainer.HasVenture ? x.Retainer.VentureEndsAt : 0).ToList(),
            _ => list // AutoRetainerOrder
        };

        // Apply ready on top/bottom
        return ApplyReadyOrdering(sorted, x => x.Retainer.HasVenture && x.Retainer.VentureEndsAt <= now);
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

        // Only show hide character name option when not grouping
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

        // Sort order combo
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

        // Ready position option
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

        // Hidden characters management
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
                    if (_characters != null)
                    {
                        var character = _characters.FirstOrDefault(c => c.CID == cid);
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

        // Hidden retainers management
        if (ImGui.CollapsingHeader("Hidden Retainers"))
        {
            ImGui.Indent();

            if (HiddenRetainers.Count == 0)
            {
                ImGui.TextColored(DisabledColor, "No hidden retainers");
                ImGui.TextColored(DisabledColor, "Right-click a retainer to hide it.");
            }
            else
            {
                if (ImGui.Button("Unhide All##Retainers"))
                {
                    HiddenRetainers.Clear();
                    NotifyToolSettingsChanged();
                }
                ImGui.Spacing();

                string? retainerToUnhide = null;
                foreach (var retainerKey in HiddenRetainers)
                {
                    var displayName = GetRetainerDisplayName(retainerKey);

                    ImGui.TextUnformatted(displayName);
                    ImGui.SameLine();
                    ImGui.PushID(retainerKey);
                    if (ImGui.SmallButton("Unhide"))
                    {
                        retainerToUnhide = retainerKey;
                    }
                    ImGui.PopID();
                }

                if (retainerToUnhide != null)
                {
                    HiddenRetainers.Remove(retainerToUnhide);
                    NotifyToolSettingsChanged();
                }
            }

            ImGui.Unindent();
        }

        // Colors settings
        if (ImGui.CollapsingHeader("Colors"))
        {
            ImGui.Indent();

            var readyColor = ReadyColor;
            if (ImGui.ColorEdit4("##ready", ref readyColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoLabel))
            {
                ReadyColor = readyColor;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Ready");

            var activeColor = ActiveColor;
            if (ImGui.ColorEdit4("##active", ref activeColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoLabel))
            {
                ActiveColor = activeColor;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Active");

            var disabledColor = DisabledColor;
            if (ImGui.ColorEdit4("##disabled", ref disabledColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoLabel))
            {
                DisabledColor = disabledColor;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("Disabled/Character");

            var noVentureColor = NoVentureColor;
            if (ImGui.ColorEdit4("##noventure", ref noVentureColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoLabel))
            {
                NoVentureColor = noVentureColor;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("No Venture");

            ImGui.Spacing();
            if (ImGui.Button("Reset Colors"))
            {
                ReadyColor = DefaultReadyColor;
                ActiveColor = DefaultActiveColor;
                DisabledColor = DefaultDisabledColor;
                NoVentureColor = DefaultNoVentureColor;
                NotifyToolSettingsChanged();
            }

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
            ["HiddenRetainers"] = HiddenRetainers.ToList(),
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
            ["NoVentureColorR"] = NoVentureColor.X,
            ["NoVentureColorG"] = NoVentureColor.Y,
            ["NoVentureColorB"] = NoVentureColor.Z,
            ["NoVentureColorA"] = NoVentureColor.W
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
        
        var hiddenRetainers = GetSetting<List<string>>(settings, "HiddenRetainers", null);
        if (hiddenRetainers != null)
        {
            HiddenRetainers = new HashSet<string>(hiddenRetainers);
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
            GetSetting(settings, "NoVentureColorR", NoVentureColor.X),
            GetSetting(settings, "NoVentureColorG", NoVentureColor.Y),
            GetSetting(settings, "NoVentureColorB", NoVentureColor.Z),
            GetSetting(settings, "NoVentureColorA", NoVentureColor.W));
    }

    public override void Dispose()
    {
        // No resources to dispose
    }
}
