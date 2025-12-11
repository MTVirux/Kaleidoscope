using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kaleidoscope.models
{
    /// <summary>
    /// A comprehensive, extensible character model intended to capture
    /// all relevant in-game data surfaced by ECommons and FFXIV client structs.
    /// This is intentionally a flattened, serializable POCO so it can be
    /// easily populated by mapping code that consumes ECommons/FFXIV data.
    /// </summary>
    public class CharacterModel
    {
        // Timestamp
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Identity
        public string Name { get; set; }
        public ulong ContentId { get; set; }
        public uint ObjectId { get; set; }
        public uint HomeWorldId { get; set; }
        public string FreeCompany { get; set; }
        public string GrandCompany { get; set; }

        // Appearance / demographics
        public string Race { get; set; }
        public string Tribe { get; set; }
        public string Gender { get; set; }
        public float Height { get; set; }
        public int Face { get; set; }
        public int HairStyle { get; set; }
        public int HairColor { get; set; }
        public int SkinTone { get; set; }

        // Class / Job information
        public int CurrentJob { get; set; }
        public int Level { get; set; }
        // Optional mapping of jobId -> level for all jobs
        public Dictionary<int, int> JobLevels { get; set; } = new Dictionary<int, int>();

        // Core stats
        public int HP { get; set; }
        public int MaxHP { get; set; }
        public int MP { get; set; }
        public int MaxMP { get; set; }
        public int GP { get; set; }
        public int CP { get; set; }
        public int TP { get; set; }

        // Position & world
        public uint WorldId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Rotation { get; set; }

        // Combat & status
        public bool IsCasting { get; set; }
        public uint CurrentActionId { get; set; }
        public double CastTimeRemaining { get; set; }
        public List<StatusEffectModel> StatusEffects { get; set; } = new List<StatusEffectModel>();

        // Targeting
        public ulong TargetContentId { get; set; }
        public uint TargetObjectId { get; set; }
        public TargetInfoModel TargetInfo { get; set; }

        // Equipment & inventory
        // Equipment is stored by slot name (e.g. "Head", "Body") to an ItemModel
        public Dictionary<string, ItemModel> Equipment { get; set; } = new Dictionary<string, ItemModel>(StringComparer.OrdinalIgnoreCase);
        public List<ItemModel> Inventory { get; set; } = new List<ItemModel>();

        // Party / alliance info
        public List<PartyMemberModel> PartyMembers { get; set; } = new List<PartyMemberModel>();

        // Misc
        public bool IsMounted { get; set; }
        public uint MountId { get; set; }
        public string Emote { get; set; }

        // Extensibility: raw dictionary for fields not yet mapped
        [JsonExtensionData]
        public Dictionary<string, object> Raw { get; set; } = new Dictionary<string, object>();

        // Serialization helpers
        public string ToJson(JsonSerializerOptions options = null)
        {
            options ??= new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(this, options);
        }

        public static CharacterModel FromJson(string json, JsonSerializerOptions options = null)
        {
            options ??= new JsonSerializerOptions();
            return JsonSerializer.Deserialize<CharacterModel>(json, options);
        }

        // Basic convenience factory for a minimal character
        public static CharacterModel CreateMinimal(string name, ulong contentId)
        {
            return new CharacterModel
            {
                Name = name,
                ContentId = contentId
            };
        }
    }

    public class StatusEffectModel
    {
        public int StatusId { get; set; }
        public int SourceActorId { get; set; }
        public float Duration { get; set; }
        public float TimeRemaining { get; set; }
        public int Stacks { get; set; }
    }

    public class ItemModel
    {
        public ulong ItemId { get; set; }
        public uint Slot { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public int Durability { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class PartyMemberModel
    {
        public string Name { get; set; }
        public ulong ContentId { get; set; }
        public uint ObjectId { get; set; }
        public int Job { get; set; }
        public int Level { get; set; }
        public int CurrentHP { get; set; }
        public int MaxHP { get; set; }
    }

    public class TargetInfoModel
    {
        public string Name { get; set; }
        public ulong ContentId { get; set; }
        public uint ObjectId { get; set; }
        public int TargetHP { get; set; }
        public int TargetMaxHP { get; set; }
    }
}
