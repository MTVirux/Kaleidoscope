using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Kaleidoscope.Services;

/// <summary>
/// Unified wrapper for accessing game client state and character data.
/// Consolidates FFXIVClientStructs access and Dalamud service wrappers for testability.
/// </summary>
/// <remarks>
/// This static service provides centralized access to:
/// - Player state (content ID, name)
/// - Inventory and retainer managers
/// - Character name lookups from loaded objects
/// - Currency and special currency queries
/// </remarks>
public static unsafe class GameStateService
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

    #region Character Utilities

    /// <summary>
    /// Validates that a character name follows FFXIV naming conventions.
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <returns>True if the name is valid (exactly one space, no digits).</returns>
    public static bool ValidateCharacterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        int spaceCount = 0;
        foreach (var ch in trimmed)
        {
            if (ch == ' ') spaceCount++;
            if (char.IsDigit(ch)) return false;
        }
        return spaceCount == 1;
    }

    /// <summary>
    /// Gets a character name by content ID from currently loaded game objects.
    /// </summary>
    /// <param name="contentId">The character's content ID.</param>
    /// <returns>The character name if found, null otherwise.</returns>
    public static string? GetCharacterName(ulong contentId)
    {
        try
        {
            if (contentId == 0) return null;
            
            // Check if it's the local player first
            var localCid = _playerState?.ContentId ?? 0;
            if (contentId == localCid)
            {
                var name = _objectTable?.LocalPlayer?.Name.ToString();
                if (!string.IsNullOrEmpty(name)) return name;
                return null;
            }

            // Search loaded player characters
            if (_objectTable != null)
            {
                var pc = _objectTable.OfType<IPlayerCharacter>().FirstOrDefault(p =>
                {
                    var charStruct = (Character*)p.Address;
                    return charStruct != null && charStruct->ContentId == contentId;
                });
                if (pc != null)
                {
                    var oname = pc.Name.ToString();
                    if (!string.IsNullOrEmpty(oname)) return oname;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Game Manager Access

    public static InventoryManager* InventoryManagerInstance()
    {
        try { return InventoryManager.Instance(); }
        catch (Exception ex) { LogService.Debug(LogCategory.GameState, $"InventoryManager.Instance() failed: {ex.Message}"); return null; }
    }

    public static RetainerManager* RetainerManagerInstance()
    {
        try { return RetainerManager.Instance(); }
        catch (Exception ex) { LogService.Debug(LogCategory.GameState, $"RetainerManager.Instance() failed: {ex.Message}"); return null; }
    }

    #endregion

    #region Player State

    public static ulong PlayerContentId => _playerState?.ContentId ?? 0;

    /// <summary>
    /// Gets the current player's name using IObjectTable.LocalPlayer (recommended Dalamud approach).
    /// </summary>
    public static string? LocalPlayerName
    {
        get
        {
            try { return _objectTable?.LocalPlayer?.Name.ToString(); }
            catch (Exception ex) { LogService.Debug(LogCategory.GameState, $"LocalPlayer name access failed: {ex.Message}"); return null; }
        }
    }

    #endregion

    #region Retainer Operations

    /// <summary>
    /// Gets the total gil held by all retainers (from RetainerManager cached data).
    /// </summary>
    public static long GetAllRetainersGil()
    {
        try
        {
            var rm = RetainerManagerInstance();
            if (rm == null || !rm->IsReady) return 0;

            long total = 0;
            var count = rm->GetRetainerCount();
            for (uint i = 0; i < count; i++)
            {
                var retainer = rm->GetRetainerBySortedIndex(i);
                if (retainer != null && retainer->Available)
                {
                    total += retainer->Gil;
                }
            }
            return total;
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.GameState, $"GetAllRetainersGil failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Gets the item count from the currently active retainer's inventory.
    /// Only works when a retainer is selected/open.
    /// </summary>
    public static int GetActiveRetainerItemCount(InventoryManager* im, uint itemId, bool isHq = false)
    {
        if (im == null) return 0;

        try
        {
            int total = 0;

            // Check retainer storage pages (RetainerPage1-7)
            foreach (var page in InventoryConstants.RetainerStoragePages)
            {
                total += im->GetItemCountInContainer(itemId, page, isHq);
            }

            return total;
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.GameState, $"GetActiveRetainerItemCount failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Checks if a retainer is currently active (selected/open).
    /// </summary>
    public static bool IsRetainerActive()
    {
        try
        {
            var rm = RetainerManagerInstance();
            if (rm == null || !rm->IsReady) return false;
            return rm->LastSelectedRetainerId != 0 && rm->GetActiveRetainer() != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the active retainer's ID, or 0 if no retainer is active.
    /// </summary>
    public static ulong GetActiveRetainerId()
    {
        try
        {
            var rm = RetainerManagerInstance();
            if (rm == null || !rm->IsReady) return 0;
            return rm->LastSelectedRetainerId;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Gets the active retainer's name, or null if no retainer is active.
    /// </summary>
    public static string? GetActiveRetainerName()
    {
        try
        {
            var rm = RetainerManagerInstance();
            if (rm == null || !rm->IsReady) return null;
            var retainer = rm->GetActiveRetainer();
            if (retainer == null) return null;
            return retainer->NameString;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all retainer crystal counts from the ItemFinderModule cache.
    /// This works without having a retainer window open, using the game's item finder cache.
    /// Returns an array of 18 values: [FireShard, IceShard, WindShard, EarthShard, LightningShard, WaterShard,
    ///                                  FireCrystal, IceCrystal, WindCrystal, EarthCrystal, LightningCrystal, WaterCrystal,
    ///                                  FireCluster, IceCluster, WindCluster, EarthCluster, LightningCluster, WaterCluster]
    /// </summary>
    public static long[] GetAllRetainersCrystals()
    {
        // 18 entries: 6 elements × 3 tiers (Shard, Crystal, Cluster)
        var totals = new long[18];
        
        try
        {
            var rm = RetainerManagerInstance();
            if (rm == null || !rm->IsReady) return totals;
            
            var ifm = ItemFinderModule.Instance();
            if (ifm == null) return totals;
            
            var count = rm->GetRetainerCount();
            for (uint i = 0; i < count; i++)
            {
                var retainer = rm->GetRetainerBySortedIndex(i);
                if (retainer == null || !retainer->Available || retainer->RetainerId == 0)
                    continue;
                
                if (!ifm->RetainerInventories.TryGetValuePointer(retainer->RetainerId, out var invPtr) || invPtr == null)
                    continue;
                
                // TryGetValuePointer returns Pointer<ItemFinderRetainerInventory>*, need to dereference and get the raw pointer
                var inv = (ItemFinderRetainerInventory*)(*invPtr);
                if (inv == null) continue;
                
                // CrystalQuantities: 18 entries stored by tier first, then element
                // Indices 0-5: Shards (Fire, Ice, Wind, Earth, Lightning, Water)
                // Indices 6-11: Crystals (Fire, Ice, Wind, Earth, Lightning, Water)
                // Indices 12-17: Clusters (Fire, Ice, Wind, Earth, Lightning, Water)
                for (int idx = 0; idx < 18; idx++)
                {
                    totals[idx] += inv->CrystalQuantities[idx];
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.GameState, $"GetAllRetainersCrystals failed: {ex.Message}");
        }
        
        return totals;
    }

    #endregion

    #region Currency Queries

    /// <summary>
    /// Gets the Free Company Credits from the FreeCompanyCreditShop agent.
    /// Based on AutoRetainer implementation: offset 256 in the agent.
    /// </summary>
    /// <returns>The FC Credits value, or null if unavailable (e.g., not in FC, data not loaded).</returns>
    public static long? GetFreeCompanyCredits()
    {
        try
        {
            var agentModule = AgentModule.Instance();
            if (agentModule == null) return null;

            var agent = agentModule->GetAgentByInternalId(AgentId.FreeCompanyCreditShop);
            if (agent == null) return null;

            // FC Credits are stored at offset 256 in the FreeCompanyCreditShop agent
            // This approach is used by AutoRetainer and is the reliable way to get FC credits
            var credits = *(int*)((nint)agent + 256);
            
            // Return null if credits is 0 or negative (likely means no FC or data not loaded)
            if (credits <= 0) return null;
            
            return credits;
        }
        catch (Exception ex)
        {
            LogService.Debug(LogCategory.GameState, $"GetFreeCompanyCredits failed: {ex.Message}");
            return null;
        }
    }

    #endregion
}
