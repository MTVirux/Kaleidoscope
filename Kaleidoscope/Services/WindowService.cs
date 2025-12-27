using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.ConfigWindow;
using Kaleidoscope.Gui.MainWindow;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Manages the plugin's window system and lifecycle.
/// </summary>
/// <remarks>
/// This follows the Glamourer pattern for window management, using Dalamud's
/// WindowSystem with event-based drawing and state management.
/// </remarks>
public sealed class WindowService : IDisposable, IRequiredService
{
    private readonly IPluginLog _log;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ConfigurationService _configService;
    private readonly StateService _stateService;
    private readonly WindowSystem _windowSystem;
    private readonly MainWindow _mainWindow;
    private readonly FullscreenWindow _fullscreenWindow;
    private readonly ConfigWindow _configWindow;

    public WindowService(
        IPluginLog log,
        IDalamudPluginInterface pluginInterface,
        IUiBuilder uiBuilder,
        ConfigurationService configService,
        StateService stateService,
        MainWindow mainWindow,
        FullscreenWindow fullscreenWindow,
        ConfigWindow configWindow)
    {
        _log = log;
        _pluginInterface = pluginInterface;
        _configService = configService;
        _stateService = stateService;
        _mainWindow = mainWindow;
        _fullscreenWindow = fullscreenWindow;
        _configWindow = configWindow;

        _mainWindow.SetWindowService(this);
        _fullscreenWindow.SetWindowService(this);

        _windowSystem = new WindowSystem("Kaleidoscope");

        RegisterWindows();
        AttachEvents(uiBuilder);
        ApplyInitialWindowState();

        _log.Debug("WindowService initialized");
    }

    private void RegisterWindows()
    {
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_fullscreenWindow);
        _windowSystem.AddWindow(_configWindow);
    }

    private void AttachEvents(IUiBuilder uiBuilder)
    {
        uiBuilder.Draw += Draw;
        uiBuilder.OpenConfigUi += OpenConfigWindow;
        uiBuilder.OpenMainUi += OpenMainWindow;
    }

    private void DetachEvents(IUiBuilder uiBuilder)
    {
        uiBuilder.Draw -= Draw;
        uiBuilder.OpenConfigUi -= OpenConfigWindow;
        uiBuilder.OpenMainUi -= OpenMainWindow;
    }

    private void ApplyInitialWindowState()
    {
        var config = _configService.Config;

        if (config.ShowOnStart)
        {
            if (config.ExclusiveFullscreen)
            {
                _fullscreenWindow.IsOpen = true;
                _mainWindow.IsOpen = false;
                _stateService.IsFullscreen = true;
            }
            else
            {
                _mainWindow.IsOpen = true;
                _fullscreenWindow.IsOpen = false;
                _stateService.IsFullscreen = false;
            }
        }
        else
        {
            _mainWindow.IsOpen = false;
            _fullscreenWindow.IsOpen = false;
            _stateService.IsFullscreen = false;
        }
    }

    private void Draw() => _windowSystem.Draw();

    public void OpenMainWindow() => _mainWindow.IsOpen = true;
    public void OpenConfigWindow() => _configWindow.IsOpen = true;
    public void OpenLayoutsConfig() => _configWindow.OpenToTab(ConfigWindow.TabIndex.Layouts);

    public void RequestShowFullscreen()
    {
        _mainWindow.IsOpen = false;
        _fullscreenWindow.IsOpen = true;
        _stateService.EnterFullscreen();
    }

    public void RequestExitFullscreen()
    {
        _fullscreenWindow.IsOpen = false;
        _stateService.ExitFullscreen();

        if (!_configService.Config.ExclusiveFullscreen)
        {
            _mainWindow.IsOpen = true;
            _mainWindow.ExitFullscreen();
        }
    }

    public void ApplyLayout(string name) => _mainWindow.ApplyLayoutByName(name);

    public void Dispose()
    {
        DetachEvents(_pluginInterface.UiBuilder);
        _windowSystem.RemoveAllWindows();
        _mainWindow?.Dispose();
    }
}
