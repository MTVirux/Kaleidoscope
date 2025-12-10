using ECommons.Configuration;
using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace NightmareUI.Censoring;

[Serializable]
[DataContract(Name = "CensorConfig")]
public class CensorConfig
{
    [DataMember(Name = "Seed")][Obfuscation(Exclude = true)] public string Seed = Guid.NewGuid().ToString();
    [DataMember(Name = "Enabled")][Obfuscation(Exclude = true)] public bool Enabled = false;
    [DataMember(Name = "LesserCensor")][Obfuscation(Exclude = true)] public bool LesserCensor = false;
}
