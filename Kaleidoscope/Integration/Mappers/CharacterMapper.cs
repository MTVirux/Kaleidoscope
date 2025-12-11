using System;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Kaleidoscope.Models;

namespace Kaleidoscope.Integration.Mappers
{
    public static unsafe class CharacterMapper
    {
        public static ActorModel? FromCharacter(Character* c)
        {
            if (c == null) return null;

            try
            {
                var a = new ActorModel();

                // Object id
                a.ObjectId = c->GetGameObjectId().Id;

                // Name (CStringPointer -> string)
                a.Name = c->GetName();

                // Owner / basic fields from GameObject
                a.OwnerId = c->OwnerId;

                // Position and rotation
                a.Position = new Position {
                    X = c->Position.X,
                    Y = c->Position.Y,
                    Z = c->Position.Z,
                    Rotation = c->Rotation
                };

                // CharacterData fields (inherited)
                a.CurrentHp = c->Health;
                a.MaxHp = c->MaxHealth;
                a.CurrentMp = c->Mana;
                a.Level = c->Level;
                a.JobId = c->ClassJob;

                // Kind
                a.IsPlayer = c->GetObjectKind() == ObjectKind.Pc;

                return a;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static PlayerModel? FromCharacterToPlayer(Character* c)
        {
            if (c == null) return null;

            var baseModel = FromCharacter(c);
            if (baseModel == null) return null;

            var p = new PlayerModel {
                ObjectId = baseModel.ObjectId,
                Name = baseModel.Name,
                OwnerId = baseModel.OwnerId,
                Position = baseModel.Position,
                CurrentHp = baseModel.CurrentHp,
                MaxHp = baseModel.MaxHp,
                CurrentMp = baseModel.CurrentMp,
                Level = baseModel.Level,
                JobId = baseModel.JobId,
                IsPlayer = baseModel.IsPlayer,
                HomeWorld = c->HomeWorld,
                FreeCompany = c->FreeCompanyTagString
            };

            // Try to populate primary inventory (Inventory1) if InventoryManager is available
            try
            {
                var invMgr = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
                if (invMgr != null)
                {
                    var inv = invMgr->GetInventoryContainer(FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1);
                    p.Inventory = InventoryMapper.FromInventoryContainer(inv);
                }
            }
            catch
            {
                // swallow - mapping helpers should not throw
            }

            return p;
        }
    }
}
