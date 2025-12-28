using System.Text.Json.Serialization;

namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// World information from Universalis API.
/// GET /api/v2/worlds
/// </summary>
public sealed class UniversalisWorld
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Data center information from Universalis API.
/// GET /api/v2/data-centers
/// </summary>
public sealed class UniversalisDataCenter
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("worlds")]
    public List<int>? Worlds { get; set; }
}

/// <summary>
/// Cached world/DC/region information for display and selection.
/// </summary>
public sealed class UniversalisWorldData
{
    /// <summary>All available worlds.</summary>
    public List<UniversalisWorld> Worlds { get; set; } = new();

    /// <summary>All available data centers.</summary>
    public List<UniversalisDataCenter> DataCenters { get; set; } = new();

    /// <summary>When this data was last fetched.</summary>
    public DateTime LastUpdated { get; set; } = DateTime.MinValue;

    /// <summary>Gets all unique region names.</summary>
    public IEnumerable<string> Regions => DataCenters
        .Where(dc => !string.IsNullOrEmpty(dc.Region))
        .Select(dc => dc.Region!)
        .Distinct()
        .OrderBy(r => r);

    /// <summary>Gets worlds for a specific data center.</summary>
    public IEnumerable<UniversalisWorld> GetWorldsForDataCenter(string dcName)
    {
        var dc = DataCenters.FirstOrDefault(d => d.Name == dcName);
        if (dc?.Worlds == null) yield break;

        foreach (var worldId in dc.Worlds)
        {
            var world = Worlds.FirstOrDefault(w => w.Id == worldId);
            if (world != null) yield return world;
        }
    }

    /// <summary>Gets data centers for a specific region.</summary>
    public IEnumerable<UniversalisDataCenter> GetDataCentersForRegion(string region)
    {
        return DataCenters.Where(dc => dc.Region == region);
    }

    /// <summary>Gets world name by ID.</summary>
    public string? GetWorldName(int worldId)
    {
        return Worlds.FirstOrDefault(w => w.Id == worldId)?.Name;
    }

    /// <summary>Gets world ID by name (case-insensitive).</summary>
    public int? GetWorldId(string worldName)
    {
        return Worlds.FirstOrDefault(w => string.Equals(w.Name, worldName, StringComparison.OrdinalIgnoreCase))?.Id;
    }
    
    /// <summary>Gets data center for a world by world name (case-insensitive).</summary>
    public UniversalisDataCenter? GetDataCenterForWorld(string worldName)
    {
        var worldId = GetWorldId(worldName);
        if (worldId == null) return null;
        return DataCenters.FirstOrDefault(dc => dc.Worlds?.Contains(worldId.Value) == true);
    }
    
    /// <summary>Gets region for a world by world name.</summary>
    public string? GetRegionForWorld(string worldName)
    {
        return GetDataCenterForWorld(worldName)?.Region;
    }

    /// <summary>Gets data center for a world by world ID.</summary>
    public UniversalisDataCenter? GetDataCenterForWorldId(int worldId)
    {
        return DataCenters.FirstOrDefault(dc => dc.Worlds?.Contains(worldId) == true);
    }

    /// <summary>Gets region for a world by world ID.</summary>
    public string? GetRegionForWorldId(int worldId)
    {
        return GetDataCenterForWorldId(worldId)?.Region;
    }

    /// <summary>Gets all world IDs for a given region.</summary>
    public HashSet<int> GetWorldIdsForRegion(string regionName)
    {
        var worldIds = new HashSet<int>();
        foreach (var dc in GetDataCentersForRegion(regionName))
        {
            if (dc.Worlds != null)
            {
                foreach (var wid in dc.Worlds)
                    worldIds.Add(wid);
            }
        }
        return worldIds;
    }

    /// <summary>Gets all world IDs for a given data center.</summary>
    public HashSet<int> GetWorldIdsForDataCenter(string dcName)
    {
        var worldIds = new HashSet<int>();
        var dc = DataCenters.FirstOrDefault(d => d.Name == dcName);
        if (dc?.Worlds != null)
        {
            foreach (var wid in dc.Worlds)
                worldIds.Add(wid);
        }
        return worldIds;
    }

    /// <summary>
    /// Resolves a price match mode to a set of world IDs for a given character's world.
    /// Returns null if the mode is Global (no filtering needed).
    /// </summary>
    /// <param name="characterWorldId">The world ID of the character whose inventory is being valued.</param>
    /// <param name="mode">The price match mode to apply.</param>
    /// <returns>Set of world IDs to include in price lookup, or null for global (all worlds).</returns>
    public HashSet<int>? GetWorldIdsForPriceMatchMode(int characterWorldId, PriceMatchMode mode)
    {
        switch (mode)
        {
            case PriceMatchMode.World:
                return new HashSet<int> { characterWorldId };

            case PriceMatchMode.DataCenter:
                var dc = GetDataCenterForWorldId(characterWorldId);
                if (dc?.Worlds != null)
                    return dc.Worlds.ToHashSet();
                return new HashSet<int> { characterWorldId };

            case PriceMatchMode.Region:
                var region = GetRegionForWorldId(characterWorldId);
                if (region != null)
                    return GetWorldIdsForRegion(region);
                return new HashSet<int> { characterWorldId };

            case PriceMatchMode.RegionPlusOceania:
                var charRegion = GetRegionForWorldId(characterWorldId);
                var worldIds = new HashSet<int>();
                if (charRegion != null)
                {
                    foreach (var wid in GetWorldIdsForRegion(charRegion))
                        worldIds.Add(wid);
                }
                // Always add Oceania
                foreach (var wid in GetWorldIdsForRegion("Oceania"))
                    worldIds.Add(wid);
                if (worldIds.Count == 0)
                    worldIds.Add(characterWorldId);
                return worldIds;

            case PriceMatchMode.Global:
            default:
                return null; // No filtering - use all worlds
        }
    }

    /// <summary>
    /// Resolves a scope configuration to a set of included world IDs.
    /// Returns null if the scope is "All" (meaning no filtering needed).
    /// </summary>
    [Obsolete("Use GetWorldIdsForPriceMatchMode instead")]
    public HashSet<int>? GetIncludedWorldIds(
        PriceTrackingScopeMode scopeMode,
        IEnumerable<string> selectedRegions,
        IEnumerable<string> selectedDataCenters,
        IEnumerable<int> selectedWorldIds)
    {
        switch (scopeMode)
        {
            case PriceTrackingScopeMode.All:
                return null; // No filtering

            case PriceTrackingScopeMode.ByWorld:
                var worldSet = selectedWorldIds.ToHashSet();
                return worldSet.Count > 0 ? worldSet : null;

            case PriceTrackingScopeMode.ByDataCenter:
                var dcWorldIds = new HashSet<int>();
                foreach (var dcName in selectedDataCenters)
                {
                    var dc = DataCenters.FirstOrDefault(d => d.Name == dcName);
                    if (dc?.Worlds != null)
                    {
                        foreach (var wid in dc.Worlds)
                            dcWorldIds.Add(wid);
                    }
                }
                return dcWorldIds.Count > 0 ? dcWorldIds : null;

            case PriceTrackingScopeMode.ByRegion:
                var regionWorldIds = new HashSet<int>();
                foreach (var regionName in selectedRegions)
                {
                    foreach (var dc in GetDataCentersForRegion(regionName))
                    {
                        if (dc.Worlds != null)
                        {
                            foreach (var wid in dc.Worlds)
                                regionWorldIds.Add(wid);
                        }
                    }
                }
                return regionWorldIds.Count > 0 ? regionWorldIds : null;

            default:
                return null;
        }
    }
}
