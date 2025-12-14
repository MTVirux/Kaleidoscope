using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Handles plugin chat commands.
/// </summary>
public sealed class CommandService : IDisposable
{
    private const string CommandMain = "/kld";
    private const string CommandFull = "/kaleidoscope";

    private readonly ICommandManager _commands;
    private readonly IPluginLog _log;
    private readonly WindowService _windowService;

    public CommandService(ICommandManager commands, IPluginLog log, WindowService windowService)
    {
        _commands = commands;
        _log = log;
        _windowService = windowService;

        Register();
    }

    private void Register()
    {
        try
        {
            _commands.AddHandler(CommandMain, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Kaleidoscope UI",
                ShowInHelp = true
            });

            _commands.AddHandler(CommandFull, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Kaleidoscope UI",
                ShowInHelp = true
            });

            _log.Debug($"Registered commands: {CommandMain}, {CommandFull}");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to register commands: {ex.Message}");
        }
    }

    private void OnCommand(string command, string args)
    {
        var trimmedArgs = args.Trim().ToLowerInvariant();

        switch (trimmedArgs)
        {
            case "config":
            case "settings":
                _windowService.OpenConfigWindow();
                break;
            default:
                _windowService.OpenMainWindow();
                break;
        }
    }

    public void Dispose()
    {
        try
        {
            _commands.RemoveHandler(CommandMain);
            _commands.RemoveHandler(CommandFull);
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to unregister commands: {ex.Message}");
        }
    }
}
