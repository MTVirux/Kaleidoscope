using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Services;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Character management category in the config window.
/// Provides options to view and edit character display names and time series colors.
/// </summary>
public sealed class CharactersCategory
{
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly TimeSeriesCacheService _cacheService;
    private readonly AutoRetainerIpcService _autoRetainerService;

    private ulong _editingCharacterId = 0;
    private string _editBuffer = "";
    private bool _needsRefresh = true;
    private List<(ulong cid, string? gameName, string? displayName, uint? timeSeriesColor)> _characters = new();
    
    private ulong _editingColorCid = 0;
    private Vector4 _colorEditBuffer = Vector4.One;

    public CharactersCategory(CurrencyTrackerService currencyTrackerService, TimeSeriesCacheService cacheService, ConfigurationService configService, AutoRetainerIpcService autoRetainerService)
    {
        _currencyTrackerService = currencyTrackerService;
        _cacheService = cacheService;
        _configService = configService;
        _autoRetainerService = autoRetainerService;
    }

    private readonly ConfigurationService _configService;

    public void Draw()
    {
        DrawNameFormatSettings();
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.TextUnformatted("Character Display Names & Colors");
        ImGui.Separator();
        ImGui.TextWrapped("You can set a custom display name and time series color for each character.");
        ImGui.Spacing();

        if (ImGui.Button("Refresh") || _needsRefresh)
        {
            RefreshCharacterList();
            _needsRefresh = false;
        }

        if (!_currencyTrackerService.HasDb)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), "Database not available");
            return;
        }

        ImGui.Spacing();

        if (_characters.Count == 0)
        {
            ImGui.TextColored(UiColors.Info, "No characters found in database.");
            return;
        }

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        var availableHeight = ImGui.GetContentRegionAvail().Y - 50;
        if (availableHeight < 100) availableHeight = 100;

        if (ImGui.BeginTable("CharacterNamesTable", 5, tableFlags, new Vector2(0, availableHeight)))
        {
            ImGui.TableSetupColumn("CID", ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("Game Name", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Display Name", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var (cid, gameName, displayName, timeSeriesColor) in _characters)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var cidStr = cid.ToString();
                if (ImGui.Selectable(cidStr, false, ImGuiSelectableFlags.None, new Vector2(0, 0)))
                {
                    ImGui.SetClipboardText(cidStr);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Content ID (unique identifier)\nClick to copy to clipboard");
                    ImGui.EndTooltip();
                }

                ImGui.TableNextColumn();
                var gameNameDisplay = string.IsNullOrEmpty(gameName) ? "(unknown)" : gameName;
                ImGui.TextUnformatted(gameNameDisplay);

                ImGui.TableNextColumn();
                if (_editingCharacterId == cid)
                {
                    ImGui.SetNextItemWidth(-1);
                    var enterPressed = ImGui.InputText($"##edit_{cid}", ref _editBuffer, 100, ImGuiInputTextFlags.EnterReturnsTrue);
                    
                    if (enterPressed)
                    {
                        SaveDisplayName(cid, _editBuffer);
                    }
                    
                    if (ImGui.IsItemActivated())
                    {
                        ImGui.SetKeyboardFocusHere(-1);
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        ImGui.TextUnformatted(displayName);
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "(using game name)");
                    }
                }

                ImGui.TableNextColumn();
                
                ImGui.PushID((int)cid);
                
                Vector4 colorValue;
                var hasColor = timeSeriesColor.HasValue;
                
                if (_editingColorCid == cid)
                {
                    colorValue = _colorEditBuffer;
                }
                else if (timeSeriesColor.HasValue)
                {
                    colorValue = ColorUtils.UintToVector4(timeSeriesColor.Value);
                }
                else
                {
                    colorValue = new Vector4(0.5f, 0.5f, 0.5f, 1f);
                }
                
                if (!hasColor && _editingColorCid != cid)
                {
                    if (ImGui.ColorButton("##colorPreview", new Vector4(0.3f, 0.3f, 0.3f, 0.5f), 
                        ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20)))
                    {
                        _editingColorCid = cid;
                        _colorEditBuffer = new Vector4(1f, 1f, 1f, 1f);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Click to set a custom color");
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Auto");
                }
                else
                {
                    if (ImGui.ColorEdit4("##color", ref colorValue, 
                        ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar))
                    {
                        _colorEditBuffer = colorValue;
                    }
                    
                    if (ImGui.IsItemActivated() && hasColor)
                    {
                        _editingColorCid = cid;
                        _colorEditBuffer = colorValue;
                    }
                    
                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        SaveTimeSeriesColor(cid, ColorUtils.Vector4ToUint(_colorEditBuffer));
                        _editingColorCid = 0;
                    }
                    
                    if (hasColor || _editingColorCid == cid)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton("X"))
                        {
                            SaveTimeSeriesColor(cid, null);
                            _editingColorCid = 0;
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.TextUnformatted("Clear custom color");
                            ImGui.EndTooltip();
                        }
                    }
                }
                
                ImGui.PopID();

                ImGui.TableNextColumn();
                if (_editingCharacterId == cid)
                {
                    if (ImGui.Button($"Save##{cid}"))
                    {
                        SaveDisplayName(cid, _editBuffer);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"Cancel##{cid}"))
                    {
                        _editingCharacterId = 0;
                        _editBuffer = "";
                    }
                }
                else
                {
                    if (ImGui.Button($"Edit##{cid}"))
                    {
                        _editingCharacterId = cid;
                        _editBuffer = displayName ?? "";
                    }
                    
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        ImGui.SameLine();
                        if (ImGui.Button($"Clear##{cid}"))
                        {
                            SaveDisplayName(cid, null);
                        }
                    }
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        var customCount = _characters.Count(c => !string.IsNullOrEmpty(c.displayName));
        var colorCount = _characters.Count(c => c.timeSeriesColor.HasValue);
        ImGui.TextColored(UiColors.Info, 
            $"{_characters.Count} characters total, {customCount} with custom display names, {colorCount} with custom colors");
    }

    private void RefreshCharacterList()
    {
        if (!_currencyTrackerService.HasDb) return;
        
        try
        {
            _characters = _currencyTrackerService.DbService?.GetAllCharacterDataExtended() ?? new();
            
            var sortOrder = _configService.Config.CharacterSortOrder;
            
            switch (sortOrder)
            {
                case CharacterSortOrder.Alphabetical:
                    _characters.Sort((a, b) =>
                    {
                        var nameA = a.gameName ?? a.cid.ToString();
                        var nameB = b.gameName ?? b.cid.ToString();
                        return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                    
                case CharacterSortOrder.ReverseAlphabetical:
                    _characters.Sort((a, b) =>
                    {
                        var nameA = a.gameName ?? a.cid.ToString();
                        var nameB = b.gameName ?? b.cid.ToString();
                        return string.Compare(nameB, nameA, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                    
                case CharacterSortOrder.AutoRetainer:
                    var arOrder = _autoRetainerService.GetRegisteredCharacterIds();
                    if (arOrder != null && arOrder.Count > 0)
                    {
                        var orderLookup = new Dictionary<ulong, int>();
                        for (var i = 0; i < arOrder.Count; i++)
                        {
                            orderLookup[arOrder[i]] = i;
                        }
                        
                        _characters.Sort((a, b) =>
                        {
                            var hasA = orderLookup.TryGetValue(a.cid, out var orderA);
                            var hasB = orderLookup.TryGetValue(b.cid, out var orderB);
                            
                            if (hasA && hasB)
                                return orderA.CompareTo(orderB);
                            if (hasA)
                                return -1; // A comes first
                            if (hasB)
                                return 1;  // B comes first
                            
                            var nameA = a.gameName ?? a.cid.ToString();
                            var nameB = b.gameName ?? b.cid.ToString();
                            return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                        });
                    }
                    else
                    {
                        _characters.Sort((a, b) =>
                        {
                            var nameA = a.gameName ?? a.cid.ToString();
                            var nameB = b.gameName ?? b.cid.ToString();
                            return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                        });
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.Character, $"[CharactersCategory] Failed to refresh character list: {ex.Message}");
            _characters = new();
        }
    }

    private void SaveDisplayName(ulong cid, string? displayName)
    {
        try
        {
            var trimmed = displayName?.Trim();
            if (string.IsNullOrEmpty(trimmed)) trimmed = null;

            _currencyTrackerService.DbService?.SaveCharacterDisplayName(cid, trimmed);
            _cacheService.SetCharacterDisplayName(cid, trimmed);
            _currencyTrackerService.DbService?.InvalidateCharacterNameCache();
            
            LogService.Debug(LogCategory.Character, $"[CharactersCategory] Saved display name for {cid}: {trimmed ?? "(cleared)"}");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Character, $"Failed to save display name for {cid}", ex);
        }
        finally
        {
            _editingCharacterId = 0;
            _editBuffer = "";
            _needsRefresh = true;
        }
    }

    private void SaveTimeSeriesColor(ulong cid, uint? color)
    {
        try
        {
            _currencyTrackerService.DbService?.SaveCharacterTimeSeriesColor(cid, color);
            _cacheService.SetCharacterTimeSeriesColor(cid, color);
            _currencyTrackerService.DbService?.InvalidateCharacterNameCache();
            
            LogService.Debug(LogCategory.Character, $"[CharactersCategory] Saved time series color for {cid}: {color?.ToString("X8") ?? "(cleared)"}");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Character, $"Failed to save time series color for {cid}", ex);
        }
        finally
        {
            _needsRefresh = true;
        }
    }

    /// <summary>
    /// Draws the name format settings section.
    /// </summary>
    private void DrawNameFormatSettings()
    {
        ImGui.TextUnformatted("Name Display Format");
        ImGui.Separator();
        ImGui.TextWrapped("Choose how character names are displayed throughout the UI. " +
            "\nCustom display names override this setting.");
        ImGui.Spacing();

        var currentFormat = (int)_configService.Config.CharacterNameFormat;
        var formatNames = new[] { "Full Name", "First Name Only", "Last Name Only", "Initials" };
        var formatExamples = new[] { "John Smith", "John", "Smith", "J.S." };

        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Name Format", ref currentFormat, formatNames, formatNames.Length))
        {
            _configService.Config.CharacterNameFormat = (CharacterNameFormat)currentFormat;
            _configService.MarkDirty();
        }

        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"(e.g., \"{formatExamples[currentFormat]}\")");
        
        ImGui.Spacing();
        
        ImGui.TextUnformatted("Character Sort Order");
        ImGui.Separator();
        ImGui.TextWrapped("Choose how characters are sorted in lists throughout the UI.");
        ImGui.Spacing();

        var currentSortOrder = (int)_configService.Config.CharacterSortOrder;
        var sortOrderNames = new[] { "Alphabetical (A-Z)", "Reverse Alphabetical (Z-A)", "AutoRetainer Order" };

        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Sort Order", ref currentSortOrder, sortOrderNames, sortOrderNames.Length))
        {
            _configService.Config.CharacterSortOrder = (CharacterSortOrder)currentSortOrder;
            _configService.MarkDirty();
            _needsRefresh = true; // Refresh to apply new sort order
        }
        
        if (_configService.Config.CharacterSortOrder == CharacterSortOrder.AutoRetainer && !_autoRetainerService.IsAvailable)
        {
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), "AutoRetainer not available - using alphabetical order.");
        }
    }
}
