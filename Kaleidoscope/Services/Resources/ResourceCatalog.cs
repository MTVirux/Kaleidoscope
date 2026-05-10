using System.Collections.Generic;
using Kaleidoscope.Models.Resources;

namespace Kaleidoscope.Services.Resources;

/// <summary>
/// Static catalog of resource identity helpers — synthetic item IDs for game-memory-only
/// counters, GameInventoryType ↔ Container mapping, and legacy variable-name parsing
/// for the schema 1→2 migration.
/// </summary>
public static class ResourceCatalog
{
    // Synthetic item IDs for values that don't have a real game item ID.
    // Chosen well above any real item ID (game's range is < 100,000) so no collision possible.
    public const uint GilItemId         = 1_000_001;
    public const uint MGPItemId         = 1_000_002;
    public const uint WolfMarksItemId   = 1_000_003;
    public const uint AlliedSealsItemId = 1_000_004;
    public const uint FCCreditsItemId   = 1_000_005;

    /// <summary>
    /// Map a Dalamud GameInventoryType numeric value to our Container enum.
    /// Caller passes the int value (cast from GameInventoryType) so this class
    /// stays free of Dalamud references and remains testable from xUnit.
    /// Numeric values verified against FFXIVClientStructs/FFXIV/Client/Game/InventoryType.cs.
    /// </summary>
    public static bool TryMapContainer(int gameInventoryType, out Container container)
    {
        container = gameInventoryType switch
        {
            0     => Container.Inventory1,
            1     => Container.Inventory2,
            2     => Container.Inventory3,
            3     => Container.Inventory4,
            1000  => Container.EquippedItems,
            2000  => Container.Currency,
            2001  => Container.Crystals,
            2004  => Container.KeyItems,
            3200  => Container.ArmoryOffHand,
            3201  => Container.ArmoryHead,
            3202  => Container.ArmoryBody,
            3203  => Container.ArmoryHands,
            3205  => Container.ArmoryLegs,
            3206  => Container.ArmoryFeets,
            3207  => Container.ArmoryEar,
            3208  => Container.ArmoryNeck,
            3209  => Container.ArmoryWrist,
            3300  => Container.ArmoryRings,
            3400  => Container.ArmorySoulCrystal,
            3500  => Container.ArmoryMainHand,
            4000  => Container.SaddleBag1,
            4001  => Container.SaddleBag2,
            4100  => Container.PremiumSaddleBag1,
            4101  => Container.PremiumSaddleBag2,
            5000  => Container.Cosmopouch1,
            5001  => Container.Cosmopouch2,
            10000 => Container.RetainerPage1,
            10001 => Container.RetainerPage2,
            10002 => Container.RetainerPage3,
            10003 => Container.RetainerPage4,
            10004 => Container.RetainerPage5,
            10005 => Container.RetainerPage6,
            10006 => Container.RetainerPage7,
            11000 => Container.RetainerEquippedItems,
            12001 => Container.RetainerCrystals,
            12002 => Container.RetainerMarket,
            20000 => Container.FreeCompanyPage1,
            20001 => Container.FreeCompanyPage2,
            20002 => Container.FreeCompanyPage3,
            20003 => Container.FreeCompanyPage4,
            20004 => Container.FreeCompanyPage5,
            22000 => Container.FreeCompanyGil,
            22001 => Container.FreeCompanyCrystals,
            _ => (Container)(-1),
        };
        return (int)container != -1;
    }

    public readonly record struct LegacyVariableMapping(
        OwnerKind OwnerKind,
        ulong     OwnerId,
        Container Container,
        uint      ItemId);

    /// <summary>
    /// Parse a legacy series.variable name into a structured mapping. Used by the schema 1→2
    /// migration to convert magic-string time-series rows into resource_history rows.
    /// Returns null if the name is unrecognized — caller is expected to log and skip.
    /// </summary>
    public static LegacyVariableMapping? ParseLegacyVariableName(string variable, ulong characterId)
    {
        if (string.IsNullOrEmpty(variable))
            return null;

        // Pattern: "ItemRetainerX_{rid}_{itemId}" — must be checked BEFORE "ItemRetainer_" prefix
        if (variable.StartsWith("ItemRetainerX_", StringComparison.Ordinal))
        {
            var rest = variable.AsSpan(14);
            var underscore = rest.IndexOf('_');
            if (underscore <= 0 || underscore == rest.Length - 1) return null;
            if (!ulong.TryParse(rest[..underscore], out var rid)) return null;
            if (!uint.TryParse(rest[(underscore + 1)..], out var itemId)) return null;
            return new LegacyVariableMapping(OwnerKind.Retainer, rid, Container.RetainerPage1, itemId);
        }

        // Pattern: "ItemRetainer_{itemId}"
        if (variable.StartsWith("ItemRetainer_", StringComparison.Ordinal))
        {
            var rest = variable.AsSpan(13);
            if (rest.IsEmpty) return null;
            if (!uint.TryParse(rest, out var itemId)) return null;
            return new LegacyVariableMapping(OwnerKind.Player, characterId, Container.RetainerAggregate, itemId);
        }

        // Pattern: "Item_{itemId}"
        if (variable.StartsWith("Item_", StringComparison.Ordinal))
        {
            var rest = variable.AsSpan(5);
            if (rest.IsEmpty) return null;
            if (!uint.TryParse(rest, out var itemId)) return null;
            return new LegacyVariableMapping(OwnerKind.Player, characterId, Container.PlayerAggregate, itemId);
        }

        // TrackedDataType enum-name lookup
        if (TrackedDataLegacyMap.TryGetValue(variable, out var mapping))
        {
            return new LegacyVariableMapping(OwnerKind.Player, characterId, mapping.Container, mapping.ItemId);
        }

        return null;
    }

    /// <summary>
    /// Public accessor for the TrackedDataType → (Container, ItemId) mapping. Used by
    /// TrackedDataRegistry when reading live values from ResourceStore.
    /// </summary>
    public static bool TryGetMappingForTrackedDataType(Kaleidoscope.Models.TrackedDataType type, out (Container Container, uint ItemId) mapping)
    {
        return TrackedDataLegacyMap.TryGetValue(type.ToString(), out mapping);
    }

    /// <summary>
    /// Mapping table for TrackedDataType enum names → (Container, ItemId). Mirrors registrations
    /// in TrackedDataRegistry.RegisterAllTypes. ItemIds match the values used there; Container
    /// is Currency for real-item currencies, SpecialPlayer for game-memory-only counters.
    /// </summary>
    private static readonly Dictionary<string, (Container Container, uint ItemId)> TrackedDataLegacyMap = new(StringComparer.Ordinal)
    {
        // Game-memory-only counters → SpecialPlayer with synthetic IDs
        ["Gil"]                = (Container.SpecialPlayer,      GilItemId),
        ["MGP"]                = (Container.SpecialPlayer,      MGPItemId),
        ["WolfMarks"]          = (Container.SpecialPlayer,      WolfMarksItemId),
        ["AlliedSeals"]        = (Container.SpecialPlayer,      AlliedSealsItemId),
        ["FreeCompanyCredits"] = (Container.SpecialFreeCompany, FCCreditsItemId),

        // Real Currency-container items — itemIds from TrackedDataRegistry registrations
        ["TomestonePoetics"]    = (Container.Currency, 28),
        ["TomestoneCapped"]     = (Container.Currency, 47),
        ["TomestoneUncapped"]   = (Container.Currency, 46),
        ["WhiteCraftersScrip"]  = (Container.Currency, 25199),
        ["PurpleCraftersScrip"] = (Container.Currency, 33913),
        ["OrangeCraftersScrip"] = (Container.Currency, 41784),
        ["WhiteGatherersScrip"] = (Container.Currency, 25200),
        ["PurpleGatherersScrip"]= (Container.Currency, 33914),
        ["OrangeGatherersScrip"]= (Container.Currency, 41785),
        ["SkybuildersScrip"]    = (Container.Currency, 28063),
        ["MaelstromSeals"]      = (Container.Currency, 20),
        ["TwinAdderSeals"]      = (Container.Currency, 21),
        ["ImmortalFlamesSeals"] = (Container.Currency, 22),
        ["TrophyCrystals"]      = (Container.Currency, 36656),
        ["CenturioSeals"]       = (Container.Currency, 10307),
        ["SackOfNuts"]          = (Container.Currency, 26533),
        ["BicolorGemstone"]     = (Container.Currency, 26807),
        ["Ventures"]            = (Container.Currency, 21072),

        // FreeCompanyGil → its own container, gil item id
        ["FreeCompanyGil"]      = (Container.FreeCompanyGil, GilItemId),

        // Crystals — aggregate. Map to PlayerAggregate with the lowest-tier item id of each element.
        ["CrystalsTotal"]      = (Container.PlayerAggregate, 2),
        ["FireCrystals"]       = (Container.PlayerAggregate, 2),
        ["IceCrystals"]        = (Container.PlayerAggregate, 3),
        ["WindCrystals"]       = (Container.PlayerAggregate, 4),
        ["EarthCrystals"]      = (Container.PlayerAggregate, 5),
        ["LightningCrystals"]  = (Container.PlayerAggregate, 6),
        ["WaterCrystals"]      = (Container.PlayerAggregate, 7),

        // RetainerGil → RetainerGil container (12000); scoped to OwnerKind.Retainer in TrackedDataRegistry
        ["RetainerGil"]        = (Container.RetainerGil, GilItemId),

        // Synthetic per-character metrics with reserved synthetic IDs
        ["InventoryFreeSlots"]  = (Container.SpecialPlayer, 1_000_006),
        ["InventoryValueItems"] = (Container.SpecialPlayer, 1_000_007),
    };

    /// <summary>
    /// Lazy reverse index: (itemId, container) → legacy variable name.
    /// Built once on first use from <see cref="TrackedDataLegacyMap"/>.
    /// </summary>
    private static readonly Lazy<Dictionary<(uint ItemId, Container Container), string>> _reverseMap =
        new(() =>
        {
            var d = new Dictionary<(uint, Container), string>();
            foreach (var (name, mapping) in TrackedDataLegacyMap)
                d.TryAdd((mapping.ItemId, mapping.Container), name);
            return d;
        });

    /// <summary>
    /// Reverse-maps a synthetic (itemId ≥ 1_000_000) + container pair back to the legacy
    /// variable name used in time-series operations.  Returns null if not found.
    /// </summary>
    public static string? GetLegacyVariableName(uint itemId, Container container)
    {
        return _reverseMap.Value.TryGetValue((itemId, container), out var name) ? name : null;
    }
}
