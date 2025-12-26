using System.Numerics;

namespace Kaleidoscope.Gui.Helpers;

/// <summary>
/// Utility methods for importing settings from serialized dictionaries.
/// Handles multiple JSON formats (Newtonsoft.Json, System.Text.Json, in-memory).
/// </summary>
public static class SettingsImportHelper
{
    /// <summary>
    /// Imports a Vector4 color from various serialized formats.
    /// </summary>
    /// <param name="settings">The settings dictionary.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The imported color, or null if not found or invalid.</returns>
    public static Vector4? ImportColor(Dictionary<string, object?>? settings, string key)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return null;

        try
        {
            // Handle Newtonsoft.Json JArray (used by ConfigManager)
            if (value is Newtonsoft.Json.Linq.JArray jArray && jArray.Count >= 4)
            {
                return new Vector4(
                    jArray[0].ToObject<float>(),
                    jArray[1].ToObject<float>(),
                    jArray[2].ToObject<float>(),
                    jArray[3].ToObject<float>());
            }

            // Handle System.Text.Json.JsonElement
            if (value is System.Text.Json.JsonElement jsonElement &&
                jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var arr = jsonElement.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                if (arr.Length >= 4)
                    return new Vector4(arr[0], arr[1], arr[2], arr[3]);
            }

            // Handle in-memory float[] (from direct ExportToolSettings)
            if (value is float[] floatArr && floatArr.Length >= 4)
            {
                return new Vector4(floatArr[0], floatArr[1], floatArr[2], floatArr[3]);
            }
        }
        catch
        {
            // Graceful fallback - return null on any conversion error
        }

        return null;
    }

    /// <summary>
    /// Imports a collection of values from various serialized formats.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="settings">The settings dictionary.</param>
    /// <param name="key">The key to look up.</param>
    /// <param name="converter">Function to convert individual elements.</param>
    /// <returns>A list of imported values, or null if not found.</returns>
    public static List<T>? ImportList<T>(
        Dictionary<string, object?>? settings,
        string key,
        Func<object, T?> converter) where T : struct
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return null;

        try
        {
            var result = new List<T>();

            // Handle Newtonsoft.Json JArray
            if (value is Newtonsoft.Json.Linq.JArray jArray)
            {
                foreach (var item in jArray)
                {
                    var converted = converter(item);
                    if (converted.HasValue)
                        result.Add(converted.Value);
                }
                return result;
            }

            // Handle IEnumerable<object>
            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null) continue;
                    var converted = converter(item);
                    if (converted.HasValue)
                        result.Add(converted.Value);
                }
                return result;
            }
        }
        catch
        {
            // Graceful fallback
        }

        return null;
    }

    /// <summary>
    /// Imports a HashSet of ulong values from various serialized formats.
    /// </summary>
    public static HashSet<ulong>? ImportUlongHashSet(Dictionary<string, object?>? settings, string key)
    {
        var list = ImportList<ulong>(settings, key, ConvertToUlong);
        return list != null ? new HashSet<ulong>(list) : null;
    }

    /// <summary>
    /// Imports a List of ulong values from various serialized formats.
    /// </summary>
    public static List<ulong>? ImportUlongList(Dictionary<string, object?>? settings, string key)
    {
        return ImportList<ulong>(settings, key, ConvertToUlong);
    }

    /// <summary>
    /// Imports a List of int values from various serialized formats.
    /// </summary>
    public static List<int>? ImportIntList(Dictionary<string, object?>? settings, string key)
    {
        return ImportList<int>(settings, key, ConvertToInt);
    }

    /// <summary>
    /// Converts an object to ulong, handling various numeric types and Newtonsoft.Json tokens.
    /// </summary>
    private static ulong? ConvertToUlong(object item)
    {
        if (item is ulong ul) return ul;
        if (item is long l) return (ulong)l;
        if (item is int i) return (ulong)i;
        if (item is Newtonsoft.Json.Linq.JValue jv) return jv.ToObject<ulong>();
        if (ulong.TryParse(item.ToString(), out var parsed)) return parsed;
        return null;
    }

    /// <summary>
    /// Converts an object to int, handling various numeric types and Newtonsoft.Json tokens.
    /// </summary>
    private static int? ConvertToInt(object item)
    {
        if (item is int i) return i;
        if (item is long l) return (int)l;
        if (item is Newtonsoft.Json.Linq.JValue jv) return jv.ToObject<int>();
        if (int.TryParse(item.ToString(), out var parsed)) return parsed;
        return null;
    }

    /// <summary>
    /// Gets a setting value with type conversion and default fallback.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="settings">The settings dictionary.</param>
    /// <param name="key">The key to look up.</param>
    /// <param name="defaultValue">The default value if not found.</param>
    /// <returns>The setting value or default.</returns>
    public static T GetSetting<T>(Dictionary<string, object?>? settings, string key, T defaultValue)
    {
        if (settings == null || !settings.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        try
        {
            // Handle Newtonsoft.Json JValue
            if (value is Newtonsoft.Json.Linq.JValue jValue)
            {
                return jValue.ToObject<T>() ?? defaultValue;
            }

            // Handle System.Text.Json.JsonElement
            if (value is System.Text.Json.JsonElement jsonElement)
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(jsonElement.GetRawText()) ?? defaultValue;
            }

            // Direct conversion
            if (value is T typedValue)
                return typedValue;

            // Try Convert.ChangeType for primitives
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}
