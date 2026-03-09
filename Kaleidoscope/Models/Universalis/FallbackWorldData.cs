namespace Kaleidoscope.Models.Universalis;

/// <summary>
/// Provides fallback world and data center data when the Universalis API is unavailable.
/// This data is periodically updated to match the current game state.
/// Last updated: 2026-01-11 (FFXIV Patch 7.x)
/// </summary>
public static class FallbackWorldData
{
    /// <summary>
    /// Creates fallback world data when the Universalis API is unavailable.
    /// </summary>
    public static UniversalisWorldData CreateFallback()
    {
        return new UniversalisWorldData
        {
            Worlds = GetFallbackWorlds(),
            DataCenters = GetFallbackDataCenters(),
            LastUpdated = DateTime.MinValue, // Indicates this is fallback data
        };
    }

    private static List<UniversalisWorld> GetFallbackWorlds()
    {
        return new List<UniversalisWorld>
        {
            // Japan - Elemental
            new() { Id = 45, Name = "Aegis" },
            new() { Id = 49, Name = "Atomos" },
            new() { Id = 50, Name = "Carbuncle" },
            new() { Id = 58, Name = "Garuda" },
            new() { Id = 68, Name = "Gungnir" },
            new() { Id = 72, Name = "Kujata" },
            new() { Id = 90, Name = "Tonberry" },
            new() { Id = 94, Name = "Typhon" },
            
            // Japan - Gaia
            new() { Id = 43, Name = "Alexander" },
            new() { Id = 46, Name = "Bahamut" },
            new() { Id = 51, Name = "Durandal" },
            new() { Id = 59, Name = "Fenrir" },
            new() { Id = 69, Name = "Ifrit" },
            new() { Id = 76, Name = "Ridill" },
            new() { Id = 92, Name = "Tiamat" },
            new() { Id = 98, Name = "Ultima" },
            
            // Japan - Mana
            new() { Id = 23, Name = "Asura" },
            new() { Id = 28, Name = "Pandaemonium" },
            new() { Id = 44, Name = "Anima" },
            new() { Id = 47, Name = "Belias" },
            new() { Id = 48, Name = "Chocobo" },
            new() { Id = 61, Name = "Hades" },
            new() { Id = 70, Name = "Ixion" },
            new() { Id = 96, Name = "Titan" },
            
            // Japan - Meteor
            new() { Id = 24, Name = "Ramuh" },
            new() { Id = 29, Name = "Unicorn" },
            new() { Id = 30, Name = "Valefor" },
            new() { Id = 31, Name = "Yojimbo" },
            new() { Id = 32, Name = "Zeromus" },
            
            // North America - Aether
            new() { Id = 40, Name = "Adamantoise" },
            new() { Id = 54, Name = "Cactuar" },
            new() { Id = 57, Name = "Faerie" },
            new() { Id = 63, Name = "Gilgamesh" },
            new() { Id = 65, Name = "Jenova" },
            new() { Id = 73, Name = "Midgardsormr" },
            new() { Id = 79, Name = "Sargatanas" },
            new() { Id = 99, Name = "Siren" },
            
            // North America - Primal
            new() { Id = 35, Name = "Behemoth" },
            new() { Id = 53, Name = "Excalibur" },
            new() { Id = 55, Name = "Exodus" },
            new() { Id = 64, Name = "Famfrit" },
            new() { Id = 77, Name = "Hyperion" },
            new() { Id = 78, Name = "Lamia" },
            new() { Id = 93, Name = "Leviathan" },
            new() { Id = 95, Name = "Ultros" },
            
            // North America - Crystal
            new() { Id = 34, Name = "Brynhildr" },
            new() { Id = 37, Name = "Coeurl" },
            new() { Id = 41, Name = "Balmung" },
            new() { Id = 62, Name = "Goblin" },
            new() { Id = 74, Name = "Malboro" },
            new() { Id = 75, Name = "Mateus" },
            new() { Id = 81, Name = "Zalera" },
            new() { Id = 91, Name = "Diabolos" },
            
            // North America - Dynamis
            new() { Id = 406, Name = "Halicarnassus" },
            new() { Id = 407, Name = "Maduin" },
            new() { Id = 408, Name = "Marilith" },
            new() { Id = 409, Name = "Seraph" },
            new() { Id = 411, Name = "Cuchulainn" },
            new() { Id = 412, Name = "Golem" },
            new() { Id = 413, Name = "Kraken" },
            new() { Id = 414, Name = "Rafflesia" },
            
            // Europe - Chaos
            new() { Id = 39, Name = "Omega" },
            new() { Id = 71, Name = "Moogle" },
            new() { Id = 80, Name = "Cerberus" },
            new() { Id = 83, Name = "Louisoix" },
            new() { Id = 85, Name = "Ragnarok" },
            new() { Id = 97, Name = "Spriggan" },
            new() { Id = 400, Name = "Sagittarius" },
            new() { Id = 401, Name = "Phantom" },
            
            // Europe - Light
            new() { Id = 33, Name = "Twintania" },
            new() { Id = 36, Name = "Lich" },
            new() { Id = 42, Name = "Zodiark" },
            new() { Id = 56, Name = "Phoenix" },
            new() { Id = 66, Name = "Odin" },
            new() { Id = 67, Name = "Shiva" },
            new() { Id = 402, Name = "Alpha" },
            new() { Id = 403, Name = "Raiden" },
            
            // Europe - Shadow (newer DC)
            new() { Id = 404, Name = "Innocence" },
            new() { Id = 405, Name = "Pixie" },
            new() { Id = 410, Name = "Titania" },
            new() { Id = 415, Name = "Tycoon" },
            
            // Oceania - Materia
            new() { Id = 21, Name = "Ravana" },
            new() { Id = 22, Name = "Bismarck" },
            new() { Id = 86, Name = "Sephirot" },
            new() { Id = 87, Name = "Sophia" },
            new() { Id = 88, Name = "Zurvan" },
        };
    }

    private static List<UniversalisDataCenter> GetFallbackDataCenters()
    {
        return new List<UniversalisDataCenter>
        {
            // Japan
            new() { Name = "Elemental", Region = "Japan", Worlds = new List<int> { 45, 49, 50, 58, 68, 72, 90, 94 } },
            new() { Name = "Gaia", Region = "Japan", Worlds = new List<int> { 43, 46, 51, 59, 69, 76, 92, 98 } },
            new() { Name = "Mana", Region = "Japan", Worlds = new List<int> { 23, 28, 44, 47, 48, 61, 70, 96 } },
            new() { Name = "Meteor", Region = "Japan", Worlds = new List<int> { 24, 29, 30, 31, 32 } },
            
            // North America
            new() { Name = "Aether", Region = "North-America", Worlds = new List<int> { 40, 54, 57, 63, 65, 73, 79, 99 } },
            new() { Name = "Primal", Region = "North-America", Worlds = new List<int> { 35, 53, 55, 64, 77, 78, 93, 95 } },
            new() { Name = "Crystal", Region = "North-America", Worlds = new List<int> { 34, 37, 41, 62, 74, 75, 81, 91 } },
            new() { Name = "Dynamis", Region = "North-America", Worlds = new List<int> { 406, 407, 408, 409, 411, 412, 413, 414 } },
            
            // Europe
            new() { Name = "Chaos", Region = "Europe", Worlds = new List<int> { 39, 71, 80, 83, 85, 97, 400, 401 } },
            new() { Name = "Light", Region = "Europe", Worlds = new List<int> { 33, 36, 42, 56, 66, 67, 402, 403 } },
            new() { Name = "Shadow", Region = "Europe", Worlds = new List<int> { 404, 405, 410, 415 } },
            
            // Oceania
            new() { Name = "Materia", Region = "Oceania", Worlds = new List<int> { 21, 22, 86, 87, 88 } },
        };
    }
}
