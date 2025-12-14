namespace Kaleidoscope
{
    using System.Numerics;

    /// <summary>
    /// Static configuration values that don't change at runtime.
    /// These are compile-time constants for UI layout and other fixed settings.
    /// </summary>
    public static class ConfigStatic
    {
        #region Window Defaults

        /// <summary>Default window position when first opened.</summary>
        public static readonly Vector2 DefaultWindowPosition = new(100, 100);

        /// <summary>Default window size when first opened.</summary>
        public static readonly Vector2 DefaultWindowSize = new(800, 600);

        /// <summary>Minimum allowed window size.</summary>
        public static readonly Vector2 MinimumWindowSize = new(250,180);

        #endregion

        #region GilTracker Defaults

        /// <summary>Maximum number of in-memory samples to display in the plot.</summary>
        public const int GilTrackerMaxSamples = 200;

        /// <summary>Starting value for the gil tracker when no data exists.</summary>
        public const float GilTrackerStartingValue = 100000f;

        /// <summary>Maximum gil value for Y-axis scaling (999,999,999 gil cap).</summary>
        public const float GilTrackerMaxGil = 999_999_999f;

        /// <summary>Default size for the GilTracker tool window.</summary>
        public static readonly Vector2 GilTrackerToolSize = new(360, 220);

        /// <summary>Default size for viewing data points popup.</summary>
        public static readonly Vector2 GilTrackerPointsPopupSize = new(700, 300);

        #endregion

        #region Sampler Defaults

        /// <summary>Default sampling interval in milliseconds.</summary>
        public const int DefaultSamplerIntervalMs = 1000;

        /// <summary>Minimum allowed sampling interval in milliseconds.</summary>
        public const int MinSamplerIntervalMs = 1000;

        #endregion

        #region UI Constants

        /// <summary>Epsilon for floating-point comparisons.</summary>
        public const float FloatEpsilon = 0.0001f;

        /// <summary>Maximum buffer size for text input fields.</summary>
        public const int TextInputBufferSize = 128;

        /// <summary>Maximum delta for drag/resize operations.</summary>
        public const float MaxDragDelta = 2000f;

        /// <summary>Maximum grid lines per axis.</summary>
        public const int MaxGridLines = 1024;

        #endregion
    }
}
