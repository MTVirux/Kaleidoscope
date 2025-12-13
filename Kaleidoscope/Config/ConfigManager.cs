using Newtonsoft.Json;
using System.IO;
using Kaleidoscope.Services;

namespace Kaleidoscope.Config
{
    public class ConfigManager
    {
        private readonly string folder;

        public ConfigManager(string pluginConfigDirectory)
        {
            if (string.IsNullOrEmpty(pluginConfigDirectory)) throw new ArgumentNullException(nameof(pluginConfigDirectory));
            folder = pluginConfigDirectory;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }

        private string FilePath(string name) => Path.Combine(folder, name);

        public T LoadOrCreate<T>(string fileName, Func<T> factory) where T : class
        {
            var fp = FilePath(fileName);
            try
            {
                if (File.Exists(fp))
                {
                    var txt = File.ReadAllText(fp);
                    var obj = JsonConvert.DeserializeObject<T>(txt);
                    if (obj != null) return obj;
                }
            }
            catch (Exception ex) { LogService.Warning($"[ConfigManager] Failed to load config '{fileName}', using default: {ex.Message}"); }
            var def = factory();
            Save(fileName, def);
            return def;
        }

        public void Save<T>(string fileName, T obj) where T : class
        {
            try
            {
                var fp = FilePath(fileName);
                var txt = JsonConvert.SerializeObject(obj, Formatting.Indented);
                File.WriteAllText(fp, txt);
            }
            catch (Exception ex) { LogService.Error($"[ConfigManager] Failed to save config '{fileName}': {ex.Message}", ex); }
        }
    }
}
