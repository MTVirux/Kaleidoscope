using Dalamud.Plugin.Services;
using Kaleidoscope.Core;
using OtterGui.Services;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// PHASE 1 STUB — GlamourChest and Armoire capture is deferred to a follow-up plan.
/// These containers aren't in GameInventoryType so InventoryChangedRaw doesn't cover them;
/// the proper implementation reads MirageManager (glamour) and UIState.Cabinet (armoire)
/// gated by IAddonLifecycle for the relevant addons.
///
/// Until then, glamour/armoire data is simply not captured by the new pipeline. The OLD
/// InventoryCacheService still runs (Phase 1 dual-write) and continues to capture these
/// for old-path consumers. This stub ensures the service graph compiles and binds correctly
/// while making the gap explicit.
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
