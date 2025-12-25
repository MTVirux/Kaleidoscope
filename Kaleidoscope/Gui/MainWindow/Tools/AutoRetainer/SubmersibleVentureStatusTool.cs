using Dalamud.Bindings.ImGui;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// A tool that displays submersible voyage status with precise timers.
/// </summary>
public class SubmersibleVentureStatusTool : ToolComponent
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
    private static readonly Vector4 DefaultNoVoyageColor = new(0.6f, 0.6f, 0.6f, 1f);

    // Configurable colors
    public Vector4 ReadyColor { get; set; } = DefaultReadyColor;
    public Vector4 ActiveColor { get; set; } = DefaultActiveColor;
    public Vector4 DisabledColor { get; set; } = DefaultDisabledColor;
    public Vector4 NoVoyageColor { get; set; } = DefaultNoVoyageColor;

    /// <summary>
    /// Whether to group submersibles by character.
    /// </summary>
    public bool GroupByCharacter { get; set; } = true;

    /// <summary>
    /// Whether to hide character names when not grouping by character.
    /// </summary>
    public bool HideCharacterName { get; set; } = false;

    /// <summary>
    /// The sort order for displaying submersibles.
    /// </summary>
    public VentureSortOrder SortOrder { get; set; } = VentureSortOrder.EarliestToComplete;

    /// <summary>
    /// Set of character IDs that are hidden from the list.
    /// </summary>
    public HashSet<ulong> HiddenCharacters { get; set; } = new();

    /// <summary>
    /// Whether ready submersibles should appear at the top (true) or bottom (false) of the list.
    /// </summary>
    public bool ReadyOnTop { get; set; } = true;

    public SubmersibleVentureStatusTool(AutoRetainerIpcService? autoRetainerIpc = null, ConfigurationService? configService = null)
    {
        _autoRetainerIpc = autoRetainerIpc;
        _configService = configService;

        Title = "Submersible Voyages";
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
            LogService.Debug($"[SubmersibleVentureStatusTool] Draw error: {ex.Message}");
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
            LogService.Debug($"[SubmersibleVentureStatusTool] Refresh error: {ex.Message}");
        }
    }

    private void DrawGroupedByCharacter(long nowUnix, long nowMs)
    {
        var sortedCharacters = GetSortedCharacters();

        foreach (var character in sortedCharacters)
        {
            if (HiddenCharacters.Contains(character.CID))
                continue;

            // Filter to only submersibles
            var submersibles = character.Vessels.Where(v => v.IsSubmersible).ToList();
            if (submersibles.Count == 0)
                continue;

            var readyCount = submersibles.Count(v => v.ReturnTime > 0 && v.ReturnTime <= nowUnix);
            var totalDeployed = submersibles.Count(v => v.ReturnTime > 0);

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

                var sortedSubmersibles = GetSortedVessels(submersibles, character.Name);

                var isEditMode = StateService.IsEditModeStatic;
                var tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.Borders;

                if (ImGui.BeginTable($"VesselTable_{character.CID}", 2, tableFlags))
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);

                    ImGui.TableHeadersRow();

                    foreach (var vessel in sortedSubmersibles)
                    {
                        DrawVesselTableRow(vessel, nowUnix, nowMs);
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
        // Build flat list of all submersibles with character info
        var allVessels = new List<(AutoRetainerVesselData Vessel, AutoRetainerCharacterData Character)>();

        foreach (var character in _characters!)
        {
            if (HiddenCharacters.Contains(character.CID))
                continue;

            foreach (var vessel in character.Vessels.Where(v => v.IsSubmersible))
            {
                allVessels.Add((vessel, character));
            }
        }

        // Sort the flat list
        allVessels = SortFlatVesselList(allVessels);

        var showCharacter = !HideCharacterName;
        var columnCount = showCharacter ? 3 : 2;

        var isEditMode = StateService.IsEditModeStatic;
        var tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY;
        var tableHeight = ImGui.GetContentRegionAvail().Y;

        if (ImGui.BeginTable("VesselFlatTable", columnCount, tableFlags, new Vector2(0, tableHeight)))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            if (showCharacter)
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);

            ImGui.TableHeadersRow();

            foreach (var (vessel, character) in allVessels)
            {
                DrawVesselTableRowWithCharacter(vessel, character, nowUnix, nowMs, showCharacter);
            }

            ImGui.EndTable();
        }
    }

    private void DrawVesselTableRow(AutoRetainerVesselData vessel, long nowUnix, long nowMs)
    {
        ImGui.TableNextRow();
        
        // Name column
        ImGui.TableNextColumn();
        if (vessel.ReturnTime == 0)
        {
            ImGui.TextColored(NoVoyageColor, vessel.Name);
        }
        else
        {
            var isReady = vessel.ReturnTime <= nowUnix;
            var color = isReady ? ReadyColor : ActiveColor;
            ImGui.TextColored(color, vessel.Name);
        }

        // Status column
        ImGui.TableNextColumn();
        if (vessel.ReturnTime == 0)
        {
            ImGui.TextColored(DisabledColor, "No voyage");
        }
        else if (vessel.ReturnTime <= nowUnix)
        {
            ImGui.TextColored(ReadyColor, "Ready!");
        }
        else
        {
            var timeRemaining = FormatTimeRemaining(vessel.ReturnTime, nowUnix, nowMs);
            ImGui.TextColored(ActiveColor, timeRemaining);
        }
    }

    private void DrawVesselTableRowWithCharacter(AutoRetainerVesselData vessel, AutoRetainerCharacterData character, long nowUnix, long nowMs, bool showCharacter)
    {
        ImGui.TableNextRow();
        
        // Name column
        ImGui.TableNextColumn();
        if (vessel.ReturnTime == 0)
        {
            ImGui.TextColored(NoVoyageColor, vessel.Name);
        }
        else
        {
            var isReady = vessel.ReturnTime <= nowUnix;
            var color = isReady ? ReadyColor : ActiveColor;
            ImGui.TextColored(color, vessel.Name);
        }

        // Character column (optional)
        if (showCharacter)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(DisabledColor, GetFormattedCharacterName(character));
        }

        // Status column
        ImGui.TableNextColumn();
        if (vessel.ReturnTime == 0)
        {
            ImGui.TextColored(DisabledColor, "No voyage");
        }
        else if (vessel.ReturnTime <= nowUnix)
        {
            ImGui.TextColored(ReadyColor, "Ready!");
        }
        else
        {
            var timeRemaining = FormatTimeRemaining(vessel.ReturnTime, nowUnix, nowMs);
            ImGui.TextColored(ActiveColor, timeRemaining);
        }
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
                _characters.OrderBy(c => c.Vessels.Where(v => v.IsSubmersible && v.ReturnTime > 0).Select(v => v.ReturnTime).DefaultIfEmpty(long.MaxValue).Min()).ToList(),
            VentureSortOrder.LatestToComplete =>
                _characters.OrderByDescending(c => c.Vessels.Where(v => v.IsSubmersible && v.ReturnTime > 0).Select(v => v.ReturnTime).DefaultIfEmpty(0).Max()).ToList(),
            _ => _characters.ToList() // AutoRetainerOrder - use original order
        };
    }

    private List<AutoRetainerVesselData> GetSortedVessels(List<AutoRetainerVesselData> vessels, string characterName)
    {
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        var sorted = SortOrder switch
        {
            VentureSortOrder.Alphabetical or VentureSortOrder.CharacterAlphabetical =>
                vessels.OrderBy(v => v.Name).ToList(),
            VentureSortOrder.ReverseAlphabetical or VentureSortOrder.CharacterReverseAlphabetical =>
                vessels.OrderByDescending(v => v.Name).ToList(),
            VentureSortOrder.EarliestToComplete =>
                vessels.OrderBy(v => v.ReturnTime > 0 ? v.ReturnTime : long.MaxValue).ToList(),
            VentureSortOrder.LatestToComplete =>
                vessels.OrderByDescending(v => v.ReturnTime).ToList(),
            _ => vessels.ToList() // AutoRetainerOrder
        };

        // Apply ready on top/bottom
        return ApplyReadyOrdering(sorted, v => v.ReturnTime > 0 && v.ReturnTime <= now);
    }

    private List<(AutoRetainerVesselData Vessel, AutoRetainerCharacterData Character)> SortFlatVesselList(
        List<(AutoRetainerVesselData Vessel, AutoRetainerCharacterData Character)> list)
    {
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        var sorted = SortOrder switch
        {
            VentureSortOrder.Alphabetical =>
                list.OrderBy(x => x.Vessel.Name).ToList(),
            VentureSortOrder.ReverseAlphabetical =>
                list.OrderByDescending(x => x.Vessel.Name).ToList(),
            VentureSortOrder.CharacterAlphabetical =>
                list.OrderBy(x => x.Character.Name).ThenBy(x => x.Vessel.Name).ToList(),
            VentureSortOrder.CharacterReverseAlphabetical =>
                list.OrderByDescending(x => x.Character.Name).ThenBy(x => x.Vessel.Name).ToList(),
            VentureSortOrder.EarliestToComplete =>
                list.OrderBy(x => x.Vessel.ReturnTime > 0 ? x.Vessel.ReturnTime : long.MaxValue).ToList(),
            VentureSortOrder.LatestToComplete =>
                list.OrderByDescending(x => x.Vessel.ReturnTime).ToList(),
            _ => list // AutoRetainerOrder
        };

        // Apply ready on top/bottom
        return ApplyReadyOrdering(sorted, x => x.Vessel.ReturnTime > 0 && x.Vessel.ReturnTime <= now);
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
                if (ImGui.Button("Unhide All"))
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

            var noVoyageColor = NoVoyageColor;
            if (ImGui.ColorEdit4("##novoyage", ref noVoyageColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoLabel))
            {
                NoVoyageColor = noVoyageColor;
                NotifyToolSettingsChanged();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("No Voyage");

            ImGui.Spacing();
            if (ImGui.Button("Reset Colors"))
            {
                ReadyColor = DefaultReadyColor;
                ActiveColor = DefaultActiveColor;
                DisabledColor = DefaultDisabledColor;
                NoVoyageColor = DefaultNoVoyageColor;
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
            ["NoVoyageColorR"] = NoVoyageColor.X,
            ["NoVoyageColorG"] = NoVoyageColor.Y,
            ["NoVoyageColorB"] = NoVoyageColor.Z,
            ["NoVoyageColorA"] = NoVoyageColor.W
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
        
        NoVoyageColor = new Vector4(
            GetSetting(settings, "NoVoyageColorR", NoVoyageColor.X),
            GetSetting(settings, "NoVoyageColorG", NoVoyageColor.Y),
            GetSetting(settings, "NoVoyageColorB", NoVoyageColor.Z),
            GetSetting(settings, "NoVoyageColorA", NoVoyageColor.W));
    }

    public override void Dispose()
    {
        // No resources to dispose
    }
}
