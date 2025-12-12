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
                if (contentId == 0) return null;
                var localCid = Svc.ClientState.LocalContentId;
                if (contentId == localCid)
                {
                    var name = Svc.ClientState.LocalPlayer?.Name.ToString();
                    if (!string.IsNullOrEmpty(name)) return name;
                    return null;
                }
                
                // Try to find the character among currently-loaded objects and return their name.
                var pc = Svc.Objects.OfType<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>().FirstOrDefault(p => p.Struct() != null && p.Struct()->ContentId == contentId);
                if (pc != null)
                {
                    var oname = pc.Name.ToString();
                    if (!string.IsNullOrEmpty(oname)) return oname;
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
