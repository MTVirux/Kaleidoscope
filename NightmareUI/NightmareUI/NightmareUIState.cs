using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NightmareUI;
[Serializable]
public sealed unsafe class NightmareUIState
{
    [Obfuscation] public Dictionary<string, string> ActiveTab = [];
    [Obfuscation] public HashSet<uint> CollapsedHeaders = [];
}