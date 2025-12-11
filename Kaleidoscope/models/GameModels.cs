using System;

namespace Kaleidoscope.Models
{
    /// <summary>
    /// Simple 3D position + rotation container used by other models.
    /// </summary>
    public sealed class Position
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Rotation { get; set; }
    }

    /// <summary>
    /// Represents a single inventory item or stack.
    /// Values are intentionally simple so mapping code can adapt from ECommons wrappers.
    /// </summary>
    public sealed class InventoryItemModel
    {
        public uint ItemId { get; set; }
        public uint Quantity { get; set; }
        public ushort Slot { get; set; }
    }

    /// <summary>
    /// A lightweight actor model representing data commonly read from FFXIVClientStructs.
    /// This model is POCO so mapping code can translate from native/generated types in the
    /// integration layer (not included here).
    /// </summary>
    public class ActorModel
    {
        public ulong ObjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint OwnerId { get; set; }
        public Position Position { get; set; } = new Position();
        public uint CurrentHp { get; set; }
        public uint MaxHp { get; set; }
        public uint CurrentMp { get; set; }
        public uint Level { get; set; }
        public uint JobId { get; set; }
        public bool IsPlayer { get; set; }
    }

    /// <summary>
    /// Represents the player's basic model. Keep this small — other plugin code can extend it.
    /// </summary>
    public sealed class PlayerModel : ActorModel
    {
        public uint HomeWorld { get; set; }
        public string FreeCompany { get; set; } = string.Empty;
        public InventoryItemModel[]? Inventory { get; set; }
    }

    /// <summary>
    /// Holds resolver / address metadata commonly produced by generator or resolver tooling
    /// (FFXIVClientStructs). Useful to record mapping results from ECommons resolver helpers.
    /// </summary>
    public sealed class AddressResolutionModel
    {
        /// <summary>Unique key or name used to identify the signature (e.g. resolver key).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Raw signature pattern or description used for resolution.</summary>
        public string Signature { get; set; } = string.Empty;

        /// <summary>Resolved address, if any. Use 0 to indicate unresolved.</summary>
        public IntPtr Address { get; set; }

        /// <summary>True when the resolver returned a non-zero address.</summary>
        public bool Resolved => Address != IntPtr.Zero;
    }
}
