using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Kaleidoscope.Models;

namespace Kaleidoscope.Integration.Mappers
{
    public static unsafe class InventoryMapper
    {
        public static InventoryItemModel[]? FromInventoryContainer(InventoryContainer* container)
        {
            if (container == null) return null;

            try
            {
                var size = container->GetSize();
                if (size <= 0) return Array.Empty<InventoryItemModel>();

                var list = new List<InventoryItemModel>(size);

                for (int i = 0; i < size; ++i)
                {
                    var slot = container->GetInventorySlot(i);
                    if (slot == null) continue;

                    // If InventoryItem exposes helper methods, prefer them; otherwise read fields directly
                    uint itemId = 0;
                    uint quantity = 0;
                    ushort slotIndex = 0;

                    try { itemId = slot->GetItemId(); } catch { itemId = slot->ItemId; }
                    try { quantity = slot->GetQuantity(); } catch { quantity = (uint)slot->Quantity; }
                    try { slotIndex = slot->GetSlot(); } catch { slotIndex = (ushort)slot->Slot; }

                    if (itemId == 0 && quantity == 0) continue;

                    list.Add(new InventoryItemModel {
                        ItemId = itemId,
                        Quantity = quantity,
                        Slot = slotIndex
                    });
                }

                return list.ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Map a set of inventory types from the provided InventoryManager into a dictionary keyed by <see cref="InventoryType"/>.
        /// Returns null values for inventories that could not be read.
        /// </summary>
        public static Dictionary<InventoryType, InventoryItemModel[]?> FromInventoryTypes(InventoryManager* mgr, IEnumerable<InventoryType> types)
        {
            var result = new Dictionary<InventoryType, InventoryItemModel[]?>();
            if (mgr == null) return result;

            foreach (var t in types)
            {
                try
                {
                    var cont = mgr->GetInventoryContainer(t);
                    result[t] = FromInventoryContainer(cont);
                }
                catch
                {
                    result[t] = null;
                }
            }

            return result;
        }

        /// <summary>
        /// Convenience helper: map the player's common bag set (Inventory1..Inventory4, Saddlebags, Premium saddlebags, EquippedItems, armory, retainer, free company).
        /// Uses InventoryManager.Instance() internally.
        /// </summary>
        public static Dictionary<InventoryType, InventoryItemModel[]?> FromPlayerInventories(Character* c)
        {
            var result = new Dictionary<InventoryType, InventoryItemModel[]?>();
            if (c == null) return result;

            try
            {
                var mgr = InventoryManager.Instance();
                if (mgr == null) return result;

                var types = new InventoryType[] {
                    // Player bags
                    InventoryType.Inventory1,
                    InventoryType.Inventory2,
                    InventoryType.Inventory3,
                    InventoryType.Inventory4,

                    // Saddlebags
                    InventoryType.SaddleBag1,
                    InventoryType.SaddleBag2,
                    InventoryType.PremiumSaddleBag1,
                    InventoryType.PremiumSaddleBag2,

                    // Equipped / armory
                    InventoryType.EquippedItems,
                    InventoryType.ArmoryOffHand,
                    InventoryType.ArmoryHead,
                    InventoryType.ArmoryBody,
                    InventoryType.ArmoryHands,
                    InventoryType.ArmoryWaist,
                    InventoryType.ArmoryLegs,
                    InventoryType.ArmoryFeets,
                    InventoryType.ArmoryEar,
                    InventoryType.ArmoryNeck,
                    InventoryType.ArmoryWrist,
                    InventoryType.ArmoryRings,
                    InventoryType.ArmorySoulCrystal,
                    InventoryType.ArmoryMainHand,

                    // Retainer pages + retainer equipped
                    InventoryType.RetainerPage1,
                    InventoryType.RetainerPage2,
                    InventoryType.RetainerPage3,
                    InventoryType.RetainerPage4,
                    InventoryType.RetainerPage5,
                    InventoryType.RetainerPage6,
                    InventoryType.RetainerPage7,
                    InventoryType.RetainerEquippedItems,

                    // Free Company
                    InventoryType.FreeCompanyPage1,
                    InventoryType.FreeCompanyPage2,
                    InventoryType.FreeCompanyPage3,
                    InventoryType.FreeCompanyPage4,
                    InventoryType.FreeCompanyPage5,
                    InventoryType.FreeCompanyGil
                };

                return FromInventoryTypes(mgr, types);
            }
            catch
            {
                return result;
            }
        }
    }
}
