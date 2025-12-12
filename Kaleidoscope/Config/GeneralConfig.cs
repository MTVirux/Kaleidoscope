namespace Kaleidoscope.Config
{
    public class GeneralConfig
    {
        public bool ShowOnStart { get; set; } = true;
        public bool ExclusiveFullscreen { get; set; } = false;
        
        // Grid cell percent defaults for content container
        public float ContentGridCellWidthPercent { get; set; } = 25f;
        public float ContentGridCellHeightPercent { get; set; } = 25f;
    }
}
