using Kaleidoscope.Models;

namespace Kaleidoscope.Gui.Common;

/// <summary>
/// Readonly record struct representing an item for combo dropdowns.
/// Used by MTItemComboDropdown and related widgets.
/// </summary>
public readonly record struct ComboItem(uint Id, string Name, ushort IconId);

/// <summary>
/// Readonly record struct representing a currency for combo dropdowns.
/// Used by MTCurrencyComboDropdown and related widgets.
/// </summary>
public readonly record struct ComboCurrency(TrackedDataType Type, string Name, string ShortName, uint? ItemId, TrackedDataCategory Category);

/// <summary>
/// Readonly record struct representing a character for combo dropdowns.
/// Used by MTCharacterCombo and related widgets.
/// </summary>
public readonly record struct ComboCharacter(ulong Id, string Name, string? World, string? DataCenter = null, string? Region = null);
