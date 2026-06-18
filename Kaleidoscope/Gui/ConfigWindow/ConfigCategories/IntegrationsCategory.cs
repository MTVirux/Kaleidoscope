using Dalamud.Bindings.ImGui;
using Kaleidoscope.Models.Resources;
using Kaleidoscope.Services;
using Kaleidoscope.Services.Database;
using Kaleidoscope.Services.Resources;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace Kaleidoscope.Gui.ConfigWindow.ConfigCategories;

/// <summary>
/// Integrations with other plugins (AutoRetainer, etc.). Import-from-AR lives here so the
/// Data tab stays focused on data-management actions and integrations are a separate
/// concern with their own UI affordances.
/// </summary>
public sealed class IntegrationsCategory
{
    private readonly AutoRetainerService _autoRetainerIpc;
    private readonly CurrencyTrackerService _currencyTrackerService;
    private readonly KaleidoscopeDbService _dbService;
    private readonly ResourceObservationService _obsService;
    private readonly AutoRetainerFcPointsSyncService _fcPointsSync;

    // User-chosen import options. Default all unchecked — opt-in only.
    private bool _importCharacterNames = false;
    private bool _importRetainerNames = false;
    private bool _importGil = false;
    private bool _importFcPoints = false;

    private bool _importPopupOpen = false;
    private string _importStatus = "";

    public IntegrationsCategory(
        AutoRetainerService autoRetainerIpc,
        CurrencyTrackerService currencyTrackerService,
        KaleidoscopeDbService dbService,
        ResourceObservationService obsService,
        AutoRetainerFcPointsSyncService fcPointsSync)
    {
        _autoRetainerIpc = autoRetainerIpc;
        _currencyTrackerService = currencyTrackerService;
        _dbService = dbService;
        _obsService = obsService;
        _fcPointsSync = fcPointsSync;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("AutoRetainer");
        ImGui.Separator();

        if (!_autoRetainerIpc.IsAvailable)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.5f, 1f), "AutoRetainer not available");
            if (ImGui.Button("Refresh Connection"))
            {
                _autoRetainerIpc.Refresh();
            }
            return;
        }

        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1f, 0.5f, 1f), "AutoRetainer connected");
        ImGui.Spacing();

        ImGui.TextUnformatted("Import options:");
        ImGui.Checkbox("Character names + worlds", ref _importCharacterNames);
        ImGui.Checkbox("Retainer names (with owning-character link)", ref _importRetainerNames);
        ImGui.Checkbox("Gil values (character + retainer where available)", ref _importGil);
        ImGui.Checkbox("FC points (read from AutoRetainer's config)", ref _importFcPoints);

        ImGui.Spacing();

        var anySelected = _importCharacterNames || _importRetainerNames || _importGil || _importFcPoints;
        if (!anySelected) ImGui.BeginDisabled();
        if (ImGui.Button("Import Selected"))
        {
            ImGui.OpenPopup("config_import_autoretainer_confirm");
            _importPopupOpen = true;
            _importStatus = "";
        }
        if (!anySelected) ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_importStatus))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted(_importStatus);
        }

        if (ImGui.BeginPopupModal("config_import_autoretainer_confirm", ref _importPopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Import from AutoRetainer using these options:");
            if (_importCharacterNames) ImGui.BulletText("Character names + worlds");
            if (_importRetainerNames)  ImGui.BulletText("Retainer names");
            if (_importGil)            ImGui.BulletText("Gil values");
            if (_importFcPoints)       ImGui.BulletText("FC points");
            ImGui.Spacing();
            ImGui.TextUnformatted("Proceed?");

            if (ImGui.Button("Yes"))
            {
                try
                {
                    var (chars, retainers, gilUpdates, fcPointUpdates) = RunSelectiveImport();
                    _importStatus = $"Imported: {chars} characters, {retainers} retainers, {gilUpdates} gil updates, {fcPointUpdates} FC point updates.";
                    LogService.Info(LogCategory.UI, $"[IntegrationsCategory] {_importStatus}");
                }
                catch (Exception ex)
                {
                    _importStatus = $"Import failed: {ex.Message}";
                    LogService.Error(LogCategory.UI, "AR import failed", ex);
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("No"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private (int chars, int retainers, int gilUpdates, int fcPointUpdates) RunSelectiveImport()
    {
        int chars = 0;
        int retainers = 0;
        int gilUpdates = 0;
        int fcPointUpdates = 0;

        // FC points come from AutoRetainer's config file rather than its IPC offline data.
        if (_importFcPoints)
            fcPointUpdates = _fcPointsSync.ImportNow();

        var characters = _autoRetainerIpc.GetAllFullCharacterData();
        foreach (var ch in characters)
        {
            if (ch.CID == 0 || string.IsNullOrEmpty(ch.Name)) continue;

            if (_importCharacterNames)
            {
                _dbService.UpsertOwnerName(ch.CID, OwnerKind.Player, ch.Name, ch.World);
                // Mirror into in-memory character cache so name lookups see it immediately.
                _currencyTrackerService.CharacterDataCache.SetCharacterName(ch.CID, ch.Name);
                chars++;
            }

            if (_importGil && ch.Gil > 0)
            {
                _obsService.RecordObservation(new ResourceObservation
                {
                    Key = new ResourceKey
                    {
                        OwnerId = ch.CID,
                        OwnerKind = OwnerKind.Player,
                        Container = Container.SpecialPlayer,
                        ItemId = ResourceCatalog.GilItemId,
                        Slot = -1,
                    },
                    Quantity = ch.Gil,
                    UpdatedAt = DateTime.UtcNow,
                    ParentOwnerId = 0,
                });
                gilUpdates++;
            }

            if (_importRetainerNames)
            {
                foreach (var ret in ch.Retainers)
                {
                    if (ret.RetainerId == 0 || string.IsNullOrEmpty(ret.Name)) continue;
                    _dbService.UpsertOwnerName(ret.RetainerId, OwnerKind.Retainer, ret.Name);

                    // Placeholder resource row so the retainer is enumerable in the data table
                    // immediately after import. MemoryPoller will upsert this with the real gil
                    // value the next time the retainer is observed. Quantity=0 is honest about
                    // the unknown state until first observation.
                    _obsService.RecordObservation(new ResourceObservation
                    {
                        Key = new ResourceKey
                        {
                            OwnerId = ret.RetainerId,
                            OwnerKind = OwnerKind.Retainer,
                            Container = Container.RetainerGil,
                            ItemId = ResourceCatalog.GilItemId,
                            Slot = -1,
                        },
                        Quantity = 0,
                        UpdatedAt = DateTime.UtcNow,
                        ParentOwnerId = ch.CID,
                    });
                    retainers++;

                    if (_importGil && ret.Gil > 0)
                    {
                        _obsService.RecordObservation(new ResourceObservation
                        {
                            Key = new ResourceKey
                            {
                                OwnerId = ret.RetainerId,
                                OwnerKind = OwnerKind.Retainer,
                                Container = Container.RetainerGil,
                                ItemId = ResourceCatalog.GilItemId,
                                Slot = -1,
                            },
                            Quantity = ret.Gil,
                            UpdatedAt = DateTime.UtcNow,
                            ParentOwnerId = ch.CID,
                        });
                        gilUpdates++;
                    }
                }
            }
        }

        return (chars, retainers, gilUpdates, fcPointUpdates);
    }
}
