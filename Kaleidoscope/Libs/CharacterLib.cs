using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
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
                
                // last-resort fallback to numeric id (no reliable global lookup available here)
                return contentId.ToString();
            }
            catch
            {
                return contentId.ToString();
            }
        }
    }
}
