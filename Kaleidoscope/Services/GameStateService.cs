using ECommons.DalamudServices;

namespace Kaleidoscope.Services;

/// <summary>
/// Wrapper for unsafe access to game client structs.
/// Isolates FFXIVClientStructs references for testability.
/// </summary>
public static unsafe class GameStateService
{
    public static FFXIVClientStructs.FFXIV.Client.Game.InventoryManager* InventoryManagerInstance()
    {
        try { return FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance(); }
        catch (Exception ex) { LogService.Debug($"InventoryManager.Instance() failed: {ex.Message}"); return null; }
    }

    public static FFXIVClientStructs.FFXIV.Client.Game.CurrencyManager* CurrencyManagerInstance()
    {
        try { return FFXIVClientStructs.FFXIV.Client.Game.CurrencyManager.Instance(); }
        catch (Exception ex) { LogService.Debug($"CurrencyManager.Instance() failed: {ex.Message}"); return null; }
    }

    public static ulong PlayerContentId => Svc.PlayerState.ContentId;

    public static string? LocalPlayerName
    {
        get
        {
            try { return Svc.Objects.LocalPlayer?.Name.ToString(); }
            catch (Exception ex) { LogService.Debug($"LocalPlayer name access failed: {ex.Message}"); return null; }
        }
    }
}
