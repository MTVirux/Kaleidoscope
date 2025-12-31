using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using MTGui.Combo;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// Character item for MTComboWidget with grouping support.
/// </summary>
public sealed class MTCharacterItem : IMTGroupableComboItem<ulong>
{
    public ulong Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? World { get; init; }
    public string? DataCenter { get; init; }
    public string? Region { get; init; }
    
    // IMTGroupableComboItem implementation
    string? IMTGroupableComboItem<ulong>.Group => Region;
    string? IMTGroupableComboItem<ulong>.SubGroup => DataCenter;
    string? IMTGroupableComboItem<ulong>.TertiaryGroup => World;
    
    /// <summary>
    /// Creates from the legacy ComboCharacter type.
    /// </summary>
    public static MTCharacterItem FromComboCharacter(ComboCharacter c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        World = c.World,
        DataCenter = c.DataCenter,
        Region = c.Region
    };
}

/// <summary>
/// Game item for MTComboWidget.
/// </summary>
public sealed class MTGameItem : IMTComboItem<uint>
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ushort IconId { get; init; }
    
    /// <summary>
    /// Creates from the legacy ComboItem type.
    /// </summary>
    public static MTGameItem FromComboItem(ComboItem c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        IconId = c.IconId
    };
}

/// <summary>
/// Currency item for MTComboWidget with category grouping.
/// </summary>
public sealed class MTCurrencyItem : IMTGroupableComboItem<TrackedDataType>
{
    public TrackedDataType Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public uint? ItemId { get; init; }
    public TrackedDataCategory Category { get; init; }
    
    // IMTGroupableComboItem implementation - group by category
    string? IMTGroupableComboItem<TrackedDataType>.Group => Category.ToString();
    string? IMTGroupableComboItem<TrackedDataType>.SubGroup => null;
    string? IMTGroupableComboItem<TrackedDataType>.TertiaryGroup => null;
    
    /// <summary>
    /// Creates from the legacy ComboCurrency type.
    /// </summary>
    public static MTCurrencyItem FromComboCurrency(ComboCurrency c) => new()
    {
        Id = c.Type,
        Name = c.Name,
        ShortName = c.ShortName,
        ItemId = c.ItemId,
        Category = c.Category
    };
}
