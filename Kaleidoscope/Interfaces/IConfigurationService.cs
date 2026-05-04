using Kaleidoscope.Config;

namespace Kaleidoscope.Interfaces;

public interface IConfigurationService
{
    Configuration Config { get; }
    ConfigManager ConfigManager { get; }

    void Save();
    void SaveLayouts();
}
