using Kaleidoscope.Config;

namespace Kaleidoscope.Interfaces;

public interface IConfigurationService
{
    Configuration Config { get; }
    ConfigManager ConfigManager { get; }
    GeneralConfig GeneralConfig { get; }
    CurrencyTrackerConfig CurrencyTrackerConfig { get; }
    WindowConfig WindowConfig { get; }

    void Save();
    void SaveLayouts();
}
