namespace Kaleidoscope.Config;

/// <summary>
/// Configuration for the data sampling service.
/// </summary>
public class SamplerConfig
{
    public bool SamplerEnabled { get; set; } = false;
    public int SamplerIntervalMs { get; set; } = 1000;
}
