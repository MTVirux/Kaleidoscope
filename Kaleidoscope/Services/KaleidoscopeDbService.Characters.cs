using Microsoft.Data.Sqlite;
using System.Text;

namespace Kaleidoscope.Services;

public sealed partial class KaleidoscopeDbService
{

    /// <summary>
    /// Gets all character IDs that have data for a variable.
    /// </summary>
    public List<ulong> GetAvailableCharacters(string variable)
    {
        var result = new List<ulong>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT character_id FROM series WHERE variable = $v ORDER BY character_id";
                cmd.Parameters.AddWithValue("$v", variable);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cid = reader.GetInt64(0);
                    if (cid != 0)
                        result.Add((ulong)cid);
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetAvailableCharacters failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all unique variable names that start with the given prefix.
    /// Used to find all item tracking series (Item_*, ItemRetainer_*, etc.).
    /// </summary>
    public List<string> GetAllVariablesWithPrefix(string prefix)
    {
        var result = new List<string>();

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return result;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT variable FROM series WHERE variable LIKE $prefix ORDER BY variable";
                cmd.Parameters.AddWithValue("$prefix", prefix + "%");

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var variable = reader.GetString(0);
                    if (!string.IsNullOrEmpty(variable))
                        result.Add(variable);
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetAllVariablesWithPrefix failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Saves or updates a character's game name (automatically detected from the game).
    /// Preserves any existing display_name that was set by the user.
    /// </summary>
    public bool SaveCharacterName(ulong characterId, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                // First check if record exists to preserve display_name and time_series_color
                cmd.CommandText = "SELECT display_name, time_series_color FROM character_names WHERE character_id = $c";
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                string? existingDisplayName = null;
                long? existingColor = null;
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        existingDisplayName = reader.IsDBNull(0) ? null : reader.GetString(0);
                        existingColor = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                    }
                }
                
                cmd.CommandText = "INSERT OR REPLACE INTO character_names(character_id, name, display_name, time_series_color) VALUES($c, $n, $d, $col)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$d", existingDisplayName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$col", existingColor.HasValue ? (object)existingColor.Value : DBNull.Value);
                cmd.ExecuteNonQuery();
                
                // Invalidate cache so next lookup gets fresh data
                InvalidateCharacterNameCache();
                return true;
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] SaveCharacterName failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Saves or updates a character's display name (user-customizable).
    /// </summary>
    /// <param name="characterId">The character's content ID.</param>
    /// <param name="displayName">The custom display name. Pass null to clear and use game name.</param>
    /// <returns>True if successful.</returns>
    public bool SaveCharacterDisplayName(ulong characterId, string? displayName)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                // Update only the display_name column, or insert if not exists
                cmd.CommandText = @"
                    INSERT INTO character_names(character_id, name, display_name) 
                    VALUES($c, NULL, $d)
                    ON CONFLICT(character_id) DO UPDATE SET display_name = $d";
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.Parameters.AddWithValue("$d", string.IsNullOrEmpty(displayName) ? (object)DBNull.Value : displayName);
                cmd.ExecuteNonQuery();
                
                // Invalidate cache so next lookup gets fresh data
                InvalidateCharacterNameCache();
                return true;
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] SaveCharacterDisplayName failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Saves or updates a character's time series color.
    /// </summary>
    /// <param name="characterId">The character's content ID.</param>
    /// <param name="color">The ARGB color value. Pass null to clear and use default colors.</param>
    /// <returns>True if successful.</returns>
    public bool SaveCharacterTimeSeriesColor(ulong characterId, uint? color)
    {
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return false;

            try
            {
                using var cmd = _connection.CreateCommand();
                // Update only the time_series_color column, or insert if not exists
                cmd.CommandText = @"
                    INSERT INTO character_names(character_id, name, display_name, time_series_color) 
                    VALUES($c, NULL, NULL, $col)
                    ON CONFLICT(character_id) DO UPDATE SET time_series_color = $col";
                cmd.Parameters.AddWithValue("$c", (long)characterId);
                cmd.Parameters.AddWithValue("$col", color.HasValue ? (object)(long)color.Value : DBNull.Value);
                cmd.ExecuteNonQuery();
                
                // Invalidate cache so next lookup gets fresh data
                InvalidateCharacterNameCache();
                return true;
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] SaveCharacterTimeSeriesColor failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Gets the display name for a character (custom display_name if set, otherwise game name).
    /// Uses cached data if available to avoid repeated DB queries.
    /// </summary>
    public string? GetCharacterName(ulong characterId)
    {
        // Try cache first
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return names.DisplayName ?? names.GameName;
        
        return null;
    }

    /// <summary>
    /// Gets the game name for a character (the name automatically detected from the game).
    /// </summary>
    public string? GetCharacterGameName(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return names.GameName;
        
        return null;
    }

    /// <summary>
    /// Gets the custom display name for a character (null if not set).
    /// </summary>
    public string? GetCharacterDisplayName(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return names.DisplayName;
        
        return null;
    }

    /// <summary>
    /// Gets both the game name and display name for a character.
    /// </summary>
    public (string? GameName, string? DisplayName) GetCharacterNames(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return (names.GameName, names.DisplayName);
        
        return (null, null);
    }

    /// <summary>
    /// Gets the time series color for a character (null if not set).
    /// </summary>
    public uint? GetCharacterTimeSeriesColor(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var names))
            return names.TimeSeriesColor;
        
        return null;
    }

    /// <summary>
    /// Gets all character data (game name, display name, and time series color).
    /// </summary>
    public (string? GameName, string? DisplayName, uint? TimeSeriesColor) GetCharacterData(ulong characterId)
    {
        var cache = GetCharacterNameCache();
        if (cache.TryGetValue(characterId, out var data))
            return data;
        
        return (null, null, null);
    }
    
    /// <summary>
    /// Gets or refreshes the character name cache.
    /// </summary>
    private Dictionary<ulong, (string? GameName, string? DisplayName, uint? TimeSeriesColor)> GetCharacterNameCache()
    {
        var now = DateTime.UtcNow;
        if (_characterNameCache != null && (now - _characterNameCacheTime).TotalSeconds < CharacterNameCacheExpirySeconds)
        {
            return _characterNameCache;
        }
        
        // Refresh cache
        var newCache = new Dictionary<ulong, (string?, string?, uint?)>();
        
        lock (_writeLock)
        {
            EnsureConnection();
            if (_connection == null) return newCache;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT character_id, name, display_name, time_series_color FROM character_names";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var cid = reader.GetInt64(0);
                    var gameName = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var displayName = reader.IsDBNull(2) ? null : reader.GetString(2);
                    uint? timeSeriesColor = reader.IsDBNull(3) ? null : (uint)reader.GetInt64(3);
                    if (cid != 0)
                        newCache[(ulong)cid] = (gameName, displayName, timeSeriesColor);
                }
            }
            catch (Exception ex)
            {
                LogService.Debug(LogCategory.Database, $"[KaleidoscopeDb] GetCharacterNameCache failed: {ex.Message}");
            }
        }
        
        _characterNameCache = newCache;
        _characterNameCacheTime = now;
        return newCache;
    }
    
    /// <summary>
    /// Invalidates the character name cache.
    /// Call this after saving or updating character names.
    /// </summary>
    public void InvalidateCharacterNameCache()
    {
        _characterNameCache = null;
        _characterNameCacheTime = DateTime.MinValue;
    }

    /// <summary>
    /// Gets all stored character name mappings (returns display_name if set, otherwise game name).
    /// Uses cached data to avoid repeated DB queries.
    /// </summary>
    public List<(ulong characterId, string? name)> GetAllCharacterNames()
    {
        var cache = GetCharacterNameCache();
        var result = new List<(ulong, string?)>(cache.Count);
        
        foreach (var kvp in cache)
        {
            // Return display_name if set, otherwise game name
            result.Add((kvp.Key, kvp.Value.DisplayName ?? kvp.Value.GameName));
        }
        
        return result;
    }

    /// <summary>
    /// Gets all stored character name mappings with both game and display names.
    /// </summary>
    public List<(ulong characterId, string? gameName, string? displayName)> GetAllCharacterNamesExtended()
    {
        var cache = GetCharacterNameCache();
        var result = new List<(ulong, string?, string?)>(cache.Count);
        
        foreach (var kvp in cache)
        {
            result.Add((kvp.Key, kvp.Value.GameName, kvp.Value.DisplayName));
        }
        
        return result;
    }

    /// <summary>
    /// Gets all stored character data including time series colors.
    /// </summary>
    public List<(ulong characterId, string? gameName, string? displayName, uint? timeSeriesColor)> GetAllCharacterDataExtended()
    {
        var cache = GetCharacterNameCache();
        var result = new List<(ulong, string?, string?, uint?)>(cache.Count);
        
        foreach (var kvp in cache)
        {
            result.Add((kvp.Key, kvp.Value.GameName, kvp.Value.DisplayName, kvp.Value.TimeSeriesColor));
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets all stored character name mappings as a dictionary (display_name if set, otherwise game name).
    /// </summary>
    public IReadOnlyDictionary<ulong, string?> GetAllCharacterNamesDict()
    {
        var cache = GetCharacterNameCache();
        var result = new Dictionary<ulong, string?>(cache.Count);
        foreach (var kvp in cache)
        {
            result[kvp.Key] = kvp.Value.DisplayName ?? kvp.Value.GameName;
        }
        return result;
    }

}
