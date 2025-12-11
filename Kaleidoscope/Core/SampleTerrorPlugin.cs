namespace CrystalTerror
{
    using System;
    using Dalamud.Interface.Windowing;
    using Dalamud.Plugin;

    public sealed class CrystalTerrorPlugin : IDalamudPlugin, IDisposable
    {
        public string Name => "Crystal Terror";

        public Configuration Config { get; private set; }

        private readonly IDalamudPluginInterface pluginInterface;
        private readonly WindowSystem windowSystem;
        private readonly Gui.MainWindow.MainWindow mainWindow;
        private readonly Gui.ConfigWindow.ConfigWindow configWindow;

        public CrystalTerrorPlugin(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));

            var cfg = this.pluginInterface.GetPluginConfig() as Configuration;
            if (cfg == null)
            {
                cfg = new Configuration();
                this.pluginInterface.SavePluginConfig(cfg);
            }
            this.Config = cfg;

            this.windowSystem = new WindowSystem("CrystalTerror");
            this.mainWindow = new Gui.MainWindow.MainWindow();
            this.configWindow = new Gui.ConfigWindow.ConfigWindow();

            this.windowSystem.AddWindow(this.mainWindow);
            this.windowSystem.AddWindow(this.configWindow);

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
