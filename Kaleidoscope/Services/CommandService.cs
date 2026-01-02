using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Handles plugin chat commands.
/// </summary>
/// <remarks>
/// Follows the Glamourer pattern for command handling with separate handlers for
/// main and config commands.
/// </remarks>
public sealed class CommandService : IDisposable, IRequiredService
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

            LogService.Debug(LogCategory.UI, $"Registered commands: {CommandMain}, {CommandFull}");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, $"Failed to register commands: {ex.Message}");
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
            LogService.Warning(LogCategory.UI, $"Failed to unregister commands: {ex.Message}");
        }
    }
}
