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
                
                // delegate to shared CharacterLib for lookup (includes monitor, object table, and DB fallback)
                try
                {
                    return CriticalCommonLib.Services.CharacterLib.GetCharacterName(contentId, false);
                }
                catch
                {
                    // last-resort fallback to numeric id
                    return contentId.ToString();
                }
            }
            catch
            {
                return contentId.ToString();
            }
        }
    }
}
