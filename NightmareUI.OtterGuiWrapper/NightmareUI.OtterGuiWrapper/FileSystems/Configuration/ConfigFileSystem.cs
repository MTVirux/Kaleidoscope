using OtterGui.Filesystem;
using OtterGui.Raii;
using Dalamud.Interface.Colors;
using ECommons.DalamudServices;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using ECommons;
using Newtonsoft.Json.Linq;
using System.Linq;
using Newtonsoft.Json;
using System.Numerics;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using NightmareUI.PrimaryUI;

#pragma warning disable CS8618
#pragma warning disable

namespace NightmareUI.OtterGuiWrapper.FileSystems.Configuration;
public sealed class ConfigFileSystem: FileSystem<ConfigFileSystemEntry>
{
		public FileSystemSelector Selector { get; private set; }
		public ICollection<ConfigFileSystemEntry> DataStorage { get; private set; }
		private Func<ICollection<ConfigFileSystemEntry>> GetDataAction;
		public Func<ConfigFileSystemEntry> DefaultValueSelector;
		public Action DrawWhenNoSelection { get; set; } = null;

    public ConfigFileSystem(Func<ICollection<ConfigFileSystemEntry>> getDataAction)
		{
				this.DefaultValueSelector = () => DataStorage.First();
        this.GetDataAction = getDataAction;
				try
				{
						Reload();
				}
				catch (Exception e)
				{
						e.Log();
				}
				var types = ECommonsMain.Instance.GetType().Assembly.GetTypes().Where(x => !x.IsInterface && !x.IsAbstract && x.IsAssignableTo(typeof(ConfigFileSystemEntry)));
				var dataTypes = DataStorage.Select(x => x.GetType());
				foreach(var type in types)
				{
						if(!dataTypes.Contains(type))
						{
								//PluginLog.Warning($"{type} was not found in ConfigFileSystem's Data, did you forgot to create an instance of it?");
						}
				}
		}

		private string SelectedPath;

		public void Reload()
		{
				if(Selector != null)
				{
						SelectedPath = Selector.Selected?.Path;
				}
				DataStorage = GetDataAction().Where(x => x.ShouldDisplay()).ToArray();
				var identifiers = new Dictionary<string, Dictionary<string, string>>()
				{
						["Data"] = DataStorage.ToDictionary(x => x.Path, x => x.Path)
				};
				//PluginLog.Information($"{JsonConvert.SerializeObject(identifiers)}");
				this.Load(JObject.Parse(JsonConvert.SerializeObject(identifiers)), DataStorage, (x) => x.Path, (x) => x.Path);
				Selector = new(this);
				//PluginLog.Information($"Selected: {SelectedPath}");
				if (SelectedPath != null)
				{
						var value = DataStorage.FirstOrDefault(x => x.Path == SelectedPath);
						if (value != null) Selector.SelectByValue(value);
				}
				foreach(var x in DataStorage)
				{
						x.Filter = () => Selector.Filter;
						try
						{
								if (x.Path.IsNullOrEmpty()) PluginLog.Error($"Item {x.GetType().FullName} has it's path null or empty");
								PluginLog.Verbose($"Item {x.GetType().FullName} pat {x.Path}");
						}
						catch(Exception e)
						{
								PluginLog.Error($"Item {x.GetType().FullName} does not implements path");
						}
				}
		}

		public void Draw(float? width = null)
		{
				foreach(var x in DataStorage)
				{
						if(x.Builder != null)
						{
								x.Builder.Filter = Selector.Filter;
						}
				}
				if(width == null)
				{
						width = 0f;
						foreach(var x in DataStorage)
						{
								var splitPath = x.Path.SplitDirectories();
								for (int i = 0; i < splitPath.Length; i++)
								{
										var newWidth = ImGui.CalcTextSize(splitPath[i]).X + (i + 1) * 25f + ImGui.GetStyle().ScrollbarSize;
										if (newWidth > width.Value) width = newWidth;
								}
						}
				}
				Selector.Draw(width.Value);
				ImGui.SameLine();
				if (ImGui.BeginChild("CFSChild"))
				{
						try
						{
								if (Selector.Selected == null && DefaultValueSelector != null)
								{
										Selector.SelectByValue(DefaultValueSelector());
								}
								if(Selector.Selected != null)
								{
										if (Selector.Selected.NoFrame || Selector.Selected.Builder != null)
										{
												Selector.Selected.Draw();
										}
										else
										{
												new NuiBuilder()
														.Section(Selector.Selected.Path.SplitDirectories().Join(" - "))
														.Widget(Selector.Selected.Draw)
														.Draw();
										}
								}
								else
								{
										try
										{
												DrawWhenNoSelection?.Invoke();
										}
										catch(Exception e)
										{
												e.Log();
										}
								}
						}
						catch (Exception e)
						{
								e.Log();
						}
				}
				ImGui.EndChild();
		}

		public class FileSystemSelector : NightmareUI.OtterGuiWrapper.FileSystems.Configuration.SimplifiedSelector.FileSystemSelector<ConfigFileSystemEntry, FileSystemSelector.State>
		{
				public string Filter => this.FilterValue;
				public bool Sorted = false;
				public override ISortMode<ConfigFileSystemEntry> SortMode => Sorted?ISortMode<ConfigFileSystemEntry>.FoldersFirst:ISortMode<ConfigFileSystemEntry>.InternalOrder;

				ConfigFileSystem FS;
				public FileSystemSelector(ConfigFileSystem fs) : base(fs, Svc.KeyState, new(), (e) => e.Log())
				{
						this.FS = fs;
				}

				protected override bool FoldersDefaultOpen => true;
				protected override uint CollapsedFolderColor => ImGuiColors.DalamudViolet.ToUint();
				protected override uint ExpandedFolderColor => CollapsedFolderColor;

				protected sealed override void DrawLeafName(Leaf leaf, in State state, bool selected)
				{
						var flag = selected ? ImGuiTreeNodeFlags.Selected | LeafFlags : LeafFlags;
						flag |= ImGuiTreeNodeFlags.SpanFullWidth;
						var col = leaf.Value.GetColor();
						if (col != null) ImGui.PushStyleColor(ImGuiCol.Text, col.Value);
						using var _ = ImRaii.TreeNode(leaf.Name, flag);
						if (col != null) ImGui.PopStyleColor();
				}

				public record struct State { }
				protected override bool ApplyFilters(IPath path)
				{
						return false;
				}
				protected override bool ApplyFiltersAndState(IPath path, out ConfigFileSystem.FileSystemSelector.State state)
				{
						return false;
				}
		}
}
