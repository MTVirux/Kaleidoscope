using Dalamud.Plugin.Services;
using Kaleidoscope.Core;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// STUB — GlamourChest and Armoire capture is deferred to a follow-up plan.
/// These containers aren't in GameInventoryType so InventoryChangedRaw doesn't cover them;
/// the proper implementation reads MirageManager (glamour) and UIState.Cabinet (armoire)
/// gated by IAddonLifecycle for the relevant addons.
///
/// Until then, glamour/armoire data is NOT captured anywhere: InventoryCacheService is now a
/// read-only adapter over ResourceStore/DB and no longer dual-writes these containers, so the
/// data gap is real. This stub only keeps the service graph binding correct while making that
/// gap explicit.
/// </summary>
public sealed class HiddenContainerCapture : IDisposable, IRequiredService
{
    public HiddenContainerCapture(IAddonLifecycle addonLifecycle, ResourceObservationService service)
    {
        _ = addonLifecycle;
        _ = service;
        LogService.Debug(LogCategory.Inventory, "[HiddenContainerCapture] Phase 1 stub — Glamour/Armoire capture deferred");
    }

    public void Dispose() { }
}
