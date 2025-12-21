using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace Kaleidoscope.Libs;

/// <summary>
/// Utility library for character-related operations.
/// </summary>
public static unsafe class CharacterLib
{
    private static IPlayerState? _playerState;
    private static IObjectTable? _objectTable;

    /// <summary>
    /// Initializes the static service references. Called once during plugin startup.
    /// </summary>
    public static void Initialize(IPlayerState playerState, IObjectTable objectTable)
    {
        _playerState = playerState;
        _objectTable = objectTable;
    }

    /// <summary>
    /// Validates that a character name follows FFXIV naming conventions.
    /// </summary>
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

    /// <summary>
    /// Gets the FFXIVClientStructs Character pointer from a player character.
    /// </summary>
    private static Character* GetCharacterStruct(IPlayerCharacter pc)
    {
        return (Character*)pc.Address;
    }

    /// <summary>
    /// Gets a character name by content ID from loaded game objects.
    /// </summary>
    public static string? GetCharacterName(ulong contentId)
    {
        try
        {
            if (contentId == 0) return null;
            var localCid = _playerState?.ContentId ?? 0;
            if (contentId == localCid)
            {
                var name = _objectTable?.LocalPlayer?.Name.ToString();
                if (!string.IsNullOrEmpty(name)) return name;
                return null;
            }

            // Try to find the character among currently-loaded objects and return their name.
            if (_objectTable != null)
            {
                var pc = _objectTable.OfType<IPlayerCharacter>().FirstOrDefault(p =>
                {
                    var charStruct = GetCharacterStruct(p);
                    return charStruct != null && charStruct->ContentId == contentId;
                });
                if (pc != null)
                {
                    var oname = pc.Name.ToString();
                    if (!string.IsNullOrEmpty(oname)) return oname;
                }
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
