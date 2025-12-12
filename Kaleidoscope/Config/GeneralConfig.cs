namespace Kaleidoscope.Config
{
    public class GeneralConfig
    {
        public bool ShowOnStart { get; set; } = true;
        public bool ExclusiveFullscreen { get; set; } = false;
        
        // Grid cell percent defaults for content container
        public float ContentGridCellWidthPercent { get; set; } = 25f;
        public float ContentGridCellHeightPercent { get; set; } = 25f;
        // Persist the edit-mode toggle so users can keep it between sessions
        public bool EditMode { get; set; } = false;
    }
}
