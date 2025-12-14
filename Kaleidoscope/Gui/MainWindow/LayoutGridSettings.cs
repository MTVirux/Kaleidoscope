namespace Kaleidoscope.Gui.MainWindow
{
    /// <summary>
    /// Represents the grid settings for a layout, including resolution and calculation mode.
    /// </summary>
    public class LayoutGridSettings
    {
        /// <summary>
        /// When true, grid resolution is calculated from aspect ratio * GridResolutionMultiplier.
        /// When false, Columns and Rows are used directly.
        /// </summary>
        public bool AutoAdjustResolution { get; set; } = true;
        
        /// <summary>Number of columns in the grid (used when AutoAdjustResolution is false).</summary>
        public int Columns { get; set; } = 16;
        
        /// <summary>Number of rows in the grid (used when AutoAdjustResolution is false).</summary>
        public int Rows { get; set; } = 9;
        
        /// <summary>
        /// Grid resolution multiplier (1-10). When AutoAdjustResolution is true:
        /// Columns = AspectRatioWidth * GridResolutionMultiplier
        /// Rows = AspectRatioHeight * GridResolutionMultiplier
        /// </summary>
        public int GridResolutionMultiplier { get; set; } = 2;
        
        /// <summary>
        /// Creates a copy of this settings instance.
        /// </summary>
        public LayoutGridSettings Clone()
        {
            return new LayoutGridSettings
            {
                AutoAdjustResolution = AutoAdjustResolution,
                Columns = Columns,
                Rows = Rows,
                GridResolutionMultiplier = GridResolutionMultiplier
            };
        }
        
        /// <summary>
        /// Copies values from another settings instance.
        /// </summary>
        public void CopyFrom(LayoutGridSettings other)
        {
            AutoAdjustResolution = other.AutoAdjustResolution;
            Columns = other.Columns;
            Rows = other.Rows;
            GridResolutionMultiplier = other.GridResolutionMultiplier;
        }
        
        /// <summary>
        /// Gets the effective number of columns based on the current settings and aspect ratio.
        /// </summary>
        public int GetEffectiveColumns(float aspectWidth = 16f, float aspectHeight = 9f)
        {
            if (AutoAdjustResolution)
            {
                return (int)(aspectWidth * GridResolutionMultiplier);
            }
            return System.Math.Max(1, Columns);
        }
        
        /// <summary>
        /// Gets the effective number of rows based on the current settings and aspect ratio.
        /// </summary>
        public int GetEffectiveRows(float aspectWidth = 16f, float aspectHeight = 9f)
        {
            if (AutoAdjustResolution)
            {
                return (int)(aspectHeight * GridResolutionMultiplier);
            }
            return System.Math.Max(1, Rows);
        }
        
        /// <summary>
        /// Loads settings from a ContentLayoutState.
        /// </summary>
        public static LayoutGridSettings FromLayoutState(ContentLayoutState? layout)
        {
            if (layout == null)
            {
                return new LayoutGridSettings();
            }
            
            return new LayoutGridSettings
            {
                AutoAdjustResolution = layout.AutoAdjustResolution,
                Columns = layout.Columns,
                Rows = layout.Rows,
                GridResolutionMultiplier = layout.GridResolutionMultiplier
            };
        }
        
        /// <summary>
        /// Applies these settings to a ContentLayoutState.
        /// </summary>
        public void ApplyToLayoutState(ContentLayoutState layout)
        {
            if (layout == null) return;
            
            layout.AutoAdjustResolution = AutoAdjustResolution;
            layout.Columns = Columns;
            layout.Rows = Rows;
            layout.GridResolutionMultiplier = GridResolutionMultiplier;
        }
    }
}
