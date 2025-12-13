namespace Kaleidoscope.Interfaces
{
    using Kaleidoscope;
    using Kaleidoscope.Config;

    public interface IConfigurationService
    {
        Configuration Config { get; }
        ConfigManager ConfigManager { get; }
        GeneralConfig GeneralConfig { get; }
        SamplerConfig SamplerConfig { get; }
        WindowConfig WindowConfig { get; }

        void Save();
        void SaveLayouts();
    }
}
