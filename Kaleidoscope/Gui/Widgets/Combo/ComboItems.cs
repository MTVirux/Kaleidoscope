using Kaleidoscope.Models;

namespace Kaleidoscope.Gui.Widgets.Combo;

/// <summary>
/// Character item for ComboWidget. Grouping is supplied via the widget's grouping delegates.
/// </summary>
public sealed class CharacterItem : IComboItem<ulong>
{
    public ulong Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? World { get; init; }
    public string? DataCenter { get; init; }
    public string? Region { get; init; }
}

/// <summary>
/// Game item for ComboWidget.
/// </summary>
public sealed class GameItem : IComboItem<uint>
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ushort IconId { get; init; }
}

/// <summary>
/// Currency item for ComboWidget. Grouping is supplied via the widget's grouping delegates.
/// </summary>
public sealed class CurrencyItem : IComboItem<TrackedDataType>
{
    public TrackedDataType Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public uint? ItemId { get; init; }
    public uint? IconId { get; init; }
    public TrackedDataCategory Category { get; init; }
}
