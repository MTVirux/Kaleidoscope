using Kaleidoscope.Models;

namespace Kaleidoscope.Gui.MainWindow.Tools.Data;

/// <summary>
/// Column dispatch half of the aggregator. Kept apart from SumPerCharacter because
/// SpecialGroupingHelper references Dalamud-bound UI types, while SumPerCharacter is
/// compiled into the Dalamud-free test project.
/// </summary>
public static partial class CrystalColumnAggregator
{
    private const uint VentureItemId = 21072;

    public static bool UsesInventoryAggregation(TrackedDataType type) => TryGetItemIds(type, out _);

    public static bool TryGetItemIds(TrackedDataType type, out HashSet<uint> itemIds)
    {
        switch (type)
        {
            case TrackedDataType.FireCrystals:
                itemIds = ElementIds(CrystalElement.Fire);
                return true;
            case TrackedDataType.IceCrystals:
                itemIds = ElementIds(CrystalElement.Ice);
                return true;
            case TrackedDataType.WindCrystals:
                itemIds = ElementIds(CrystalElement.Wind);
                return true;
            case TrackedDataType.EarthCrystals:
                itemIds = ElementIds(CrystalElement.Earth);
                return true;
            case TrackedDataType.LightningCrystals:
                itemIds = ElementIds(CrystalElement.Lightning);
                return true;
            case TrackedDataType.WaterCrystals:
                itemIds = ElementIds(CrystalElement.Water);
                return true;
            case TrackedDataType.CrystalsTotal:
                itemIds = SpecialGroupingHelper.GetAllCrystalItemIds();
                return true;
            case TrackedDataType.Ventures:
                itemIds = new HashSet<uint> { VentureItemId };
                return true;
            default:
                itemIds = new HashSet<uint>();
                return false;
        }
    }

    private static HashSet<uint> ElementIds(CrystalElement element)
        => new(SpecialGroupingHelper.GetCrystalItemIdsForElement(element));
}
