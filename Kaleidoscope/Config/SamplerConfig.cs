namespace Kaleidoscope.Config;

/// <summary>
/// Configuration for the data sampling service.
/// </summary>
public class SamplerConfig
{
    /// <summary>Whether the sampler is enabled.</summary>
    public bool SamplerEnabled { get; set; } = true;

    /// <summary>Sampling interval in milliseconds.</summary>
    public int SamplerIntervalMs { get; set; } = 1000;
}
