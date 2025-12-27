using System.Numerics;
using Kaleidoscope;

namespace Kaleidoscope.Models;

/// <summary>
/// Types of special groupings that can be detected based on item selection.
/// </summary>
[Flags]
public enum SpecialGroupingType
{
    /// <summary>No special grouping detected.</summary>
    None = 0,
    
    /// <summary>All 18 crystal types are selected (6 elements × 3 tiers).</summary>
    AllCrystals = 1 << 0,
    
    /// <summary>All 3 gil currencies are selected (Gil, FC Gil, Retainer Gil).</summary>
    AllGil = 1 << 1
}

/// <summary>
/// Settings for special grouping filters when a special grouping is active.
/// </summary>
public class SpecialGroupingSettings
{
    /// <summary>
    /// Whether special grouping filters are currently enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// The type of special grouping currently active.
    /// </summary>
    public SpecialGroupingType ActiveGrouping { get; set; } = SpecialGroupingType.None;
    
    /// <summary>
    /// For AllCrystals grouping: which elements to show (all enabled by default).
    /// </summary>
    public HashSet<CrystalElement> EnabledElements { get; set; } = new()
    {
        CrystalElement.Fire,
        CrystalElement.Ice,
        CrystalElement.Wind,
        CrystalElement.Earth,
        CrystalElement.Lightning,
        CrystalElement.Water
    };
    
    /// <summary>
    /// For AllCrystals grouping: which tiers to show (all enabled by default).
    /// </summary>
    public HashSet<CrystalTier> EnabledTiers { get; set; } = new()
    {
        CrystalTier.Shard,
        CrystalTier.Crystal,
        CrystalTier.Cluster
    };
    
    // === AllGil Grouping Settings ===
    
    /// <summary>
    /// Whether the AllGil special grouping filter is enabled.
    /// </summary>
    public bool AllGilEnabled { get; set; } = false;
    
    /// <summary>
    /// Whether to merge FC Gil and Retainer Gil into the main Gil column.
    /// When enabled, FC Gil and Retainer Gil columns are hidden and their values are added to Gil.
    /// </summary>
    public bool MergeGilCurrencies { get; set; } = false;
    
    // === AllCrystals Grouping Settings ===
    
    /// <summary>
    /// Whether the AllCrystals special grouping filter is enabled.
    /// </summary>
    public bool AllCrystalsEnabled { get; set; } = false;
}

/// <summary>
/// Helper class for crystal-related calculations and special grouping detection.
/// </summary>
public static class SpecialGroupingHelper
{
    /// <summary>
    /// Total number of crystal types (6 elements × 3 tiers = 18).
    /// </summary>
    public const int TotalCrystalTypes = 18;
    
    /// <summary>
    /// Gets the item ID for a specific crystal element and tier.
    /// </summary>
    /// <param name="element">The crystal element.</param>
    /// <param name="tier">The crystal tier.</param>
    /// <returns>The game item ID for the crystal.</returns>
    public static uint GetCrystalItemId(CrystalElement element, CrystalTier tier)
    {
        // Base item ID is 2 (Fire Shard)
        // Elements are offset by 1 (Fire=0, Ice=1, Wind=2, Earth=3, Lightning=4, Water=5)
        // Tiers are offset by 6 (Shard=0, Crystal=6, Cluster=12)
        return (uint)(ConfigStatic.CrystalBaseItemId + (int)element + ((int)tier * ConfigStatic.CrystalTierOffset));
    }
    
    /// <summary>
    /// Checks if an item ID is a crystal (shard, crystal, or cluster).
    /// </summary>
    /// <param name="itemId">The item ID to check.</param>
    /// <returns>True if the item is a crystal.</returns>
    public static bool IsCrystalItem(uint itemId)
    {
        // Crystals are items 2-19 (Fire Shard=2, Water Cluster=19)
        return itemId >= ConfigStatic.CrystalBaseItemId && 
               itemId < ConfigStatic.CrystalBaseItemId + TotalCrystalTypes;
    }
    
    /// <summary>
    /// Gets the element of a crystal item.
    /// </summary>
    /// <param name="itemId">The crystal item ID.</param>
    /// <returns>The element, or null if not a crystal.</returns>
    public static CrystalElement? GetCrystalElement(uint itemId)
    {
        if (!IsCrystalItem(itemId)) return null;
        return (CrystalElement)((itemId - ConfigStatic.CrystalBaseItemId) % ConfigStatic.CrystalTierOffset);
    }
    
    /// <summary>
    /// Gets the tier of a crystal item.
    /// </summary>
    /// <param name="itemId">The crystal item ID.</param>
    /// <returns>The tier, or null if not a crystal.</returns>
    public static CrystalTier? GetCrystalTier(uint itemId)
    {
        if (!IsCrystalItem(itemId)) return null;
        return (CrystalTier)((itemId - ConfigStatic.CrystalBaseItemId) / ConfigStatic.CrystalTierOffset);
    }
    
    /// <summary>
    /// Gets all 18 crystal item IDs.
    /// </summary>
    /// <returns>A hash set containing all crystal item IDs.</returns>
    public static HashSet<uint> GetAllCrystalItemIds()
    {
        var ids = new HashSet<uint>();
        for (int tier = 0; tier < 3; tier++)
        {
            for (int element = 0; element < 6; element++)
            {
                ids.Add(GetCrystalItemId((CrystalElement)element, (CrystalTier)tier));
            }
        }
        return ids;
    }
    
    /// <summary>
    /// The three gil currency types.
    /// </summary>
    public static readonly TrackedDataType[] GilCurrencyTypes = new[]
    {
        TrackedDataType.Gil,
        TrackedDataType.FreeCompanyGil,
        TrackedDataType.RetainerGil
    };
    
    /// <summary>
    /// Checks if all 3 gil currencies are present in the columns.
    /// </summary>
    public static bool HasAllGilCurrencies(IEnumerable<Gui.Widgets.ItemColumnConfig> columns)
    {
        var currencyIds = columns
            .Where(c => c.IsCurrency)
            .Select(c => c.Id)
            .ToHashSet();
        
        return GilCurrencyTypes.All(t => currencyIds.Contains((uint)t));
    }
    
    /// <summary>
    /// Detects which special groupings are available based on the selected item columns.
    /// Returns flags for all detected groupings.
    /// </summary>
    /// <param name="columns">The list of column configurations.</param>
    /// <returns>Flags indicating all detected special grouping types.</returns>
    public static SpecialGroupingType DetectSpecialGrouping(IEnumerable<Gui.Widgets.ItemColumnConfig> columns)
    {
        var result = SpecialGroupingType.None;
        var columnList = columns.ToList();
        
        // Check for AllCrystals: all 18 crystal types must be present
        var itemIds = columnList
            .Where(c => !c.IsCurrency)
            .Select(c => c.Id)
            .ToHashSet();
        
        var allCrystalIds = GetAllCrystalItemIds();
        if (allCrystalIds.IsSubsetOf(itemIds))
        {
            result |= SpecialGroupingType.AllCrystals;
        }
        
        // Check for AllGil: all 3 gil currencies must be present
        if (HasAllGilCurrencies(columnList))
        {
            result |= SpecialGroupingType.AllGil;
        }
        
        return result;
    }
    
    /// <summary>
    /// Filters a list of columns based on the special grouping settings.
    /// </summary>
    /// <param name="columns">The original list of columns.</param>
    /// <param name="settings">The special grouping settings.</param>
    /// <returns>A filtered list of columns to display.</returns>
    public static List<Gui.Widgets.ItemColumnConfig> ApplySpecialGroupingFilter(
        IEnumerable<Gui.Widgets.ItemColumnConfig> columns, 
        SpecialGroupingSettings settings)
    {
        return columns.Where(c =>
        {
            // Apply AllGil filter: hide FC Gil and Retainer Gil when merging
            if (settings.AllGilEnabled && settings.MergeGilCurrencies && c.IsCurrency)
            {
                var currencyType = (TrackedDataType)c.Id;
                if (currencyType == TrackedDataType.FreeCompanyGil || 
                    currencyType == TrackedDataType.RetainerGil)
                {
                    return false; // Hide these, they'll be merged into Gil
                }
            }
            
            // Apply AllCrystals filter: filter by element and tier
            if (settings.AllCrystalsEnabled && !c.IsCurrency)
            {
                // Check if it's a crystal
                if (IsCrystalItem(c.Id))
                {
                    var element = GetCrystalElement(c.Id);
                    var tier = GetCrystalTier(c.Id);
                    
                    if (element.HasValue && tier.HasValue)
                    {
                        // Filter by enabled elements and tiers
                        return settings.EnabledElements.Contains(element.Value) &&
                               settings.EnabledTiers.Contains(tier.Value);
                    }
                }
            }
            
            // All other columns pass through
            return true;
        }).ToList();
    }
    
    /// <summary>
    /// Checks if a currency type is one that should be merged into Gil.
    /// </summary>
    public static bool IsGilMergeSource(TrackedDataType currencyType)
    {
        return currencyType == TrackedDataType.FreeCompanyGil || 
               currencyType == TrackedDataType.RetainerGil;
    }
    
    /// <summary>
    /// Gets the display name for a crystal element.
    /// </summary>
    public static string GetElementName(CrystalElement element) => element switch
    {
        CrystalElement.Fire => "Fire",
        CrystalElement.Ice => "Ice",
        CrystalElement.Wind => "Wind",
        CrystalElement.Earth => "Earth",
        CrystalElement.Lightning => "Lightning",
        CrystalElement.Water => "Water",
        _ => element.ToString()
    };
    
    /// <summary>
    /// Gets the display name for a crystal tier.
    /// </summary>
    public static string GetTierName(CrystalTier tier) => tier switch
    {
        CrystalTier.Shard => "Shard",
        CrystalTier.Crystal => "Crystal",
        CrystalTier.Cluster => "Cluster",
        _ => tier.ToString()
    };
    
    /// <summary>
    /// Gets a color associated with a crystal element.
    /// </summary>
    public static Vector4 GetElementColor(CrystalElement element) => element switch
    {
        CrystalElement.Fire => new Vector4(1.0f, 0.4f, 0.2f, 1.0f),      // Orange-red
        CrystalElement.Ice => new Vector4(0.6f, 0.85f, 1.0f, 1.0f),      // Light blue
        CrystalElement.Wind => new Vector4(0.6f, 1.0f, 0.6f, 1.0f),      // Light green
        CrystalElement.Earth => new Vector4(0.8f, 0.65f, 0.4f, 1.0f),    // Brown/tan
        CrystalElement.Lightning => new Vector4(0.9f, 0.7f, 1.0f, 1.0f), // Light purple
        CrystalElement.Water => new Vector4(0.4f, 0.6f, 1.0f, 1.0f),     // Blue
        _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
    };
}
