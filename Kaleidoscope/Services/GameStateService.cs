using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Kaleidoscope.Services;

/// <summary>
/// Wrapper for unsafe access to game client structs.
/// Isolates FFXIVClientStructs references for testability.
/// </summary>
public static unsafe class GameStateService
{
    public static InventoryManager* InventoryManagerInstance()
    {
        try { return InventoryManager.Instance(); }
        catch (Exception ex) { LogService.Debug($"InventoryManager.Instance() failed: {ex.Message}"); return null; }
    }

    public static RetainerManager* RetainerManagerInstance()
    {
        try { return RetainerManager.Instance(); }
        catch (Exception ex) { LogService.Debug($"RetainerManager.Instance() failed: {ex.Message}"); return null; }
    }

    public static ulong PlayerContentId => Svc.PlayerState.ContentId;

    /// <summary>
    /// Gets the current player's name using IObjectTable.LocalPlayer (recommended Dalamud approach).
    /// </summary>
    public static string? LocalPlayerName
    {
        get
        {
            try { return Svc.Objects.LocalPlayer?.Name.ToString(); }
            catch (Exception ex) { LogService.Debug($"LocalPlayer name access failed: {ex.Message}"); return null; }
        }
    }

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
            LogService.Debug($"GetAllRetainersGil failed: {ex.Message}");
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

            // Check retainer inventory pages (RetainerPage1-7)
            for (var page = InventoryType.RetainerPage1; page <= InventoryType.RetainerPage7; page++)
            {
                total += im->GetItemCountInContainer(itemId, page, isHq);
            }

            return total;
        }
        catch (Exception ex)
        {
            LogService.Debug($"GetActiveRetainerItemCount failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Gets crystal count from the currently active retainer's crystal inventory.
    /// Only works when a retainer is selected/open.
    /// </summary>
    public static int GetActiveRetainerCrystalCount(InventoryManager* im, uint itemId)
    {
        if (im == null) return 0;

        try
        {
            return im->GetItemCountInContainer(itemId, InventoryType.RetainerCrystals);
        }
        catch (Exception ex)
        {
            LogService.Debug($"GetActiveRetainerCrystalCount failed: {ex.Message}");
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
}
