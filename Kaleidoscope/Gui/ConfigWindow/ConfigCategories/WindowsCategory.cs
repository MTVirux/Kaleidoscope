using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Windows configuration category in the config window.
/// Controls window pinning/locking behavior.
/// </summary>
public class WindowsCategory
{
    private readonly Kaleidoscope.Configuration config;
    private readonly Action saveConfig;
    private readonly StateService? _stateService;

    public WindowsCategory(Kaleidoscope.Configuration config, Action saveConfig, StateService? stateService = null)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this._stateService = stateService;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Windows");
        ImGui.Separator();

        // Use StateService for main window pin state if available
        var pinMain = _stateService?.IsLocked ?? this.config.PinMainWindow;
        if (ImGui.Checkbox("Pin main window", ref pinMain))
        {
            if (_stateService != null)
            {
                _stateService.IsLocked = pinMain;
            }
            else
            {
                this.config.PinMainWindow = pinMain;
                this.saveConfig();
            }
        }

        var pinConfig = this.config.PinConfigWindow;
        if (ImGui.Checkbox("Pin config window", ref pinConfig))
        {
            this.config.PinConfigWindow = pinConfig;
            this.saveConfig();
        }
    }
}
