using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.AutoRetainer;

/// <summary>
/// A tool that displays retainer venture status with precise timers.
/// </summary>
[ToolType("RetainerVentureStatus", "Ventures Status", "AutoRetainer", "Displays retainer venture timers with millisecond precision")]
public sealed class RetainerVentureStatusTool : VentureStatusToolBase
{
    public override string ToolName => "Retainer Venture Status";
    
    protected override string EntityNameSingular => "Retainer";
    protected override string EntityNamePlural => "Retainers";
    protected override string VentureNameSingular => "Venture";
    protected override string HiddenEntitiesSettingsKey => "HiddenRetainers";
    protected override string NoVentureColorSettingsKey => "NoVentureColor";

    public RetainerVentureStatusTool(AutoRetainerService? autoRetainerIpc = null, ConfigurationService? configService = null)
        : base(autoRetainerIpc, configService)
    {
        Title = "Retainer Ventures";
    }

    protected override IEnumerable<IVentureEntity> GetEntities(AutoRetainerCharacterData character)
    {
        return character.Retainers.Select(r => (IVentureEntity)new RetainerVentureAdapter(r));
    }
}
