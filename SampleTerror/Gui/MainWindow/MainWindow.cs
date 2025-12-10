namespace CrystalTerror.Gui.MainWindow
{
	using Dalamud.Interface.Windowing;
	using ImGui = Dalamud.Bindings.ImGui.ImGui;

		public class MainWindow : Window
	{
		public MainWindow() : base("CrystalTerror")
		{
			Size = new System.Numerics.Vector2(400, 300);
		}

		public override void Draw()
		{
			ImGui.Begin("CrystalTerror");
			ImGui.TextUnformatted("Main UI");
			ImGui.End();
		}
	}
}
