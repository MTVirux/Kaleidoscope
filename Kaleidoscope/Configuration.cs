using Dalamud.Configuration;

namespace CrystalTerror
{
        public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        // Keep a single simple setting to control whether the UI opens on start.
        public bool ShowOnStart { get; set; } = true;
        
        // Window pin states used by the UI lock button component.
        public bool PinMainWindow { get; set; } = false;
        public bool PinConfigWindow { get; set; } = false;
    }
}
