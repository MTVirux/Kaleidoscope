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

    /// <summary>Gets world ID by name.</summary>
    public int? GetWorldId(string worldName)
    {
        return Worlds.FirstOrDefault(w => w.Name == worldName)?.Id;
    }
    
    /// <summary>Gets data center for a world by world name.</summary>
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
}
