using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Registry of all trackable data types and their definitions.
/// Provides methods to fetch current values from game state.
/// </summary>
public sealed class TrackedDataRegistry : IRequiredService
{
    private readonly IPluginLog _log;
    private readonly Dictionary<TrackedDataType, TrackedDataDefinition> _definitions = new();

    /// <summary>
    /// Gets all registered data type definitions.
    /// </summary>
    public IReadOnlyDictionary<TrackedDataType, TrackedDataDefinition> Definitions => _definitions;

    public TrackedDataRegistry(IPluginLog log)
    {
        _log = log;
        RegisterAllTypes();
    }

    private void RegisterAllTypes()
    {
        // === Core Currencies ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.Gil,
            DisplayName = "Gil",
            ShortName = "Gil",
            Category = TrackedDataCategory.Currency,
            ItemId = 1,
            MaxValue = 999_999_999,
            EnabledByDefault = true,
            Description = "The primary currency in FFXIV."
        });

        // === Tomestones ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.TomestonePoetics,
            DisplayName = "Allagan Tomestone of Poetics",
            ShortName = "Poetics",
            Category = TrackedDataCategory.Tomestone,
            ItemId = 28,
            MaxValue = 2000,
            EnabledByDefault = true,
            Description = "Uncapped tomestones for older expansion gear."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.TomestoneCapped,
            DisplayName = "Tomestone (Capped)",
            ShortName = "Capped",
            Category = TrackedDataCategory.Tomestone,
            ItemId = 44123, // Heliometry as of 7.x
            MaxValue = 2000,
            EnabledByDefault = true,
            Description = "Weekly-capped tomestones for current expansion gear."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.TomestoneUncapped,
            DisplayName = "Tomestone (Uncapped)",
            ShortName = "Uncapped",
            Category = TrackedDataCategory.Tomestone,
            ItemId = 43693, // Aesthetics as of 7.x
            MaxValue = 2000,
            EnabledByDefault = false,
            Description = "Uncapped tomestones for current expansion."
        });

        // === Scrips ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.WhiteCraftersScrip,
            DisplayName = "White Crafters' Scrip",
            ShortName = "W.Crafter",
            Category = TrackedDataCategory.Scrip,
            ItemId = 25199,
            MaxValue = 4000,
            EnabledByDefault = false,
            Description = "Crafters' scrips for older recipes."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.PurpleCraftersScrip,
            DisplayName = "Purple Crafters' Scrip",
            ShortName = "P.Crafter",
            Category = TrackedDataCategory.Scrip,
            ItemId = 33913,
            MaxValue = 4000,
            EnabledByDefault = false,
            Description = "Crafters' scrips for endgame crafting."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.OrangeCraftersScrip,
            DisplayName = "Orange Crafters' Scrip",
            ShortName = "O.Crafter",
            Category = TrackedDataCategory.Scrip,
            ItemId = 41784,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "Current crafters' scrips."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.WhiteGatherersScrip,
            DisplayName = "White Gatherers' Scrip",
            ShortName = "W.Gatherer",
            Category = TrackedDataCategory.Scrip,
            ItemId = 25200,
            MaxValue = 4000,
            EnabledByDefault = false,
            Description = "Gatherers' scrips for older content."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.PurpleGatherersScrip,
            DisplayName = "Purple Gatherers' Scrip",
            ShortName = "P.Gatherer",
            Category = TrackedDataCategory.Scrip,
            ItemId = 33914,
            MaxValue = 4000,
            EnabledByDefault = false,
            Description = "Gatherers' scrips for endgame gathering."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.OrangeGatherersScrip,
            DisplayName = "Orange Gatherers' Scrip",
            ShortName = "O.Gatherer",
            Category = TrackedDataCategory.Scrip,
            ItemId = 41785,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "Current gatherers' scrips."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.SkybuildersScrip,
            DisplayName = "Skybuilders' Scrip",
            ShortName = "Skybuilder",
            Category = TrackedDataCategory.Scrip,
            ItemId = 28063,
            MaxValue = 99999,
            EnabledByDefault = false,
            Description = "Ishgardian Restoration scrips."
        });

        // === Grand Company Seals ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.MaelstromSeals,
            DisplayName = "Storm Seals (Maelstrom)",
            ShortName = "Storm",
            Category = TrackedDataCategory.GrandCompany,
            MaxValue = 90000,
            EnabledByDefault = false,
            Description = "Maelstrom grand company seals."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.TwinAdderSeals,
            DisplayName = "Serpent Seals (Twin Adder)",
            ShortName = "Serpent",
            Category = TrackedDataCategory.GrandCompany,
            MaxValue = 90000,
            EnabledByDefault = false,
            Description = "Order of the Twin Adder grand company seals."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.ImmortalFlamesSeals,
            DisplayName = "Flame Seals (Immortal Flames)",
            ShortName = "Flame",
            Category = TrackedDataCategory.GrandCompany,
            MaxValue = 90000,
            EnabledByDefault = false,
            Description = "Immortal Flames grand company seals."
        });

        // === PvP Currencies ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.WolfMarks,
            DisplayName = "Wolf Marks",
            ShortName = "Wolf",
            Category = TrackedDataCategory.PvP,
            MaxValue = 20000,
            EnabledByDefault = false,
            Description = "PvP currency for gear and items."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.TrophyCrystals,
            DisplayName = "Trophy Crystals",
            ShortName = "Trophy",
            Category = TrackedDataCategory.PvP,
            ItemId = 36656,
            MaxValue = 20000,
            EnabledByDefault = false,
            Description = "PvP currency for special rewards."
        });

        // === Hunt Currencies ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.AlliedSeals,
            DisplayName = "Allied Seals",
            ShortName = "Allied",
            Category = TrackedDataCategory.Hunt,
            MaxValue = 4000,
            EnabledByDefault = false,
            Description = "ARR/HW hunt currency."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.CenturioSeals,
            DisplayName = "Centurio Seals",
            ShortName = "Centurio",
            Category = TrackedDataCategory.Hunt,
            ItemId = 10307,
            MaxValue = 4000,
            EnabledByDefault = false,
            Description = "Stormblood hunt currency."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.SackOfNuts,
            DisplayName = "Sack of Nuts",
            ShortName = "Nuts",
            Category = TrackedDataCategory.Hunt,
            ItemId = 26533,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "ShB/EW/DT hunt currency."
        });

        // === Gold Saucer ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.MGP,
            DisplayName = "Manderville Gold Saucer Points",
            ShortName = "MGP",
            Category = TrackedDataCategory.GoldSaucer,
            MaxValue = 9_999_999,
            EnabledByDefault = false,
            Description = "Gold Saucer currency."
        });

        // === Tribal ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.BicolorGemstone,
            DisplayName = "Bicolor Gemstones",
            ShortName = "Bicolor",
            Category = TrackedDataCategory.Tribal,
            ItemId = 26807,
            MaxValue = 1000,
            EnabledByDefault = false,
            Description = "FATE currency for ShB/EW zones."
        });

        // === Ventures ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.Ventures,
            DisplayName = "Ventures",
            ShortName = "Venture",
            Category = TrackedDataCategory.Retainer,
            ItemId = 21072,
            MaxValue = 65535,
            EnabledByDefault = true,
            Description = "Retainer venture tokens."
        });

        // === Crystals ===
        // Note: Individual crystal types are kept for backwards compatibility and direct API access,
        // but the CrystalTracker tool provides unified tracking with grouping/filtering.
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.CrystalsTotal,
            DisplayName = "Crystals (Total)",
            ShortName = "Crystals",
            Category = TrackedDataCategory.Crafting,
            MaxValue = 9_999_999,
            EnabledByDefault = false,
            Description = "Total count of all crystals, clusters, and shards (player + retainers). Use Crystal Tracker tool instead."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.FireCrystals,
            DisplayName = "Fire Crystals",
            ShortName = "Fire",
            Category = TrackedDataCategory.Crafting,
            MaxValue = 9_999_999,
            EnabledByDefault = false,
            Description = "Fire shards, crystals, and clusters (player + retainers). Use Crystal Tracker tool instead."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.IceCrystals,
            DisplayName = "Ice Crystals",
            ShortName = "Ice",
            Category = TrackedDataCategory.Crafting,
            MaxValue = 9_999_999,
            EnabledByDefault = false,
            Description = "Ice shards, crystals, and clusters (player + retainers). Use Crystal Tracker tool instead."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.WindCrystals,
            DisplayName = "Wind Crystals",
            ShortName = "Wind",
            Category = TrackedDataCategory.Crafting,
            MaxValue = 9_999_999,
            EnabledByDefault = false,
            Description = "Wind shards, crystals, and clusters (player + retainers). Use Crystal Tracker tool instead."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.EarthCrystals,
            DisplayName = "Earth Crystals",
            ShortName = "Earth",
            Category = TrackedDataCategory.Crafting,
            MaxValue = 9_999_999,
            EnabledByDefault = false,
            Description = "Earth shards, crystals, and clusters (player + retainers). Use Crystal Tracker tool instead."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.LightningCrystals,
            DisplayName = "Lightning Crystals",
            ShortName = "Lightning",
            Category = TrackedDataCategory.Crafting,
            MaxValue = 9_999_999,
            EnabledByDefault = false,
            Description = "Lightning shards, crystals, and clusters (player + retainers). Use Crystal Tracker tool instead."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.WaterCrystals,
            DisplayName = "Water Crystals",
            ShortName = "Water",
            Category = TrackedDataCategory.Crafting,
            MaxValue = 9_999_999,
            EnabledByDefault = false,
            Description = "Water shards, crystals, and clusters (player + retainers). Use Crystal Tracker tool instead."
        });

        // === Inventory Space ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.InventoryFreeSlots,
            DisplayName = "Free Inventory Slots",
            ShortName = "Free Slots",
            Category = TrackedDataCategory.Inventory,
            MaxValue = 140,
            EnabledByDefault = false,
            Description = "Number of empty slots in main inventory."
        });

        // === FC/Retainer ===
        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.FreeCompanyGil,
            DisplayName = "Free Company Gil",
            ShortName = "FC Gil",
            Category = TrackedDataCategory.FreeCompanyRetainer,
            MaxValue = 999_999_999,
            EnabledByDefault = false,
            Description = "Gil held by your Free Company."
        });

        Register(new TrackedDataDefinition
        {
            Type = TrackedDataType.RetainerGil,
            DisplayName = "Retainer Gil",
            ShortName = "Ret Gil",
            Category = TrackedDataCategory.FreeCompanyRetainer,
            MaxValue = 999_999_999,
            EnabledByDefault = false,
            Description = "Gil held by your retainers."
        });
    }

    private void Register(TrackedDataDefinition definition)
    {
        _definitions[definition.Type] = definition;
    }

    /// <summary>
    /// Gets the definition for a specific data type.
    /// </summary>
    public TrackedDataDefinition? GetDefinition(TrackedDataType type)
    {
        return _definitions.TryGetValue(type, out var def) ? def : null;
    }

    /// <summary>
    /// Gets all definitions in a specific category.
    /// </summary>
    public IEnumerable<TrackedDataDefinition> GetByCategory(TrackedDataCategory category)
    {
        return _definitions.Values.Where(d => d.Category == category);
    }

    /// <summary>
    /// Gets the current value for a data type from game state.
    /// For applicable types (Gil, Ventures, Crystals), includes retainer inventory.
    /// </summary>
    public unsafe long? GetCurrentValue(TrackedDataType type)
    {
        try
        {
            var im = GameStateService.InventoryManagerInstance();
            if (im == null) return null;

            return type switch
            {
                // Gil: player + all retainers
                TrackedDataType.Gil => im->GetGil() + GameStateService.GetAllRetainersGil(),
                
                // Tomestones - player only (currency, not tradeable)
                TrackedDataType.TomestonePoetics => im->GetTomestoneCount(28),
                TrackedDataType.TomestoneCapped => im->GetTomestoneCount(44123),
                TrackedDataType.TomestoneUncapped => im->GetTomestoneCount(43693),
                
                // Scrips - player only (currency, not tradeable)
                TrackedDataType.WhiteCraftersScrip => im->GetInventoryItemCount(25199),
                TrackedDataType.PurpleCraftersScrip => im->GetInventoryItemCount(33913),
                TrackedDataType.OrangeCraftersScrip => im->GetInventoryItemCount(41784),
                TrackedDataType.WhiteGatherersScrip => im->GetInventoryItemCount(25200),
                TrackedDataType.PurpleGatherersScrip => im->GetInventoryItemCount(33914),
                TrackedDataType.OrangeGatherersScrip => im->GetInventoryItemCount(41785),
                TrackedDataType.SkybuildersScrip => im->GetInventoryItemCount(28063),
                
                // Grand Company Seals - player only (currency)
                TrackedDataType.MaelstromSeals => im->GetCompanySeals(1),
                TrackedDataType.TwinAdderSeals => im->GetCompanySeals(2),
                TrackedDataType.ImmortalFlamesSeals => im->GetCompanySeals(3),
                
                // PvP - player only (currency)
                TrackedDataType.WolfMarks => im->GetWolfMarks(),
                TrackedDataType.TrophyCrystals => im->GetInventoryItemCount(36656),
                
                // Hunt - player only (currency)
                TrackedDataType.AlliedSeals => im->GetAlliedSeals(),
                TrackedDataType.CenturioSeals => im->GetInventoryItemCount(10307),
                TrackedDataType.SackOfNuts => im->GetInventoryItemCount(26533),
                
                // Gold Saucer - player only (currency)
                TrackedDataType.MGP => im->GetGoldSaucerCoin(),
                
                // Tribal - player only (currency)
                TrackedDataType.BicolorGemstone => im->GetInventoryItemCount(26807),
                
                // Ventures: player + retainers (tradeable item)
                TrackedDataType.Ventures => GetItemCountWithRetainers(im, 21072),
                
                // Crystals: player + retainers
                TrackedDataType.CrystalsTotal => GetTotalCrystalsWithRetainers(im),
                TrackedDataType.FireCrystals => GetElementCrystalsWithRetainers(im, 0),
                TrackedDataType.IceCrystals => GetElementCrystalsWithRetainers(im, 1),
                TrackedDataType.WindCrystals => GetElementCrystalsWithRetainers(im, 2),
                TrackedDataType.EarthCrystals => GetElementCrystalsWithRetainers(im, 3),
                TrackedDataType.LightningCrystals => GetElementCrystalsWithRetainers(im, 4),
                TrackedDataType.WaterCrystals => GetElementCrystalsWithRetainers(im, 5),
                
                // Inventory - player only
                TrackedDataType.InventoryFreeSlots => im->GetEmptySlotsInBag(),
                
                // FC/Retainer - separate tracking for visibility
                TrackedDataType.FreeCompanyGil => im->GetFreeCompanyGil(),
                TrackedDataType.RetainerGil => GameStateService.GetAllRetainersGil(),
                
                _ => null
            };
        }
        catch (Exception ex)
        {
            _log.Debug($"[TrackedDataRegistry] Failed to get value for {type}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets item count from player inventory plus active retainer inventory (if available).
    /// </summary>
    private static unsafe long GetItemCountWithRetainers(FFXIVClientStructs.FFXIV.Client.Game.InventoryManager* im, uint itemId)
    {
        long total = im->GetInventoryItemCount(itemId);
        
        // Add retainer inventory if a retainer is currently active
        if (GameStateService.IsRetainerActive())
        {
            total += GameStateService.GetActiveRetainerItemCount(im, itemId);
        }
        
        return total;
    }

    /// <summary>
    /// Gets total crystals across all types (shards, crystals, clusters) including retainers.
    /// </summary>
    private static unsafe long GetTotalCrystalsWithRetainers(FFXIVClientStructs.FFXIV.Client.Game.InventoryManager* im)
    {
        long total = 0;
        
        // Crystal item IDs: Shards (2-7), Crystals (8-13), Clusters (14-19)
        // Fire=2,8,14  Ice=3,9,15  Wind=4,10,16  Earth=5,11,17  Lightning=6,12,18  Water=7,13,19
        for (uint i = 2; i <= 19; i++)
        {
            try { total += im->GetInventoryItemCount(i); }
            catch { /* ignore */ }
        }
        
        // Add retainer crystals if a retainer is currently active
        if (GameStateService.IsRetainerActive())
        {
            for (uint i = 2; i <= 19; i++)
            {
                try { total += GameStateService.GetActiveRetainerCrystalCount(im, i); }
                catch { /* ignore */ }
            }
        }
        
        return total;
    }

    /// <summary>
    /// Gets crystals for a specific element (0=Fire, 1=Ice, 2=Wind, 3=Earth, 4=Lightning, 5=Water).
    /// Includes player and active retainer crystals.
    /// </summary>
    private static unsafe long GetElementCrystalsWithRetainers(FFXIVClientStructs.FFXIV.Client.Game.InventoryManager* im, int element)
    {
        long total = 0;
        
        // Shard = 2 + element, Crystal = 8 + element, Cluster = 14 + element
        try { total += im->GetInventoryItemCount((uint)(2 + element)); } catch { }
        try { total += im->GetInventoryItemCount((uint)(8 + element)); } catch { }
        try { total += im->GetInventoryItemCount((uint)(14 + element)); } catch { }
        
        // Add retainer crystals if a retainer is currently active
        if (GameStateService.IsRetainerActive())
        {
            try { total += GameStateService.GetActiveRetainerCrystalCount(im, (uint)(2 + element)); } catch { }
            try { total += GameStateService.GetActiveRetainerCrystalCount(im, (uint)(8 + element)); } catch { }
            try { total += GameStateService.GetActiveRetainerCrystalCount(im, (uint)(14 + element)); } catch { }
        }
        
        return total;
    }
}
