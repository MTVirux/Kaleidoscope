using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kaleidoscope.Gui.Common;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Services;
using MTGui.Common;
using MTGui.Table;
using MTGui.Tree;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

public partial class ItemTableWidget
{
    
    /// <inheritdoc/>
    public bool HasSettings => _boundSettings != null;
    
    /// <inheritdoc/>
    public string SettingsName => _settingsName;
    
    /// <inheritdoc/>
    public bool DrawSettings()
    {
        if (_boundSettings == null) return false;
        
        var changed = false;
        var settings = _boundSettings;
        
        // Color mode setting
        var textColorMode = (int)settings.TextColorMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Color Mode", ref textColorMode, "Don't use\0Use preferred item colors\0Use preferred character colors\0"))
        {
            settings.TextColorMode = (TableTextColorMode)textColorMode;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("How to determine cell text colors: use item/currency preferred colors, character preferred colors, or column-specific colors only.");
        }
        
        ImGui.Spacing();
        
        // Number format setting
        if (NumberFormatSettingsUI.Draw($"table_{GetHashCode()}", settings.NumberFormat, "Number Format"))
        {
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("How numbers are formatted in table cells.");
        }
        
        ImGui.Spacing();
        
        // Table options
        var showTotalRow = settings.ShowTotalRow;
        if (ImGui.Checkbox("Show total row", ref showTotalRow))
        {
            settings.ShowTotalRow = showTotalRow;
            changed = true;
        }
        
        var sortable = settings.Sortable;
        if (ImGui.Checkbox("Enable sorting", ref sortable))
        {
            settings.Sortable = sortable;
            changed = true;
        }
        
        // Show hide character column option only in All mode
        if (settings.GroupingMode == TableGroupingMode.All)
        {
            var hideCharColumn = settings.HideCharacterColumnInAllMode;
            if (ImGui.Checkbox("Hide character column", ref hideCharColumn))
            {
                settings.HideCharacterColumnInAllMode = hideCharColumn;
                changed = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Hide the 'All Characters' column when grouping mode is All.");
            }
        }
        
        ImGui.Spacing();
        if (MTTreeHelpers.DrawSection("Column Sizing", true))
        {
            var useFullNameWidth = settings.UseFullNameWidth;
        if (ImGui.Checkbox("Fit character column to name width", ref useFullNameWidth))
        {
            settings.UseFullNameWidth = useFullNameWidth;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("First column width will be the width of the entry name.");
        }
        
        var autoSizeEqualColumns = settings.AutoSizeEqualColumns;
        if (ImGui.Checkbox("Equal width data columns", ref autoSizeEqualColumns))
        {
            settings.AutoSizeEqualColumns = autoSizeEqualColumns;
            changed = true;
        }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Automatically size all data columns to equal widths.\nCharacter column width takes priority.");
            }
            MTTreeHelpers.EndSection();
        }
        
        ImGui.Spacing();
        if (MTTreeHelpers.DrawSection("Alignment"))
        {
            // Data column alignment
            ImGui.TextDisabled("Data Columns");
            
            // Data horizontal alignment
            var hAlign = (int)settings.HorizontalAlignment;
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("Horizontal##data", ref hAlign, "Left\0Center\0Right\0"))
            {
                settings.HorizontalAlignment = (MTTableHorizontalAlignment)hAlign;
                changed = true;
            }
        
            // Data vertical alignment
            var vAlign = (int)settings.VerticalAlignment;
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("Vertical##data", ref vAlign, "Top\0Center\0Bottom\0"))
            {
                settings.VerticalAlignment = (MTTableVerticalAlignment)vAlign;
                changed = true;
            }
            
            ImGui.Spacing();
            ImGui.TextDisabled("Character Column");
            
            // Character column horizontal alignment
            var charHAlign = (int)settings.CharacterColumnHorizontalAlignment;
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("Horizontal##char", ref charHAlign, "Left\0Center\0Right\0"))
            {
                settings.CharacterColumnHorizontalAlignment = (MTTableHorizontalAlignment)charHAlign;
                changed = true;
            }
        
            // Character column vertical alignment
            var charVAlign = (int)settings.CharacterColumnVerticalAlignment;
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("Vertical##char", ref charVAlign, "Top\0Center\0Bottom\0"))
            {
                settings.CharacterColumnVerticalAlignment = (MTTableVerticalAlignment)charVAlign;
                changed = true;
            }
            
            ImGui.Spacing();
            ImGui.TextDisabled("Header Row");
            
            // Header horizontal alignment
            var headerHAlign = (int)settings.HeaderHorizontalAlignment;
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("Horizontal##header", ref headerHAlign, "Left\0Center\0Right\0"))
            {
                settings.HeaderHorizontalAlignment = (MTTableHorizontalAlignment)headerHAlign;
                changed = true;
            }
        
            // Header vertical alignment
            var headerVAlign = (int)settings.HeaderVerticalAlignment;
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("Vertical##header", ref headerVAlign, "Top\0Center\0Bottom\0"))
            {
                settings.HeaderVerticalAlignment = (MTTableVerticalAlignment)headerVAlign;
                changed = true;
            }
            MTTreeHelpers.EndSection();
        }
        
        ImGui.Spacing();
        if (MTTreeHelpers.DrawSection("Character Column"))
        {
            var charWidth = settings.CharacterColumnWidth;
            if (ImGui.SliderFloat("Min Width", ref charWidth, 60f, 200f, "%.0f"))
            {
                settings.CharacterColumnWidth = charWidth;
                changed = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Minimum width of the character column.\nIf 'Fit to name width' is enabled, this is the minimum value.");
            }
        
            // Character column color
            changed |= MTTableHelpers.DrawColorOption("Text Color", settings.CharacterColumnColor, c => settings.CharacterColumnColor = c);
            MTTreeHelpers.EndSection();
        }
        
        ImGui.Spacing();
        if (MTTreeHelpers.DrawSection("Row Colors"))
        {
            // Header color
            changed |= MTTableHelpers.DrawColorOption("Header", settings.HeaderColor, c => settings.HeaderColor = c);
        
            // Even row color
            changed |= MTTableHelpers.DrawColorOption("Even Rows", settings.EvenRowColor, c => settings.EvenRowColor = c);
        
            // Odd row color
            changed |= MTTableHelpers.DrawColorOption("Odd Rows", settings.OddRowColor, c => settings.OddRowColor = c);
            MTTreeHelpers.EndSection();
        }
        
        // Merged rows section
        if (settings.MergedRowGroups.Count > 0)
        {
            ImGui.Spacing();
            
            if (MTTreeHelpers.DrawSection($"Merged Rows ({settings.MergedRowGroups.Count})", false, "MergedRows"))
            {
                ImGui.TextDisabled("Hold SHIFT and click/drag on character names to select, then right-click to merge.");
                ImGui.Spacing();
                
                int? groupToRemove = null;
                for (int i = 0; i < settings.MergedRowGroups.Count; i++)
                {
                    var group = settings.MergedRowGroups[i];
                    ImGui.PushID($"rowgroup_{i}");
                    
                    // Unmerge button
                    if (ImGui.SmallButton("Unmerge"))
                    {
                        groupToRemove = i;
                    }
                    ImGui.SameLine();
                    
                    // Editable name
                    ImGui.SetNextItemWidth(100);
                    var name = group.Name;
                    if (ImGui.InputText("##Name", ref name, 64))
                    {
                        group.Name = name;
                        changed = true;
                    }
                    ImGui.SameLine();
                    
                    // Show which characters are merged
                    var charNames = new List<string>();
                    foreach (var cid in group.CharacterIds)
                    {
                        var charName = _cachedRows?.FirstOrDefault(r => r.CharacterId == cid)?.Name ?? $"CID: {cid}";
                        charNames.Add(charName);
                    }
                    ImGui.TextDisabled($"({string.Join(" + ", charNames)})");
                    
                    // Color option
                    var (colorChanged, newColor) = ImGuiHelpers.ColorPickerWithClear(
                        "Color##MergedRowColor", group.Color, new Vector4(1f, 1f, 1f, 1f), "Merged row color");
                    if (colorChanged)
                    {
                        group.Color = newColor;
                        changed = true;
                    }
                    
                    ImGui.PopID();
                }
                
                // Handle removal after iteration
                if (groupToRemove.HasValue)
                {
                    settings.MergedRowGroups.RemoveAt(groupToRemove.Value);
                    changed = true;
                }
                
                ImGui.Spacing();
                if (ImGui.Button("Unmerge All Rows"))
                {
                    settings.MergedRowGroups.Clear();
                    changed = true;
                }
                MTTreeHelpers.EndSection();
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Hold SHIFT and click/drag on character names to select, then right-click to merge.");
        }
        
        // Hidden characters section
        if (settings.HiddenCharacters.Count > 0)
        {
            ImGui.Spacing();
            
            if (MTTreeHelpers.DrawSection($"Hidden Characters ({settings.HiddenCharacters.Count})", false, "HiddenChars"))
            {
                // Show each hidden character with unhide button
                ulong? characterToUnhide = null;
                foreach (var cid in settings.HiddenCharacters)
                {
                    // Try to find character name from cached data
                    var charName = _cachedRows?.FirstOrDefault(r => r.CharacterId == cid)?.Name ?? $"CID: {cid}";
                    
                    ImGui.PushID((int)cid);
                    if (ImGui.SmallButton("Show"))
                    {
                        characterToUnhide = cid;
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted(charName);
                    ImGui.PopID();
                }
                
                // Handle unhide after iteration
                if (characterToUnhide.HasValue)
                {
                    settings.HiddenCharacters.Remove(characterToUnhide.Value);
                    changed = true;
                }
                
                ImGui.Spacing();
                if (ImGui.Button("Show All Characters"))
                {
                    settings.HiddenCharacters.Clear();
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Unhide all hidden characters");
                }
                MTTreeHelpers.EndSection();
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Right-click a character name to hide them from this table.");
        }
        
        if (changed)
        {
            _onSettingsChanged?.Invoke();
        }
        
        return changed;
    }
    
}