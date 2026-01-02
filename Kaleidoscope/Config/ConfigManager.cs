using Newtonsoft.Json;
using Kaleidoscope.Services;

namespace Kaleidoscope.Config;

/// <summary>
/// Manages JSON configuration files in the plugin config directory.
/// </summary>
public class ConfigManager
{
    private readonly string _folder;

    public ConfigManager(string pluginConfigDirectory)
    {
        _folder = pluginConfigDirectory ?? throw new ArgumentNullException(nameof(pluginConfigDirectory));
        if (!Directory.Exists(_folder))
            Directory.CreateDirectory(_folder);
    }

    private string FilePath(string name) => Path.Combine(_folder, name);

    public T LoadOrCreate<T>(string fileName, Func<T> factory) where T : class
    {
        var filePath = FilePath(fileName);
        try
        {
            if (File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                var obj = JsonConvert.DeserializeObject<T>(text);
                if (obj != null) return obj;
            }
        }
        catch (Exception ex)
        {
            LogService.Warning(LogCategory.Config, $"Failed to load config '{fileName}', using default: {ex.Message}");
        }

        var defaultValue = factory();
        Save(fileName, defaultValue);
        return defaultValue;
    }

    public void Save<T>(string fileName, T obj) where T : class
    {
        try
        {
            var filePath = FilePath(fileName);
            var text = JsonConvert.SerializeObject(obj, Formatting.Indented);
            File.WriteAllText(filePath, text);
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.Config, $"Failed to save config '{fileName}': {ex.Message}", ex);
        }
    }
}
