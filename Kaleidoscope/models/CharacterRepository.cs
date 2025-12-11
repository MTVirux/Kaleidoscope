using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kaleidoscope.models
{
    public static class CharacterRepository
    {
        /// <summary>
        /// Default storage folder (under the running app base directory): `models/characters`.
        /// </summary>
        public static string DefaultFolder()
        {
            var baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            return Path.Combine(baseDir, "models", "characters");
        }

        public static string Save(CharacterModel model, string folder = null, JsonSerializerOptions options = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            folder ??= DefaultFolder();
            Directory.CreateDirectory(folder);

            var safeName = string.IsNullOrEmpty(model.Name) ? "unknown" : MakeSafeFilename(model.Name);
            var filename = $"{safeName}_{model.ContentId}_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            var path = Path.Combine(folder, filename);

            options ??= new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(model, options);
            File.WriteAllText(path, json);
            return path;
        }

        public static CharacterModel Load(string path, JsonSerializerOptions options = null)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            options ??= new JsonSerializerOptions();
            return JsonSerializer.Deserialize<CharacterModel>(json, options);
        }

        public static CharacterModel LoadMostRecent(string folder = null, JsonSerializerOptions options = null)
        {
            folder ??= DefaultFolder();
            if (!Directory.Exists(folder)) return null;
            var fi = new DirectoryInfo(folder)
                .GetFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            return fi == null ? null : Load(fi.FullName, options);
        }

        public static string[] ListSavedFiles(string folder = null)
        {
            folder ??= DefaultFolder();
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            return Directory.GetFiles(folder, "*.json");
        }

        private static string MakeSafeFilename(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
