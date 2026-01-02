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
    private readonly ConfigWindow _configWindow;
    private readonly IUiBuilder _uiBuilder;

    public WindowService(
        IPluginLog log,
        IDalamudPluginInterface pluginInterface,
        IUiBuilder uiBuilder,
        ConfigurationService configService,
        StateService stateService,
        MainWindow mainWindow,
        ConfigWindow configWindow)
    {
        _log = log;
        _pluginInterface = pluginInterface;
        _configService = configService;
        _stateService = stateService;
        _mainWindow = mainWindow;
        _configWindow = configWindow;
        _uiBuilder = uiBuilder;

        _mainWindow.SetWindowService(this);

        _windowSystem = new WindowSystem("Kaleidoscope");

        RegisterWindows();
        AttachEvents(uiBuilder);
        ApplyInitialWindowState();
        
        // Subscribe to fullscreen state changes to update UI hide settings
        _stateService.OnFullscreenChanged += OnFullscreenChanged;

        LogService.Debug(LogCategory.UI, "WindowService initialized");
    }

    private void RegisterWindows()
    {
        _windowSystem.AddWindow(_mainWindow);
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
            _mainWindow.IsOpen = true;
            
            // If exclusive fullscreen is configured, MainWindow.PreDraw will handle entering fullscreen mode
            if (config.ExclusiveFullscreen)
            {
                _stateService.IsFullscreen = true;
                UpdateUiHideSettings(true);
            }
            else
            {
                _stateService.IsFullscreen = false;
                UpdateUiHideSettings(false);
            }
        }
        else
        {
            _mainWindow.IsOpen = false;
            _stateService.IsFullscreen = false;
            UpdateUiHideSettings(false);
        }
    }
    
    private void OnFullscreenChanged(bool isFullscreen)
    {
        UpdateUiHideSettings(isFullscreen);
    }
    
    private void UpdateUiHideSettings(bool isFullscreen)
    {
        // When in fullscreen mode, prevent the UI from hiding during cutscenes and gpose
        _uiBuilder.DisableCutsceneUiHide = isFullscreen;
        _uiBuilder.DisableGposeUiHide = isFullscreen;
    }

    private void Draw() => _windowSystem.Draw();

    public void OpenMainWindow() => _mainWindow.IsOpen = true;
    public void OpenConfigWindow() => _configWindow.IsOpen = true;
    public void OpenLayoutsConfig() => _configWindow.OpenToTab(ConfigWindow.TabIndex.Layouts);

    /// <summary>
    /// Requests entering fullscreen mode.
    /// </summary>
    public void RequestShowFullscreen()
    {
        _mainWindow.EnterFullscreenMode();
    }

    /// <summary>
    /// Requests exiting fullscreen mode.
    /// </summary>
    public void RequestExitFullscreen()
    {
        _mainWindow.ExitFullscreenMode();
    }

    public void ApplyLayout(string name) => _mainWindow.ApplyLayoutByName(name);

    public void Dispose()
    {
        _stateService.OnFullscreenChanged -= OnFullscreenChanged;
        UpdateUiHideSettings(false); // Reset to default behavior on dispose
        DetachEvents(_pluginInterface.UiBuilder);
        _windowSystem.RemoveAllWindows();
        _mainWindow?.Dispose();
    }
}
