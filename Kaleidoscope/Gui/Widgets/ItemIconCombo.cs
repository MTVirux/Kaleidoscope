using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.Common;
using Lumina.Excel.Sheets;
using Kaleidoscope.Services;
using OtterGui.Classes;
using OtterGui.Extensions;
using OtterGui.Log;
using OtterGui.Raii;
using OtterGui.Widgets;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.Widgets;

/// <summary>
/// Represents an item for display in the combo with cached icon and name.
/// </summary>
public readonly record struct ComboItem(uint Id, string Name, ushort IconId);

/// <summary>
/// A filtered combo box for selecting items with icons and favorites support.
/// Displays items with their game icons and allows starring favorites.
/// </summary>
public sealed class ItemIconCombo : FilterComboCache<ComboItem>
{
    private readonly ITextureProvider _textureProvider;
    private readonly FavoritesService _favoritesService;
    private readonly PriceTrackingService? _priceTrackingService;
    
    // Current state
    private uint _currentItemId;
    private float _innerWidth;

    /// <summary>
    /// The label for this combo (used for ImGui ID).
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the currently selected item.
    /// </summary>
    public ComboItem? SelectedItem => CurrentSelectionIdx >= 0 ? CurrentSelection : null;

    /// <summary>
    /// Gets the currently selected item ID, or 0 if none.
    /// </summary>
    public uint SelectedItemId => CurrentSelectionIdx >= 0 ? CurrentSelection.Id : 0;

    /// <summary>
    /// Event fired when selection changes.
    /// </summary>
    public new event Action<ComboItem?, ComboItem?>? SelectionChanged;

    public ItemIconCombo(
        ITextureProvider textureProvider,
        IDataManager dataManager,
        FavoritesService favoritesService,
        PriceTrackingService? priceTrackingService,
        string label,
        bool marketableOnly = false)
        : base(
            () => BuildItemList(dataManager, favoritesService, priceTrackingService, marketableOnly),
            MouseWheelType.Control,
            new Logger())
    {
        _textureProvider = textureProvider;
        _favoritesService = favoritesService;
        _priceTrackingService = priceTrackingService;
        Label = label;
        SearchByParts = true;
        
        // Subscribe to favorites changes to rebuild list
        _favoritesService.OnFavoritesChanged += OnFavoritesChanged;
    }

    private void OnFavoritesChanged()
    {
        ResetFilter();
    }

    private static IReadOnlyList<ComboItem> BuildItemList(
        IDataManager dataManager,
        FavoritesService favoritesService,
        PriceTrackingService? priceTrackingService,
        bool marketableOnly)
    {
        var items = new List<ComboItem>();
        var favorites = favoritesService.FavoriteItems;
        var marketable = priceTrackingService?.MarketableItems;

        try
        {
            var sheet = dataManager.GetExcelSheet<Item>();
            if (sheet == null) return items;

            foreach (var row in sheet)
            {
                // Skip items with empty names
                var name = row.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Skip non-marketable if filter is enabled
                if (marketableOnly && marketable != null && !marketable.Contains((int)row.RowId))
                    continue;

                items.Add(new ComboItem(row.RowId, name, row.Icon));
            }

            // Sort: favorites first, then alphabetically
            items.Sort((a, b) =>
            {
                var aFav = favorites.Contains(a.Id);
                var bFav = favorites.Contains(b.Id);
                if (aFav != bFav)
                    return bFav.CompareTo(aFav); // Favorites first
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception ex)
        {
            LogService.Debug($"[ItemIconCombo] Error building item list: {ex.Message}");
        }

        return items;
    }

    protected override string ToString(ComboItem obj)
        => obj.Name;

    protected override float GetFilterWidth()
        => _innerWidth - 2 * ImGui.GetStyle().FramePadding.X;

    protected override int UpdateCurrentSelected(int currentSelected)
    {
        if (CurrentSelectionIdx >= 0 && CurrentSelection.Id == _currentItemId)
            return currentSelected;

        CurrentSelectionIdx = Items.IndexOf(i => i.Id == _currentItemId);
        if (CurrentSelectionIdx >= 0)
            CurrentSelection = Items[CurrentSelectionIdx];
        else
            CurrentSelection = default;
            
        return base.UpdateCurrentSelected(CurrentSelectionIdx);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var item = Items[globalIdx];
        var name = ToString(item);
        
        // Draw favorite star (matching Glamourer's pattern)
        if (DrawFavoriteStar(item.Id) && CurrentSelectionIdx == globalIdx)
        {
            // Star was clicked on current selection - clear it
            CurrentSelectionIdx = -1;
            _currentItemId = item.Id;
            CurrentSelection = default;
        }
        
        ImGui.SameLine();
        
        // Draw item icon
        DrawItemIcon(item.IconId);
        
        ImGui.SameLine();
        
        // Draw selectable text
        var ret = ImGui.Selectable(name, selected);
        
        // Draw item ID on right side (dimmed)
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, 0xFF808080))
        {
            RightAlignText($"({item.Id})");
        }
        
        // If Shift is held, keep dropdown open (return false to prevent close)
        if (ret && ImGui.GetIO().KeyShift)
        {
            // Update selection but don't close
            _currentItemId = item.Id;
            CurrentSelectionIdx = globalIdx;
            CurrentSelection = item;
            return false;
        }
        
        return ret;
    }

    protected override void DrawList(float width, float itemHeight)
    {
        base.DrawList(width, itemHeight);
        if (NewSelection != null && Items.Count > NewSelection.Value)
        {
            var newItem = Items[NewSelection.Value];
            if (!Equals(CurrentSelection, newItem))
            {
                SelectionChanged?.Invoke(CurrentSelection, newItem);
            }
            CurrentSelection = newItem;
        }
    }

    private static void RightAlignText(string text)
    {
        var textWidth = ImGui.CalcTextSize(text).X;
        var availWidth = ImGui.GetContentRegionAvail().X;
        if (availWidth > textWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + availWidth - textWidth);
        }
        ImGui.TextUnformatted(text);
    }

    private bool DrawFavoriteStar(uint itemId)
    {
        var isFavorite = _favoritesService.ContainsItem(itemId);
        var starSize = new Vector2(ImGui.GetTextLineHeight());
        var hovering = ImGui.IsMouseHoveringRect(
            ImGui.GetCursorScreenPos(),
            ImGui.GetCursorScreenPos() + starSize);

        var color = hovering ? UiColors.FavoriteStarHovered : isFavorite ? UiColors.FavoriteStarOn : UiColors.FavoriteStarOff;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.TextUnformatted(FontAwesomeIcon.Star.ToIconString());
        }

        if (!ImGui.IsItemClicked())
            return false;

        _favoritesService.ToggleItem(itemId);
        return true;
    }

    private void DrawItemIcon(ushort iconId)
    {
        var icon = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        var size = new Vector2(ImGuiHelpers.IconSize);
        if (icon.TryGetWrap(out var wrap, out _))
        {
            ImGui.Image(wrap.Handle, size);
        }
        else
        {
            // Placeholder if icon not loaded
            ImGui.Dummy(size);
        }
    }

    /// <summary>
    /// Draws the combo at the specified width.
    /// </summary>
    /// <param name="previewName">The preview text to show when closed.</param>
    /// <param name="previewItemId">The current item ID for selection tracking.</param>
    /// <param name="width">The combo width.</param>
    /// <param name="innerWidth">The popup inner width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(string previewName, uint previewItemId, float width, float innerWidth)
    {
        _innerWidth = innerWidth;
        _currentItemId = previewItemId;
        return Draw($"##{Label}", previewName, string.Empty, width, ImGui.GetTextLineHeightWithSpacing());
    }

    /// <summary>
    /// Draws the combo with automatic preview text from current selection.
    /// </summary>
    /// <param name="width">The combo width.</param>
    /// <returns>True if selection changed.</returns>
    public bool Draw(float width)
    {
        var preview = CurrentSelectionIdx >= 0 ? CurrentSelection.Name : "Select item...";
        var itemId = CurrentSelectionIdx >= 0 ? CurrentSelection.Id : 0u;
        return Draw(preview, itemId, width, width);
    }

    /// <summary>
    /// Sets the current selection by item ID.
    /// </summary>
    public void SetSelection(uint itemId)
    {
        _currentItemId = itemId;
        CurrentSelectionIdx = Items.IndexOf(i => i.Id == itemId);
        if (CurrentSelectionIdx >= 0)
            CurrentSelection = Items[CurrentSelectionIdx];
        else
            CurrentSelection = default;
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _currentItemId = 0;
        CurrentSelectionIdx = -1;
        CurrentSelection = default;
    }
}
