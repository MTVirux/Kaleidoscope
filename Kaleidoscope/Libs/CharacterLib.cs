using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.DalamudServices;
using ECommons.GameFunctions;

namespace Kaleidoscope.Libs
{
    public static unsafe class CharacterLib
    {
        public static bool ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            // Exactly one space
            int spaceCount = 0;
            foreach (var ch in trimmed)
            {
                if (ch == ' ') spaceCount++;
                if (char.IsDigit(ch)) return false; // No digits allowed
            }
            if (spaceCount != 1) return false;
            return true;
        }

        public static string GetCharacterName(ulong contentId)
        {
            try
            {
                if (contentId == 0) return null;
                var localCid = Svc.PlayerState.ContentId;
                if (contentId == localCid)
                {
                    var name = Svc.Objects.LocalPlayer?.Name.ToString();
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

                // last-resort fallback: return null (no reliable global lookup available here)
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
