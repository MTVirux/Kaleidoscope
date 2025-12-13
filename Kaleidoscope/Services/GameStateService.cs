namespace Kaleidoscope.Services
{
    using ECommons.DalamudServices;

    /// <summary>
    /// Centralized wrapper for unsafe access to game client structs.
    /// This file isolates all direct references to FFXIVClientStructs so other
    /// code can depend on safe, testable APIs instead of calling the structs directly.
    /// </summary>
    public static unsafe class GameStateService
    {
        public static FFXIVClientStructs.FFXIV.Client.Game.InventoryManager* InventoryManagerInstance()
        {
            try { return FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance(); }
            catch (Exception ex) { LogService.Debug($"[GameStateService] InventoryManager.Instance() failed: {ex.Message}"); return null; }
        }

        public static FFXIVClientStructs.FFXIV.Client.Game.CurrencyManager* CurrencyManagerInstance()
        {
            try { return FFXIVClientStructs.FFXIV.Client.Game.CurrencyManager.Instance(); }
            catch (Exception ex) { LogService.Debug($"[GameStateService] CurrencyManager.Instance() failed: {ex.Message}"); return null; }
        }

        public static ulong PlayerContentId => Svc.PlayerState.ContentId;

        public static string? LocalPlayerName
        {
            get
            {
                try { return Svc.Objects.LocalPlayer?.Name.ToString(); }
                catch (Exception ex) { LogService.Debug($"[GameStateService] LocalPlayer name access failed: {ex.Message}"); return null; }
            }
        }
    }
}
