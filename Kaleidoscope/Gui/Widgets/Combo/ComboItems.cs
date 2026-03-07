using Kaleidoscope.Gui.Common;
using Kaleidoscope.Models;
using Kaleidoscope.Gui.Widgets.Combo;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// Character item for ComboWidget with grouping support.
/// </summary>
public sealed class CharacterItem : IGroupableComboItem<ulong>
{
    public ulong Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? World { get; init; }
    public string? DataCenter { get; init; }
    public string? Region { get; init; }
    
    // IGroupableComboItem implementation
    string? IGroupableComboItem<ulong>.Group => Region;
    string? IGroupableComboItem<ulong>.SubGroup => DataCenter;
    string? IGroupableComboItem<ulong>.TertiaryGroup => World;
    
    /// <summary>
    /// Creates from the legacy ComboCharacter type.
    /// </summary>
    public static CharacterItem FromComboCharacter(ComboCharacter c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        World = c.World,
        DataCenter = c.DataCenter,
        Region = c.Region
    };
}

/// <summary>
/// Game item for ComboWidget.
/// </summary>
public sealed class GameItem : IComboItem<uint>
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ushort IconId { get; init; }
    
    /// <summary>
    /// Creates from the legacy ComboItem type.
    /// </summary>
    public static GameItem FromComboItem(ComboItem c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        IconId = c.IconId
    };
}

/// <summary>
/// Currency item for ComboWidget with category grouping.
/// </summary>
public sealed class CurrencyItem : IGroupableComboItem<TrackedDataType>
{
    public TrackedDataType Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public uint? ItemId { get; init; }
    public uint? IconId { get; init; }
    public TrackedDataCategory Category { get; init; }
    
    // IGroupableComboItem implementation - group by category
    string? IGroupableComboItem<TrackedDataType>.Group => Category.ToString();
    string? IGroupableComboItem<TrackedDataType>.SubGroup => null;
    string? IGroupableComboItem<TrackedDataType>.TertiaryGroup => null;
    
    /// <summary>
    /// Creates from the legacy ComboCurrency type.
    /// </summary>
    public static CurrencyItem FromComboCurrency(ComboCurrency c) => new()
    {
        Id = c.Type,
        Name = c.Name,
        ShortName = c.ShortName,
        ItemId = c.ItemId,
        Category = c.Category
    };
}
