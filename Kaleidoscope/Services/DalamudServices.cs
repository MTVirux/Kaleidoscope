using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Registers all Dalamud services with the service manager.
/// </summary>
public static class DalamudServices
{
    public static void AddServices(ServiceManager services, IDalamudPluginInterface pi)
    {
        services.AddExistingService(pi);
        services.AddExistingService(pi.UiBuilder);
        services.AddDalamudService<ICommandManager>(pi);
        services.AddDalamudService<IClientState>(pi);
        services.AddDalamudService<IFramework>(pi);
        services.AddDalamudService<IPluginLog>(pi);
        services.AddDalamudService<IChatGui>(pi);
        services.AddDalamudService<IGameGui>(pi);
        services.AddDalamudService<ICondition>(pi);
        services.AddDalamudService<IObjectTable>(pi);
        services.AddDalamudService<ITextureProvider>(pi);
    }
}
