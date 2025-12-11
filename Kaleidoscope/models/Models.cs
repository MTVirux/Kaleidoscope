namespace Kaleidoscope.Models
{
    public struct Position
    {
        public float X;
        public float Y;
        public float Z;
        public float Rotation;
    }

    public class ActorModel
    {
        public ulong ObjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public ulong OwnerId { get; set; }
        public Position Position { get; set; }
        public uint CurrentHp { get; set; }
        public uint MaxHp { get; set; }
        public uint CurrentMp { get; set; }
        public uint Level { get; set; }
        public uint JobId { get; set; }
        public bool IsPlayer { get; set; }
    }

    public class PlayerModel : ActorModel
    {
        public uint HomeWorld { get; set; }
        public string FreeCompany { get; set; } = string.Empty;
        public InventoryItemModel[]? Inventory { get; set; }
    }

    public class InventoryItemModel
    {
        public uint ItemId { get; set; }
        public uint Quantity { get; set; }
        public ushort Slot { get; set; }
    }
}
