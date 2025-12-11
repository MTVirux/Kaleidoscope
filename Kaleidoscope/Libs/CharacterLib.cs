using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.DalamudServices;
using ECommons.GameFunctions;

namespace Kaleidoscope.Libs
{
    public static unsafe class CharacterLib
    {
        public static string GetCharacterName(ulong contentId)
        {
            try
            {
                if (contentId == 0) return "Unknown";
                var localCid = Svc.ClientState.LocalContentId;
                if (contentId == localCid)
                {
                    var name = Svc.ClientState.LocalPlayer?.Name.ToString();
                    if (!string.IsNullOrEmpty(name)) return $"You ({name})";
                    return "You";
                }

                // try to find an in-memory object with that content id
                var found = Svc.Objects.FirstOrDefault(x => x is IPlayerCharacter pc && pc.Struct()->ContentId == contentId);
                if (found != null) return (found as IPlayerCharacter)?.Name.ToString() ?? contentId.ToString();

                // fallback to numeric id
                return contentId.ToString();
            }
            catch
            {
                return contentId.ToString();
            }
        }
    }
}
