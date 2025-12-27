using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// A tool that displays retainer venture status with precise timers.
/// </summary>
public class RetainerVentureStatusTool : VentureStatusToolBase
{
    public override string ToolName => "Retainer Venture Status";
    
    protected override string EntityNameSingular => "Retainer";
    protected override string EntityNamePlural => "Retainers";
    protected override string VentureNameSingular => "Venture";
    protected override string HiddenEntitiesSettingsKey => "HiddenRetainers";
    protected override string NoVentureColorSettingsKey => "NoVentureColor";

    public RetainerVentureStatusTool(AutoRetainerIpcService? autoRetainerIpc = null, ConfigurationService? configService = null)
        : base(autoRetainerIpc, configService)
    {
        Title = "Retainer Ventures";
    }

    protected override IEnumerable<IVentureEntity> GetEntities(AutoRetainerCharacterData character)
    {
        return character.Retainers.Select(r => (IVentureEntity)new RetainerVentureAdapter(r));
    }
}
