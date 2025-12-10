using Dalamud.Plugin.Ipc.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NightmareUI.OtterGuiWrapper.FileSystems.Configuration;
#nullable disable
public static class ConfigFileSystemHelpers
{
		public static IEnumerable<T?> CreateInstancesOf<T>() where T:ConfigFileSystemEntry
		{
				var instances = typeof(T).Assembly.GetTypes().Where(x => !x.IsAbstract && typeof(T).IsAssignableFrom(x)).Select(x => (T?)Activator.CreateInstance(x, true));
				var priorities = instances.Select(x => x.DisplayPriority).Distinct();
				foreach(var x in priorities.OrderDescending())
				{
						foreach(var i in instances)
						{
								if (i.DisplayPriority == x) yield return i;
						}
				}
		}
}
