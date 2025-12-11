namespace Kaleidoscope
{
    using System;
    using Dalamud.Interface.Windowing;
    using Dalamud.Plugin;

    public sealed class KaleidoscopePlugin : IDalamudPlugin, IDisposable
    {
        public string Name => "Crystal Terror";

        public Kaleidoscope.Configuration Config { get; private set; }

        private readonly IDalamudPluginInterface pluginInterface;
        private readonly WindowSystem windowSystem;
        private readonly Kaleidoscope.Gui.MainWindow.MainWindow mainWindow;
        private readonly Kaleidoscope.Gui.ConfigWindow.ConfigWindow configWindow;

        public KaleidoscopePlugin(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));

            var cfg = this.pluginInterface.GetPluginConfig() as Kaleidoscope.Configuration;
            if (cfg == null)
            {
                cfg = new Kaleidoscope.Configuration();
                this.pluginInterface.SavePluginConfig(cfg);
            }
            this.Config = cfg;

            this.windowSystem = new WindowSystem("Kaleidoscope");
            this.mainWindow = new Kaleidoscope.Gui.MainWindow.MainWindow();
            this.configWindow = new Kaleidoscope.Gui.ConfigWindow.ConfigWindow();

            this.windowSystem.AddWindow(this.mainWindow);
            this.windowSystem.AddWindow(this.configWindow);

            // Open the main window by default when the plugin loads
            this.mainWindow.IsOpen = true;

            this.pluginInterface.UiBuilder.Draw += this.DrawUi;
            this.pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
            this.pluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;
        }

        public void Dispose()
        {
            this.pluginInterface.UiBuilder.Draw -= this.DrawUi;
            this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
            this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;
            this.windowSystem.RemoveAllWindows();
            if (this.mainWindow is IDisposable mw)
                mw.Dispose();

            if (this.configWindow is IDisposable cw)
                cw.Dispose();
        }

        private void DrawUi() => this.windowSystem.Draw();

        private void OpenConfigUi() => this.configWindow.IsOpen = true;

        private void OpenMainUi() => this.mainWindow.IsOpen = true;
    }
}
