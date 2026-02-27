using Dalamud.Plugin.Services;
using Kaleidoscope.Models;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Registry of all trackable data types and their definitions.
/// Provides methods to fetch current values from game state.
/// Includes cached lookups for performance optimization.
/// </summary>
public sealed class TrackedDataRegistry : IRequiredService
{
    private readonly IPluginLog _log;
    private readonly Dictionary<TrackedDataType, TrackedDataDefinition> _definitions = new();
    
    private Dictionary<TrackedDataCategory, List<TrackedDataDefinition>>? _byCategory;
    private Dictionary<uint, TrackedDataDefinition>? _byItemId;
    private List<TrackedDataType>? _allTypes;
    private List<TrackedDataDefinition>? _enabledByDefaultList;

    /// <summary>
    /// Gets all registered data type definitions.
    /// </summary>
    public IReadOnlyDictionary<TrackedDataType, TrackedDataDefinition> Definitions => _definitions;

    public TrackedDataRegistry(IPluginLog log)
    {
        _log = log;
        RegisterAllTypes();
        BuildCaches();
    }
    
    /// <summary>
    /// Builds lookup caches after all types are registered.
    /// </summary>
    private void BuildCaches()
    {
        _byCategory = new Dictionary<TrackedDataCategory, List<TrackedDataDefinition>>();
        foreach (var def in _definitions.Values)
        {
            if (!_byCategory.TryGetValue(def.Category, out var list))
            {
                list = new List<TrackedDataDefinition>();
                _byCategory[def.Category] = list;
            }
            list.Add(def);
        }
        
        _byItemId = new Dictionary<uint, TrackedDataDefinition>();
        foreach (var def in _definitions.Values)
        {
            if (def.ItemId.HasValue && def.ItemId.Value > 0)
            {
                _byItemId[def.ItemId.Value] = def;
            }
        }
        
        _allTypes = _definitions.Keys.ToList();
        
        _enabledByDefaultList = _definitions.Values.Where(d => d.EnabledByDefault).ToList();
        
        LogService.Debug(LogCategory.GameState, $"[TrackedDataRegistry] Built caches: {_definitions.Count} definitions, {_byCategory.Count} categories, {_byItemId.Count} by ItemId");
    }

    private void RegisterAllTypes()
    {
        // === Core Currencies ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.Gil,
            DisplayName = "Character Gil",
            ShortName = "Char Gil",
            Category = TrackedDataCategory.Gil,
            ItemId = 1,
            MaxValue = 999_999_999,
            EnabledByDefault = true,
            Description = "The primary currency in FFXIV."
        });

        // === Tomestones ===
        RegisterTrackedType(new TrackedDataDefinition
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

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.TomestoneCapped,
            DisplayName = "Tomestone (Capped)",
            ShortName = "Capped",
            Category = TrackedDataCategory.Tomestone,
            ItemId = 47, // Heliometry as of 7.x
            MaxValue = 2000,
            EnabledByDefault = true,
            Description = "Weekly-capped tomestones for current expansion gear."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.TomestoneUncapped,
            DisplayName = "Tomestone (Uncapped)",
            ShortName = "Uncapped",
            Category = TrackedDataCategory.Tomestone,
            ItemId = 46, // Aesthetics as of 7.x
            MaxValue = 2000,
            EnabledByDefault = true,
            Description = "Uncapped tomestones for current expansion."
        });

        // === Scrips ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.WhiteCraftersScrip,
            DisplayName = "White Crafters' Scrip",
            ShortName = "W.Crafter",
            Category = TrackedDataCategory.Scrip,
            ItemId = 25199,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "Crafters' scrips for older recipes."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.PurpleCraftersScrip,
            DisplayName = "Purple Crafters' Scrip",
            ShortName = "P.Crafter",
            Category = TrackedDataCategory.Scrip,
            ItemId = 33913,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "Crafters' scrips for endgame crafting."
        });

        RegisterTrackedType(new TrackedDataDefinition
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

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.WhiteGatherersScrip,
            DisplayName = "White Gatherers' Scrip",
            ShortName = "W.Gatherer",
            Category = TrackedDataCategory.Scrip,
            ItemId = 25200,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "Gatherers' scrips for older content."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.PurpleGatherersScrip,
            DisplayName = "Purple Gatherers' Scrip",
            ShortName = "P.Gatherer",
            Category = TrackedDataCategory.Scrip,
            ItemId = 33914,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "Gatherers' scrips for endgame gathering."
        });

        RegisterTrackedType(new TrackedDataDefinition
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

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.SkybuildersScrip,
            DisplayName = "Skybuilders' Scrip",
            ShortName = "Skybuilder",
            Category = TrackedDataCategory.Scrip,
            ItemId = 28063,
            MaxValue = 99999,
            EnabledByDefault = true,
            Description = "Ishgardian Restoration scrips."
        });

        // === Grand Company Seals ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.MaelstromSeals,
            DisplayName = "Storm Seals (Maelstrom)",
            ShortName = "Storm",
            Category = TrackedDataCategory.GrandCompany,
            ItemId = 20,
            MaxValue = 90000,
            EnabledByDefault = true,
            Description = "Maelstrom grand company seals."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.TwinAdderSeals,
            DisplayName = "Serpent Seals (Twin Adder)",
            ShortName = "Serpent",
            Category = TrackedDataCategory.GrandCompany,
            ItemId = 21,
            MaxValue = 90000,
            EnabledByDefault = true,
            Description = "Order of the Twin Adder grand company seals."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.ImmortalFlamesSeals,
            DisplayName = "Flame Seals (Immortal Flames)",
            ShortName = "Flame",
            Category = TrackedDataCategory.GrandCompany,
            ItemId = 22,
            MaxValue = 90000,
            EnabledByDefault = true,
            Description = "Immortal Flames grand company seals."
        });

        // === PvP Currencies ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.WolfMarks,
            DisplayName = "Wolf Marks",
            ShortName = "Wolf",
            Category = TrackedDataCategory.PvP,
            ItemId = 25,
            MaxValue = 20000,
            EnabledByDefault = true,
            Description = "PvP currency for gear and items."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.TrophyCrystals,
            DisplayName = "Trophy Crystals",
            ShortName = "Trophy",
            Category = TrackedDataCategory.PvP,
            ItemId = 36656,
            MaxValue = 20000,
            EnabledByDefault = true,
            Description = "PvP currency for special rewards."
        });

        // === Hunt Currencies ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.AlliedSeals,
            DisplayName = "Allied Seals",
            ShortName = "Allied",
            Category = TrackedDataCategory.Hunt,
            ItemId = 27,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "ARR/HW hunt currency."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.CenturioSeals,
            DisplayName = "Centurio Seals",
            ShortName = "Centurio",
            Category = TrackedDataCategory.Hunt,
            ItemId = 10307,
            MaxValue = 4000,
            EnabledByDefault = true,
            Description = "Stormblood hunt currency."
        });

        RegisterTrackedType(new TrackedDataDefinition
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
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.MGP,
            DisplayName = "Manderville Gold Saucer Points",
            ShortName = "MGP",
            Category = TrackedDataCategory.GoldSaucer,
            ItemId = 29,
            MaxValue = 9_999_999,
            EnabledByDefault = true,
            Description = "Gold Saucer currency."
        });

        // === Tribal ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.BicolorGemstone,
            DisplayName = "Bicolor Gemstones",
            ShortName = "Bicolor",
            Category = TrackedDataCategory.Tribal,
            ItemId = 26807,
            MaxValue = 1000,
            EnabledByDefault = true,
            Description = "FATE currency for ShB/EW zones."
        });

        // === Ventures ===
        RegisterTrackedType(new TrackedDataDefinition
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

        // === FC/Retainer ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.FreeCompanyGil,
            DisplayName = "Free Company Gil",
            ShortName = "FC Gil",
            Category = TrackedDataCategory.Gil,
            ItemId = 1, // Gil icon
            MaxValue = 999_999_999,
            EnabledByDefault = false,
            Description = "Gil held by your Free Company."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.RetainerGil,
            DisplayName = "Retainer Gil",
            ShortName = "Ret Gil",
            Category = TrackedDataCategory.Gil,
            ItemId = 1, // Gil icon
            MaxValue = 999_999_999,
            EnabledByDefault = false,
            Description = "Gil held by your retainers."
        });

        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.FreeCompanyCredits,
            DisplayName = "Free Company Credits",
            ShortName = "FC Credits",
            Category = TrackedDataCategory.GrandCompany,
            IconId = 10155, // Use IconId to avoid blacklisting Ceruleum Tank (item 10155) from item combos
            MaxValue = 999_999_999,
            EnabledByDefault = false,
            Description = "Free Company credits earned from FC activities, used to purchase FC actions."
        });

        // === Inventory Space (last) ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.InventoryFreeSlots,
            DisplayName = "Free Inventory Slots",
            ShortName = "Free Slots",
            Category = TrackedDataCategory.Inventory,
            MaxValue = 140,
            EnabledByDefault = false,
            Description = "Number of empty slots in main inventory."
        });

        // === Universalis / Inventory Value (Calculated from market prices) ===
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.InventoryValueItems,
            DisplayName = "Inventory Value (in Gil)",
            ShortName = "Inv Value",
            Category = TrackedDataCategory.Universalis,
            ItemId = 1, // Gil icon
            MaxValue = 999_999_999_999, // Items can be worth a lot
            EnabledByDefault = true,
            Description = "Market value of inventory items via Universalis. Updates when prices change.",
            IsCalculated = true
        });
    }

    private void RegisterTrackedType(TrackedDataDefinition definition)
    {
        _definitions[definition.Type] = definition;
    }

    public TrackedDataDefinition? GetDefinition(TrackedDataType type)
    {
        return _definitions.TryGetValue(type, out var def) ? def : null;
    }

    public IReadOnlyList<TrackedDataDefinition> GetByCategory(TrackedDataCategory category)
    {
        if (_byCategory != null && _byCategory.TryGetValue(category, out var list))
            return list;
        
        // Fallback if cache not built (shouldn't happen)
        return _definitions.Values.Where(d => d.Category == category).ToList();
    }
    
    public TrackedDataDefinition? GetByItemId(uint itemId)
    {
        if (_byItemId != null && _byItemId.TryGetValue(itemId, out var def))
            return def;
        
        // Fallback if cache not built
        return _definitions.Values.FirstOrDefault(d => d.ItemId == itemId);
    }
    
    public IReadOnlyList<TrackedDataType> AllTypes => _allTypes ?? _definitions.Keys.ToList();
    
    public IReadOnlyList<TrackedDataDefinition> EnabledByDefault => _enabledByDefaultList ?? _definitions.Values.Where(d => d.EnabledByDefault).ToList();
    
    public int Count => _definitions.Count;
    
    public int CategoryCount => _byCategory?.Count ?? 0;

    /// <summary>
    /// Gets the current value for a data type from game state.
    /// For applicable types (Gil, Ventures, Crystals), includes retainer inventory.
    /// NOTE: For bulk value retrieval, prefer GetCurrentValuesSnapshot() to avoid redundant retainer lookups.
    /// </summary>
    public unsafe long? GetCurrentValue(TrackedDataType type)
    {
        try
        {
            var im = GameStateService.InventoryManagerInstance();
            if (im == null) return null;

            // Build a fresh cache and delegate to the shared implementation
            var retainerGil = GameStateService.GetAllRetainersGil();
            var retainerCrystals = GameStateService.GetAllRetainersCrystals();
            var cache = new RetainerDataCache(retainerGil, retainerCrystals);
            return GetValueWithCache(im, type, cache);
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.GameState, $"[TrackedDataRegistry] Failed to get value for {type}: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Cached retainer data for bulk value lookups within a single snapshot.
    /// This avoids repeatedly calling expensive retainer iteration methods.
    /// </summary>
    private readonly struct RetainerDataCache
    {
        public readonly long RetainerGil;
        public readonly long[] RetainerCrystals; // 18 elements
        
        public RetainerDataCache(long gil, long[] crystals)
        {
            RetainerGil = gil;
            RetainerCrystals = crystals;
        }
    }
    
    /// <summary>
    /// Gets current values for multiple data types in a single pass, caching expensive retainer lookups.
    /// This is significantly more efficient than calling GetCurrentValue() in a loop when tracking
    /// multiple types that require retainer data (Gil, Crystals, RetainerGil).
    /// </summary>
    /// <param name="types">The set of data types to retrieve values for.</param>
    /// <returns>Dictionary of type to current value. Types that couldn't be read are omitted.</returns>
    public unsafe Dictionary<TrackedDataType, long> GetCurrentValuesSnapshot(IEnumerable<TrackedDataType> types)
    {
        var results = new Dictionary<TrackedDataType, long>();
        
        try
        {
            var im = GameStateService.InventoryManagerInstance();
            if (im == null) return results;
            
            // Pre-fetch expensive retainer data once for the entire snapshot
            var retainerGil = GameStateService.GetAllRetainersGil();
            var retainerCrystals = GameStateService.GetAllRetainersCrystals();
            var cache = new RetainerDataCache(retainerGil, retainerCrystals);
            
            foreach (var type in types)
            {
                var value = GetValueWithCache(im, type, cache);
                if (value.HasValue)
                {
                    results[type] = value.Value;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.GameState, $"[TrackedDataRegistry] GetCurrentValuesSnapshot failed: {ex.Message}");
        }
        
        return results;
    }
    
    /// <summary>
    /// Gets the value for a single type using pre-cached retainer data.
    /// </summary>
    private unsafe long? GetValueWithCache(FFXIVClientStructs.FFXIV.Client.Game.InventoryManager* im, TrackedDataType type, RetainerDataCache cache)
    {
        try
        {
            return type switch
            {
                // Gil: player + all retainers (using cached retainer gil)
                TrackedDataType.Gil => im->GetGil() + cache.RetainerGil,
                
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
                
                // Crystals: player + retainers (using cached retainer crystals)
                TrackedDataType.CrystalsTotal => GetTotalCrystalsWithCache(im, cache.RetainerCrystals),
                TrackedDataType.FireCrystals => GetElementCrystalsWithCache(im, 0, cache.RetainerCrystals),
                TrackedDataType.IceCrystals => GetElementCrystalsWithCache(im, 1, cache.RetainerCrystals),
                TrackedDataType.WindCrystals => GetElementCrystalsWithCache(im, 2, cache.RetainerCrystals),
                TrackedDataType.EarthCrystals => GetElementCrystalsWithCache(im, 3, cache.RetainerCrystals),
                TrackedDataType.LightningCrystals => GetElementCrystalsWithCache(im, 4, cache.RetainerCrystals),
                TrackedDataType.WaterCrystals => GetElementCrystalsWithCache(im, 5, cache.RetainerCrystals),
                
                // Inventory - player only
                TrackedDataType.InventoryFreeSlots => im->GetEmptySlotsInBag(),
                
                // FC/Retainer - separate tracking for visibility (using cached retainer gil)
                TrackedDataType.FreeCompanyGil => im->GetFreeCompanyGil(),
                TrackedDataType.RetainerGil => cache.RetainerGil,
                TrackedDataType.FreeCompanyCredits => GameStateService.GetFreeCompanyCredits(),
                
                _ => null
            };
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.GameState, $"[TrackedDataRegistry] GetValueWithCache failed for {type}: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Gets total crystals across all types using pre-cached retainer crystal data.
    /// </summary>
    private static unsafe long GetTotalCrystalsWithCache(FFXIVClientStructs.FFXIV.Client.Game.InventoryManager* im, long[] retainerCrystals)
    {
        long total = 0;
        
        // Crystal item IDs: Shards (2-7), Crystals (8-13), Clusters (14-19)
        for (uint i = 2; i <= 19; i++)
        {
            try { total += im->GetInventoryItemCount(i); }
            catch (Exception) { /* ignore individual crystal read failures */ }
        }
        
        // Add cached retainer crystals
        for (int i = 0; i < 18; i++)
        {
            total += retainerCrystals[i];
        }
        
        return total;
    }
    
    /// <summary>
    /// Gets crystals for a specific element using pre-cached retainer crystal data.
    /// </summary>
    private static unsafe long GetElementCrystalsWithCache(FFXIVClientStructs.FFXIV.Client.Game.InventoryManager* im, int element, long[] retainerCrystals)
    {
        long total = 0;
        
        var shardId = (uint)(ConfigStatic.CrystalBaseItemId + element);
        var crystalId = (uint)(ConfigStatic.CrystalBaseItemId + ConfigStatic.CrystalTierOffset + element);
        var clusterId = (uint)(ConfigStatic.CrystalBaseItemId + 2 * ConfigStatic.CrystalTierOffset + element);
        
        try { total += im->GetInventoryItemCount(shardId); } catch (Exception) { /* ignore crystal read failure */ }
        try { total += im->GetInventoryItemCount(crystalId); } catch (Exception) { /* ignore crystal read failure */ }
        try { total += im->GetInventoryItemCount(clusterId); } catch (Exception) { /* ignore crystal read failure */ }
        
        // Add cached retainer crystals for this element
        total += retainerCrystals[element];           // Shard
        total += retainerCrystals[6 + element];       // Crystal
        total += retainerCrystals[12 + element];      // Cluster
        
        return total;
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

}
