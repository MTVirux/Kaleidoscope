using Dalamud.Interface.Colors;
using ECommons;
using ECommons.Logging;
using NightmareUI.PrimaryUI;
using OtterGui.Filesystem;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace NightmareUI.OtterGuiWrapper.FileSystems.Configuration;
public class ConfigFileSystemEntry
{
		internal Func<string> Filter = null!;
		public virtual NuiBuilder? Builder { get; init; }
		public virtual int DisplayPriority { get; init; } = 0;

		public virtual bool ShouldHighlight()
		{
				if (Builder == null) return false;
				return Builder.ShouldHighlight;
		}

		public virtual bool ShouldDisplay()
		{
				return true;
		}

		public virtual Vector4? GetColor()
		{
				if (Filter().IsNullOrEmpty() == false)
				{
						if (this.Path.SplitDirectories().Last().Contains(Filter(), StringComparison.OrdinalIgnoreCase)) return ImGuiColors.ParsedGreen;
						return ShouldHighlight() ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudGrey3;
				}
				return null;
		}

		public virtual string Path
		{
				get
				{
						PluginLog.Error($"You must override Path property of ConfigFileSystemEntry ({this.GetType().FullName})");
						throw new NotImplementedException("You must override Path property of ConfigFileSystemEntry");
				}
		}

		public virtual bool NoFrame { get; set; } = false;

		public virtual void Draw()
		{
				if(Builder != null)
				{
						Builder.Filter = Filter();
						Builder.Draw();
						return;
				}
				PluginLog.Error($"You must either override Draw method of ConfigFileSystemEntry or assign non-null Builder to it ({this.GetType().FullName}");
				throw new NotImplementedException("You must override Draw method of ConfigFileSystemEntry");
		}
}
