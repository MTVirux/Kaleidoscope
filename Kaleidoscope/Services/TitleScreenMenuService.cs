using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Adds a Kaleidoscope button to the FFXIV title screen menu,
/// allowing the main window to be opened before logging in.
/// </summary>
public sealed class TitleScreenMenuService : IDisposable, IRequiredService
{
    private readonly ITitleScreenMenu _titleScreenMenu;
    private readonly ITextureProvider _textureProvider;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _log;
    private readonly WindowService _windowService;

    private IReadOnlyTitleScreenMenuEntry? _menuEntry;

    public TitleScreenMenuService(
        ITitleScreenMenu titleScreenMenu,
        ITextureProvider textureProvider,
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        WindowService windowService)
    {
        _titleScreenMenu = titleScreenMenu;
        _textureProvider = textureProvider;
        _pluginInterface = pluginInterface;
        _log = log;
        _windowService = windowService;

        CreateEntry();

        LogService.Debug(LogCategory.UI, "TitleScreenMenuService initialized");
    }

    private void CreateEntry()
    {
        try
        {
            // Title screen menu requires a 64x64 texture
            // Use the icon.png file from the plugin directory
            var iconPath = Path.Combine(_pluginInterface.AssemblyLocation.DirectoryName!, "icon.png");

            if (!File.Exists(iconPath))
            {
                LogService.Warning(LogCategory.UI, $"Title screen icon not found at {iconPath}, skipping title screen menu entry");
                return;
            }

            var icon = _textureProvider.GetFromFile(iconPath);
            _menuEntry = _titleScreenMenu.AddEntry("Kaleidoscope", icon, OnMenuEntryClicked);
            LogService.Debug(LogCategory.UI, "Title screen menu entry added");
        }
        catch (Exception ex)
        {
            LogService.Error(LogCategory.UI, $"Failed to add title screen menu entry: {ex}");
        }
    }

    private void RemoveEntry()
    {
        if (_menuEntry != null)
        {
            try
            {
                _titleScreenMenu.RemoveEntry(_menuEntry);
                _menuEntry = null;
                LogService.Debug(LogCategory.UI, "Title screen menu entry removed");
            }
            catch (Exception ex)
            {
                LogService.Error(LogCategory.UI, $"Failed to remove title screen menu entry: {ex}");
            }
        }
    }

    private void OnMenuEntryClicked()
    {
        _windowService.OpenMainWindow();
    }

    public void Dispose()
    {
        RemoveEntry();
    }
}
