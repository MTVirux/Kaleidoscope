using Kaleidoscope.Models;
using Kaleidoscope.Services.Resources;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Registry of all trackable data types and their definitions.
/// Provides methods to fetch current values from game state.
/// Includes cached lookups for performance optimization.
/// </summary>
public sealed class TrackedDataRegistry : IRequiredService
{
    private readonly Dictionary<TrackedDataType, TrackedDataDefinition> _definitions = new();

    private readonly ResourceStore _resourceStore;

    private Dictionary<TrackedDataCategory, List<TrackedDataDefinition>>? _byCategory;
    private Dictionary<uint, TrackedDataDefinition>? _byItemId;
    private List<TrackedDataType>? _allTypes;
    private List<TrackedDataDefinition>? _enabledByDefaultList;

    public IReadOnlyDictionary<TrackedDataType, TrackedDataDefinition> Definitions => _definitions;

    public TrackedDataRegistry(ResourceStore resourceStore)
    {
        _resourceStore = resourceStore;
        RegisterAllTypes();
        BuildCaches();
    }
    
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
        RegisterTrackedType(new TrackedDataDefinition
        {
            Type = TrackedDataType.Gil,
            DisplayName = "Character Gil",
            ShortName = "Char Gil",
            Category = TrackedDataCategory.Gil,
            ItemId = 1,
            MaxValue = 999_999_999,
            EnabledByDefault = true,
            Description = "Gil held by the active character (excludes retainers and FC)."
        });

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
    /// Gets the current value for a data type from the ResourceStore, scoped to the active
    /// character. Returns null when no character is logged in (PlayerContentId == 0).
    /// Crystal types span three tiers (shard/crystal/cluster) per element and include the
    /// active character's player containers plus all retainers in the store (see SumRetainersForActiveChar).
    /// </summary>
    public long? GetCurrentValue(TrackedDataType type)
    {
        var pid = GameStateService.PlayerContentId;
        if (pid == 0) return null;

        return type switch
        {
            // Player gil — active character only
            TrackedDataType.Gil               => _resourceStore.GetSumForOwner(pid, Models.Resources.OwnerKind.Player, Resources.ResourceCatalog.GilItemId),

            // Retainer gil — sum of retainers belonging to the active character
            TrackedDataType.RetainerGil       => SumRetainersForActiveChar(pid, Resources.ResourceCatalog.GilItemId),

            // FC gil — active character's free company (single owner; SumForOwner with FCID)
            TrackedDataType.FreeCompanyGil    => _resourceStore.GetSumForOwner(GameStateService.GetFreeCompanyId(), Models.Resources.OwnerKind.FreeCompany, Resources.ResourceCatalog.GilItemId),

            // FC credits — stored under the active character's content id but with OwnerKind.FreeCompany
            // (see MemoryPoller / AutoRetainerFcPointsSyncService), so the default Player-scoped read misses them.
            TrackedDataType.FreeCompanyCredits => _resourceStore.GetSumForOwner(pid, Models.Resources.OwnerKind.FreeCompany, Resources.ResourceCatalog.FCCreditsItemId),

            // Crystals — active character's player containers + their retainers
            TrackedDataType.FireCrystals      => SumCrystalsForElement(pid, 0),
            TrackedDataType.IceCrystals       => SumCrystalsForElement(pid, 1),
            TrackedDataType.WindCrystals      => SumCrystalsForElement(pid, 2),
            TrackedDataType.EarthCrystals     => SumCrystalsForElement(pid, 3),
            TrackedDataType.LightningCrystals => SumCrystalsForElement(pid, 4),
            TrackedDataType.WaterCrystals     => SumCrystalsForElement(pid, 5),
            TrackedDataType.CrystalsTotal     => SumAllCrystals(pid),

            // Default: active character's player-scoped sum (currencies in Currency container, etc.)
            _ => Resources.ResourceCatalog.TryGetMappingForTrackedDataType(type, out var mapping)
                ? _resourceStore.GetSumForOwner(pid, Models.Resources.OwnerKind.Player, mapping.ItemId)
                : null,
        };
    }

    private long SumRetainersForActiveChar(ulong pid, uint itemId)
    {
        // Phase 3.5 sets parent_owner_id on retainer rows correctly in DB but in-memory Resource
        // doesn't carry it. As a best-effort heuristic, sum across all OwnerKind.Retainer rows for
        // the given item. This is correct when only one character's retainers exist in the store
        // (the common case). For users with multiple characters' retainers loaded simultaneously
        // this overcounts — that's a known gap; fix when reported.
        long total = 0;
        foreach (var r in _resourceStore.Snapshot())
        {
            if (r.Key.OwnerKind == Models.Resources.OwnerKind.Retainer && r.Key.ItemId == itemId)
                total += r.Quantity;
        }
        return total;
    }

    /// <summary>
    /// Sums all three tiers (shard, crystal, cluster) for one element scoped to the active
    /// character's player containers plus all retainers in the store.
    /// Element index (0–5): Fire=0, Ice=1, Wind=2, Earth=3, Lightning=4, Water=5.
    /// Item IDs: shard = 2+element, crystal = 8+element, cluster = 14+element.
    /// </summary>
    private long SumCrystalsForElement(ulong pid, int element)
    {
        var ids = new uint[]
        {
            (uint)(2 + element),   // Shard
            (uint)(8 + element),   // Crystal
            (uint)(14 + element),  // Cluster
        };
        long total = 0;
        foreach (var id in ids)
        {
            total += _resourceStore.GetSumForOwner(pid, Models.Resources.OwnerKind.Player, id);
            total += SumRetainersForActiveChar(pid, id);
        }
        return total;
    }

    /// <summary>
    /// Sums all 18 crystal item IDs (2–19) scoped to the active character's player containers
    /// plus all retainers in the store.
    /// </summary>
    private long SumAllCrystals(ulong pid)
    {
        long total = 0;
        for (uint id = 2; id <= 19; id++)
        {
            total += _resourceStore.GetSumForOwner(pid, Models.Resources.OwnerKind.Player, id);
            total += SumRetainersForActiveChar(pid, id);
        }
        return total;
    }

    /// <summary>
    /// Gets current values for multiple data types in a single pass via ResourceStore.
    /// </summary>
    /// <param name="types">The set of data types to retrieve values for.</param>
    /// <returns>Dictionary of type to current value. Types with no mapping are omitted.</returns>
    public Dictionary<TrackedDataType, long> GetCurrentValuesSnapshot(IEnumerable<TrackedDataType> types)
    {
        var results = new Dictionary<TrackedDataType, long>();
        foreach (var type in types)
        {
            var v = GetCurrentValue(type);
            if (v.HasValue) results[type] = v.Value;
        }
        return results;
    }

}
