using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// A tool that displays submersible voyage status with precise timers.
/// </summary>
public class SubmersibleVentureStatusTool : VentureStatusToolBase
{
    public override string ToolName => "Submersible Voyage Status";
    
    protected override string EntityNameSingular => "Submersible";
    protected override string EntityNamePlural => "Submersibles";
    protected override string VentureNameSingular => "Voyage";
    protected override string HiddenEntitiesSettingsKey => "HiddenSubmersibles";
    protected override string NoVentureColorSettingsKey => "NoVoyageColor";

    public SubmersibleVentureStatusTool(AutoRetainerIpcService? autoRetainerIpc = null, ConfigurationService? configService = null)
        : base(autoRetainerIpc, configService)
    {
        Title = "Submersible Voyages";
    }

    protected override IEnumerable<IVentureEntity> GetEntities(AutoRetainerCharacterData character)
    {
        // Filter to only submersibles (not airships)
        return character.Vessels.Where(v => v.IsSubmersible).Select(v => (IVentureEntity)new VesselVoyageAdapter(v));
    }
}
