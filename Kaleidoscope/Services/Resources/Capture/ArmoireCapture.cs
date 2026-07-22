using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Kaleidoscope.Models.Resources;
using OtterGui.Services;
using CabinetSheet = Lumina.Excel.Sheets.Cabinet;

namespace Kaleidoscope.Services.Resources.Capture;

/// <summary>
/// Captures the Armoire (cabinet) into the unified resource store. The armoire has no GameInventoryType;
/// membership lives in <c>UIState.Cabinet</c> and is queried per Cabinet sheet row via
/// <c>Cabinet.IsItemInCabinet(cabinetRowId)</c>. Cabinet data is only fetched from the server while the
/// armoire UI is open, so scanning is gated on the CabinetWithdraw addon lifecycle and on
/// <c>Cabinet.IsCabinetLoaded()</c>, then throttled while the window stays open to catch stores/withdrawals.
/// Zero idle cost: the framework tick does nothing until PostSetup flips the active flag; PreFinalize clears
/// it. Only present rows are recorded (quantity 1); each scan zeroes out any previously stored cabinet slot
/// that is no longer present, since removals produce no event.
/// </summary>
public sealed class ArmoireCapture : IDisposable, IRequiredService
{
    private const string AddonName = "CabinetWithdraw";
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(1);

    private readonly IAddonLifecycle _lifecycle;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IDataManager _dataManager;
    private readonly ResourceObservationService _service;
    private readonly GameStateService _gameState;

    // Static game data, built once off-thread: (cabinet row id, item id) for every armoire-eligible item.
    // The cabinet row id is the stable per-item slot and the argument to Cabinet.IsItemInCabinet.
    private volatile (ushort RowId, uint ItemId)[]? _cabinetRows;
    private int _cabinetRowsBuilding;

    private bool _active;
    private bool _didInitialScan;
    private DateTime _nextScan;

    public ArmoireCapture(IAddonLifecycle lifecycle, IFramework framework, IClientState clientState, IDataManager dataManager, ResourceObservationService service, GameStateService gameState)
    {
        _lifecycle = lifecycle;
        _framework = framework;
        _clientState = clientState;
        _dataManager = dataManager;
        _service = service;
        _gameState = gameState;

        _lifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, OnAddonSetup);
        _lifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnAddonFinalize);
        _framework.Update += OnTick;
    }

    private void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        _active = true;
        _didInitialScan = false;
        _nextScan = DateTime.UtcNow;   // poll every frame until the server reports the cabinet loaded
        LogService.Debug(LogCategory.Inventory, "[ArmoireCapture] Armoire opened");
    }

    private void OnAddonFinalize(AddonEvent type, AddonArgs args)
    {
        _active = false;
        LogService.Debug(LogCategory.Inventory, "[ArmoireCapture] Armoire closed");
    }

    private unsafe void OnTick(IFramework framework)
    {
        if (!_active || !_clientState.IsLoggedIn) return;

        var now = DateTime.UtcNow;
        if (now < _nextScan) return;

        var uiState = UIState.Instance();
        if (uiState == null || !uiState->Cabinet.IsCabinetLoaded()) return;   // wait for the server to send cabinet data
        _nextScan = now + RescanInterval;

        var pid = _gameState.PlayerContentId;
        if (pid == 0) return;

        // Lumina sheet iteration can fault sqpack pages (file I/O) - build the row cache off
        // the framework thread and scan on a later tick once it is ready.
        if (_cabinetRows == null)
        {
            if (Interlocked.CompareExchange(ref _cabinetRowsBuilding, 1, 0) == 0)
                _ = Task.Run(BuildCabinetRows);
            return;
        }

        Scan(uiState, pid);
    }

    private unsafe void Scan(UIState* uiState, ulong pid)
    {
        var rows = _cabinetRows;
        if (rows == null || rows.Length == 0) return;

        var seen = new HashSet<short>();
        var batch = new List<ResourceObservation>();

        foreach (var (rowId, itemId) in rows)
        {
            if (!uiState->Cabinet.IsItemInCabinet(rowId)) continue;

            var slot = (short)rowId;
            seen.Add(slot);

            batch.Add(new ResourceObservation
            {
                Key = new ResourceKey
                {
                    OwnerId   = pid,
                    OwnerKind = OwnerKind.Player,
                    Container = Container.Armoire,
                    ItemId    = itemId,
                    Slot      = slot,
                },
                Quantity      = 1,
                Flags         = ResourceFlags.None,
                UpdatedAt     = DateTime.UtcNow,
                ParentOwnerId = 0,
            });
        }

        var present = batch.Count;
        ReconcileRemovals(pid, seen, batch);
        _service.RecordObservations(batch);

        if (!_didInitialScan)
        {
            _didInitialScan = true;
            LogService.Debug(LogCategory.Inventory, $"[ArmoireCapture] Initial scan: {present} items");
        }
    }

    /// <summary>Zero out any stored cabinet slot that was not seen in this scan (item was withdrawn).</summary>
    private void ReconcileRemovals(ulong pid, HashSet<short> seen, List<ResourceObservation> batch)
    {
        foreach (var (slot, itemId) in _service.Store.GetOccupiedSlots(pid, OwnerKind.Player, Container.Armoire))
        {
            if (seen.Contains(slot)) continue;

            batch.Add(new ResourceObservation
            {
                Key = new ResourceKey
                {
                    OwnerId   = pid,
                    OwnerKind = OwnerKind.Player,
                    Container = Container.Armoire,
                    ItemId    = itemId,
                    Slot      = slot,
                },
                Quantity      = 0,
                Flags         = ResourceFlags.None,
                UpdatedAt     = DateTime.UtcNow,
                ParentOwnerId = 0,
            });
        }
    }

    private void BuildCabinetRows()
    {
        try
        {
            var list = new List<(ushort, uint)>();
            var sheet = _dataManager.GetExcelSheet<CabinetSheet>();
            if (sheet != null)
            {
                foreach (var row in sheet)
                {
                    var itemId = row.Item.RowId;
                    if (itemId == 0) continue;
                    if (row.RowId > short.MaxValue) continue;   // Slot is a short; cabinet ids sit far below this today
                    list.Add(((ushort)row.RowId, itemId));
                }
            }

            _cabinetRows = list.ToArray();
        }
        catch (Exception ex)
        {
            // Reset the guard so a later tick retries instead of leaving capture dead for the session.
            LogService.Error(LogCategory.Inventory, "[ArmoireCapture] Cabinet sheet build failed", ex);
            Interlocked.Exchange(ref _cabinetRowsBuilding, 0);
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnTick;
        _lifecycle.UnregisterListener(AddonEvent.PostSetup, AddonName, OnAddonSetup);
        _lifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnAddonFinalize);
    }
}
