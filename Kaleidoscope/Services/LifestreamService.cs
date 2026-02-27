using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// IPC service for communicating with the Lifestream plugin.
/// Provides world travel, aethernet teleportation, instance changing,
/// and character switching capabilities via IPC.
/// </summary>
/// <remarks>
/// Registered as a singleton service to avoid creating multiple IPC subscriptions.
/// Automatically initializes on first access. Retries connection every 5 seconds if unavailable.
/// <br/>
/// Lifestream uses EzIPC with the prefix "Lifestream." for all IPC names.
/// Source: https://github.com/NightmareXIV/Lifestream/blob/main/Lifestream/IPC/IPCProvider.cs
/// </remarks>
public sealed class LifestreamService : IDisposable, IService
{
    private readonly IDalamudPluginInterface _pluginInterface;

    // --- Status ---
    private ICallGateSubscriber<bool>? _isBusy;
    private ICallGateSubscriber<object?>? _abort;

    // --- World Travel ---
    private ICallGateSubscriber<string, bool>? _canVisitSameDC;
    private ICallGateSubscriber<string, bool>? _canVisitCrossDC;
    private ICallGateSubscriber<string, bool>? _changeWorld;
    private ICallGateSubscriber<uint, bool>? _changeWorldById;

    // --- Teleport ---
    private ICallGateSubscriber<uint, byte, bool>? _teleport;
    private ICallGateSubscriber<string, bool>? _aethernetTeleport;
    private ICallGateSubscriber<uint, bool>? _aethernetTeleportById;
    private ICallGateSubscriber<uint, bool>? _aethernetTeleportByPlaceNameId;
    private ICallGateSubscriber<uint, bool>? _housingAethernetTeleportById;
    private ICallGateSubscriber<bool>? _aethernetTeleportToFirmament;
    private ICallGateSubscriber<bool>? _teleportToFC;
    private ICallGateSubscriber<bool>? _teleportToHome;
    private ICallGateSubscriber<bool>? _teleportToApartment;

    // --- Aetheryte State ---
    private ICallGateSubscriber<uint>? _getActiveAetheryte;
    private ICallGateSubscriber<uint>? _getActiveCustomAetheryte;
    private ICallGateSubscriber<uint>? _getActiveResidentialAetheryte;

    // --- Instance ---
    private ICallGateSubscriber<bool>? _canChangeInstance;
    private ICallGateSubscriber<int>? _getNumberOfInstances;
    private ICallGateSubscriber<int, object?>? _changeInstance;
    private ICallGateSubscriber<int>? _getCurrentInstance;

    // --- Housing ---
    private ICallGateSubscriber<bool?>? _hasApartment;
    private ICallGateSubscriber<bool?>? _hasPrivateHouse;
    private ICallGateSubscriber<bool?>? _hasFreeCompanyHouse;
    private ICallGateSubscriber<bool?>? _hasSharedEstate;

    // --- Character / Login ---
    private ICallGateSubscriber<bool>? _canAutoLogin;
    private ICallGateSubscriber<string, string, int>? _changeCharacter;
    private ICallGateSubscriber<int>? _logout;
    private ICallGateSubscriber<string, string, bool>? _connectAndLogin;
    private ICallGateSubscriber<string, string, bool>? _connectAndOpenCharaSelect;

    // --- Misc ---
    private ICallGateSubscriber<string, object?>? _executeCommand;
    private ICallGateSubscriber<uint>? _getRealTerritoryType;
    private ICallGateSubscriber<uint, int?>? _getWorldChangeAetheryteByTerritoryType;

    private bool _initialized = false;
    private Timer? _retryTimer;
    private const int RetryIntervalMs = 5000;

    /// <summary>
    /// Whether the Lifestream plugin is currently available via IPC.
    /// </summary>
    public bool IsAvailable { get; private set; } = false;

    public LifestreamService(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;

        try
        {
            // Status
            _isBusy = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
            _abort = _pluginInterface.GetIpcSubscriber<object?>("Lifestream.Abort");

            // World Travel
            _canVisitSameDC = _pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.CanVisitSameDC");
            _canVisitCrossDC = _pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.CanVisitCrossDC");
            _changeWorld = _pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.ChangeWorld");
            _changeWorldById = _pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.ChangeWorldById");

            // Teleport
            _teleport = _pluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
            _aethernetTeleport = _pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.AethernetTeleport");
            _aethernetTeleportById = _pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportById");
            _aethernetTeleportByPlaceNameId = _pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportByPlaceNameId");
            _housingAethernetTeleportById = _pluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.HousingAethernetTeleportById");
            _aethernetTeleportToFirmament = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.AethernetTeleportToFirmament");
            _teleportToFC = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.TeleportToFC");
            _teleportToHome = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.TeleportToHome");
            _teleportToApartment = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.TeleportToApartment");

            // Aetheryte State
            _getActiveAetheryte = _pluginInterface.GetIpcSubscriber<uint>("Lifestream.GetActiveAetheryte");
            _getActiveCustomAetheryte = _pluginInterface.GetIpcSubscriber<uint>("Lifestream.GetActiveCustomAetheryte");
            _getActiveResidentialAetheryte = _pluginInterface.GetIpcSubscriber<uint>("Lifestream.GetActiveResidentialAetheryte");

            // Instance
            _canChangeInstance = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.CanChangeInstance");
            _getNumberOfInstances = _pluginInterface.GetIpcSubscriber<int>("Lifestream.GetNumberOfInstances");
            _changeInstance = _pluginInterface.GetIpcSubscriber<int, object?>("Lifestream.ChangeInstance");
            _getCurrentInstance = _pluginInterface.GetIpcSubscriber<int>("Lifestream.GetCurrentInstance");

            // Housing
            _hasApartment = _pluginInterface.GetIpcSubscriber<bool?>("Lifestream.HasApartment");
            _hasPrivateHouse = _pluginInterface.GetIpcSubscriber<bool?>("Lifestream.HasPrivateHouse");
            _hasFreeCompanyHouse = _pluginInterface.GetIpcSubscriber<bool?>("Lifestream.HasFreeCompanyHouse");
            _hasSharedEstate = _pluginInterface.GetIpcSubscriber<bool?>("Lifestream.HasSharedEstate");

            // Character / Login
            _canAutoLogin = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.CanAutoLogin");
            _changeCharacter = _pluginInterface.GetIpcSubscriber<string, string, int>("Lifestream.ChangeCharacter");
            _logout = _pluginInterface.GetIpcSubscriber<int>("Lifestream.Logout");
            _connectAndLogin = _pluginInterface.GetIpcSubscriber<string, string, bool>("Lifestream.ConnectAndLogin");
            _connectAndOpenCharaSelect = _pluginInterface.GetIpcSubscriber<string, string, bool>("Lifestream.ConnectAndOpenCharaSelect");

            // Misc
            _executeCommand = _pluginInterface.GetIpcSubscriber<string, object?>("Lifestream.ExecuteCommand");
            _getRealTerritoryType = _pluginInterface.GetIpcSubscriber<uint>("Lifestream.GetRealTerritoryType");
            _getWorldChangeAetheryteByTerritoryType = _pluginInterface.GetIpcSubscriber<uint, int?>("Lifestream.GetWorldChangeAetheryteByTerritoryType");

            // Test availability
            try
            {
                _isBusy.InvokeFunc();
                IsAvailable = true;
                StopRetryTimer();
                LogService.Debug(LogCategory.Lifestream, "Lifestream IPC connected");
            }
            catch (Exception)
            {
                IsAvailable = false;
                StartRetryTimer();
                LogService.Debug(LogCategory.Lifestream, "Lifestream not available, will retry");
            }

            _initialized = true;
        }
        catch (Exception)
        {
            IsAvailable = false;
            StartRetryTimer();
        }
    }

    private void StartRetryTimer()
    {
        if (_retryTimer != null) return;
        _retryTimer = new Timer(_ => TryReconnect(), null, RetryIntervalMs, RetryIntervalMs);
    }

    private void StopRetryTimer()
    {
        if (_retryTimer == null) return;
        _retryTimer.Dispose();
        _retryTimer = null;
    }

    private void TryReconnect()
    {
        if (IsAvailable)
        {
            StopRetryTimer();
            return;
        }

        try
        {
            _isBusy = _pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
            _isBusy.InvokeFunc();
            IsAvailable = true;
            StopRetryTimer();
            LogService.Debug(LogCategory.Lifestream, "Lifestream IPC reconnected");

            // Re-initialize all subscribers
            _initialized = false;
            Initialize();
        }
        catch (Exception)
        {
            // Lifestream IPC still not available, timer will retry
        }
    }

    public bool IsBusy()
    {
        if (!IsAvailable || _isBusy == null) return false;

        try
        {
            return _isBusy.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    public void Abort()
    {
        if (!IsAvailable || _abort == null) return;

        try
        {
            _abort.InvokeFunc();
            LogService.Debug(LogCategory.Lifestream, "Lifestream tasks aborted");
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    /// <summary>
    /// Checks if the specified world can be visited within the same data center.
    /// </summary>
    /// <param name="world">World name (e.g., "Adamantoise").</param>
    public bool CanVisitSameDC(string world)
    {
        if (!IsAvailable || _canVisitSameDC == null) return false;

        try
        {
            return _canVisitSameDC.InvokeFunc(world);
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Checks if the specified world can be visited via cross-data-center travel.
    /// </summary>
    /// <param name="world">World name (e.g., "Tonberry").</param>
    public bool CanVisitCrossDC(string world)
    {
        if (!IsAvailable || _canVisitCrossDC == null) return false;

        try
        {
            return _canVisitCrossDC.InvokeFunc(world);
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Requests Lifestream to change the current character's world.
    /// Automatically handles same-DC and cross-DC travel.
    /// </summary>
    /// <param name="world">Target world name.</param>
    /// <returns>True if the request was accepted.</returns>
    public bool ChangeWorld(string world)
    {
        if (!IsAvailable || _changeWorld == null) return false;

        try
        {
            var result = _changeWorld.InvokeFunc(world);
            LogService.Debug(LogCategory.Lifestream, $"ChangeWorld({world}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Requests Lifestream to change the current character's world by world ID.
    /// </summary>
    /// <param name="worldId">Lumina World sheet row ID.</param>
    /// <returns>True if the request was accepted.</returns>
    public bool ChangeWorldById(uint worldId)
    {
        if (!IsAvailable || _changeWorldById == null) return false;

        try
        {
            var result = _changeWorldById.InvokeFunc(worldId);
            LogService.Debug(LogCategory.Lifestream, $"ChangeWorldById({worldId}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Teleports to an aetheryte by its sheet row ID.
    /// </summary>
    /// <param name="destination">Aetheryte sheet row ID.</param>
    /// <param name="subIndex">Sub-index (0 for main aetheryte).</param>
    /// <returns>True if the teleport was initiated.</returns>
    public bool Teleport(uint destination, byte subIndex = 0)
    {
        if (!IsAvailable || _teleport == null) return false;

        try
        {
            var result = _teleport.InvokeFunc(destination, subIndex);
            LogService.Debug(LogCategory.Lifestream, $"Teleport({destination}, {subIndex}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Requests an aethernet teleport by destination name.
    /// Must be within an aetheryte or aetheryte shard range.
    /// </summary>
    /// <param name="destination">Aethernet destination name.</param>
    /// <returns>True if the request was accepted.</returns>
    public bool AethernetTeleport(string destination)
    {
        if (!IsAvailable || _aethernetTeleport == null) return false;

        try
        {
            var result = _aethernetTeleport.InvokeFunc(destination);
            LogService.Debug(LogCategory.Lifestream, $"AethernetTeleport({destination}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Requests an aethernet teleport by Aetheryte sheet row ID.
    /// Must be within an aetheryte or aetheryte shard range.
    /// </summary>
    /// <param name="aethernetSheetRowId">Row ID from the Aetheryte sheet.</param>
    /// <returns>True if the request was accepted.</returns>
    public bool AethernetTeleportById(uint aethernetSheetRowId)
    {
        if (!IsAvailable || _aethernetTeleportById == null) return false;

        try
        {
            var result = _aethernetTeleportById.InvokeFunc(aethernetSheetRowId);
            LogService.Debug(LogCategory.Lifestream, $"AethernetTeleportById({aethernetSheetRowId}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Requests an aethernet teleport by PlaceName sheet row ID.
    /// Must be within an aetheryte or aetheryte shard range.
    /// </summary>
    /// <param name="placeNameRowId">Row ID from the PlaceName sheet.</param>
    /// <returns>True if the request was accepted.</returns>
    public bool AethernetTeleportByPlaceNameId(uint placeNameRowId)
    {
        if (!IsAvailable || _aethernetTeleportByPlaceNameId == null) return false;

        try
        {
            var result = _aethernetTeleportByPlaceNameId.InvokeFunc(placeNameRowId);
            LogService.Debug(LogCategory.Lifestream, $"AethernetTeleportByPlaceNameId({placeNameRowId}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Requests an aethernet teleport by HousingAethernet sheet row ID.
    /// Must be within an aetheryte shard range.
    /// </summary>
    /// <param name="housingAethernetSheetRow">Row ID from the HousingAethernet sheet.</param>
    /// <returns>True if the request was accepted.</returns>
    public bool HousingAethernetTeleportById(uint housingAethernetSheetRow)
    {
        if (!IsAvailable || _housingAethernetTeleportById == null) return false;

        try
        {
            var result = _housingAethernetTeleportById.InvokeFunc(housingAethernetSheetRow);
            LogService.Debug(LogCategory.Lifestream, $"HousingAethernetTeleportById({housingAethernetSheetRow}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Requests aethernet teleport to the Firmament.
    /// Must be within Foundation aetheryte range.
    /// </summary>
    /// <returns>True if the request was accepted.</returns>
    public bool AethernetTeleportToFirmament()
    {
        if (!IsAvailable || _aethernetTeleportToFirmament == null) return false;

        try
        {
            return _aethernetTeleportToFirmament.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    public bool TeleportToFC()
    {
        if (!IsAvailable || _teleportToFC == null) return false;

        try
        {
            var result = _teleportToFC.InvokeFunc();
            LogService.Debug(LogCategory.Lifestream, $"TeleportToFC() = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    public bool TeleportToHome()
    {
        if (!IsAvailable || _teleportToHome == null) return false;

        try
        {
            var result = _teleportToHome.InvokeFunc();
            LogService.Debug(LogCategory.Lifestream, $"TeleportToHome() = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    public bool TeleportToApartment()
    {
        if (!IsAvailable || _teleportToApartment == null) return false;

        try
        {
            var result = _teleportToApartment.InvokeFunc();
            LogService.Debug(LogCategory.Lifestream, $"TeleportToApartment() = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Gets the ID of the currently active aetheryte/aetheryte shard, if any.
    /// </summary>
    /// <returns>Active aetheryte ID, or 0 if none.</returns>
    public uint GetActiveAetheryte()
    {
        if (!IsAvailable || _getActiveAetheryte == null) return 0;

        try
        {
            return _getActiveAetheryte.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return 0;
        }
    }

    /// <summary>
    /// Gets the ID of the currently active custom aetheryte, if any.
    /// </summary>
    /// <returns>Active custom aetheryte ID, or 0 if none.</returns>
    public uint GetActiveCustomAetheryte()
    {
        if (!IsAvailable || _getActiveCustomAetheryte == null) return 0;

        try
        {
            return _getActiveCustomAetheryte.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return 0;
        }
    }

    /// <summary>
    /// Gets the ID of the currently active housing aetheryte shard, if any.
    /// </summary>
    /// <returns>Active residential aetheryte ID, or 0 if none.</returns>
    public uint GetActiveResidentialAetheryte()
    {
        if (!IsAvailable || _getActiveResidentialAetheryte == null) return 0;

        try
        {
            return _getActiveResidentialAetheryte.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return 0;
        }
    }

    public bool CanChangeInstance()
    {
        if (!IsAvailable || _canChangeInstance == null) return false;

        try
        {
            return _canChangeInstance.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Gets the total number of available instances in the current zone.
    /// </summary>
    /// <returns>Number of instances, or 0 if not initialized.</returns>
    public int GetNumberOfInstances()
    {
        if (!IsAvailable || _getNumberOfInstances == null) return 0;

        try
        {
            return _getNumberOfInstances.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return 0;
        }
    }

    /// <summary>
    /// Requests a change to a specific instance number.
    /// </summary>
    /// <param name="instanceNumber">Target instance number (1-based).</param>
    public void ChangeInstance(int instanceNumber)
    {
        if (!IsAvailable || _changeInstance == null) return;

        try
        {
            _changeInstance.InvokeFunc(instanceNumber);
            LogService.Debug(LogCategory.Lifestream, $"ChangeInstance({instanceNumber})");
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    /// <summary>
    /// Gets the current instance number.
    /// </summary>
    /// <returns>Current instance number, or 0 if unavailable.</returns>
    public int GetCurrentInstance()
    {
        if (!IsAvailable || _getCurrentInstance == null) return 0;

        try
        {
            return _getCurrentInstance.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return 0;
        }
    }

    /// <summary>
    /// Checks if the current character has an apartment.
    /// </summary>
    /// <returns>True/false, or null if not on home world.</returns>
    public bool? HasApartment()
    {
        if (!IsAvailable || _hasApartment == null) return null;

        try
        {
            return _hasApartment.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return null;
        }
    }

    /// <summary>
    /// Checks if the current character has a private house.
    /// </summary>
    /// <returns>True/false, or null if not on home world.</returns>
    public bool? HasPrivateHouse()
    {
        if (!IsAvailable || _hasPrivateHouse == null) return null;

        try
        {
            return _hasPrivateHouse.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return null;
        }
    }

    /// <summary>
    /// Checks if the current character's Free Company has a house.
    /// </summary>
    /// <returns>True/false, or null if not on home world.</returns>
    public bool? HasFreeCompanyHouse()
    {
        if (!IsAvailable || _hasFreeCompanyHouse == null) return null;

        try
        {
            return _hasFreeCompanyHouse.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return null;
        }
    }

    /// <summary>
    /// Checks if the current character has access to a shared estate.
    /// </summary>
    /// <returns>True/false, or null if not on home world.</returns>
    public bool? HasSharedEstate()
    {
        if (!IsAvailable || _hasSharedEstate == null) return null;

        try
        {
            return _hasSharedEstate.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return null;
        }
    }

    public bool CanAutoLogin()
    {
        if (!IsAvailable || _canAutoLogin == null) return false;

        try
        {
            return _canAutoLogin.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Requests Lifestream to change to a different character.
    /// </summary>
    /// <param name="name">Character first and last name.</param>
    /// <param name="world">Character's home world name.</param>
    /// <returns>Lifestream ErrorCode as int (0 = Success).</returns>
    public int ChangeCharacter(string name, string world)
    {
        if (!IsAvailable || _changeCharacter == null) return -1;

        try
        {
            var result = _changeCharacter.InvokeFunc(name, world);
            LogService.Debug(LogCategory.Lifestream, $"ChangeCharacter({name}, {world}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return -1;
        }
    }

    /// <summary>
    /// Requests Lifestream to log out the current character.
    /// </summary>
    /// <returns>Lifestream ErrorCode as int (0 = Success).</returns>
    public int Logout()
    {
        if (!IsAvailable || _logout == null) return -1;

        try
        {
            var result = _logout.InvokeFunc();
            LogService.Debug(LogCategory.Lifestream, $"Logout() = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return -1;
        }
    }

    /// <summary>
    /// Connects to the lobby and logs in as the specified character.
    /// </summary>
    /// <param name="charaName">Character name.</param>
    /// <param name="charaHomeWorld">Character's home world.</param>
    /// <returns>True if the request was accepted.</returns>
    public bool ConnectAndLogin(string charaName, string charaHomeWorld)
    {
        if (!IsAvailable || _connectAndLogin == null) return false;

        try
        {
            var result = _connectAndLogin.InvokeFunc(charaName, charaHomeWorld);
            LogService.Debug(LogCategory.Lifestream, $"ConnectAndLogin({charaName}, {charaHomeWorld}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Connects to the lobby and opens the character select screen for the specified character.
    /// </summary>
    /// <param name="charaName">Character name.</param>
    /// <param name="charaHomeWorld">Character's home world.</param>
    /// <returns>True if the request was accepted.</returns>
    public bool ConnectAndOpenCharaSelect(string charaName, string charaHomeWorld)
    {
        if (!IsAvailable || _connectAndOpenCharaSelect == null) return false;

        try
        {
            var result = _connectAndOpenCharaSelect.InvokeFunc(charaName, charaHomeWorld);
            LogService.Debug(LogCategory.Lifestream, $"ConnectAndOpenCharaSelect({charaName}, {charaHomeWorld}) = {result}");
            return result;
        }
        catch (Exception)
        {
            IsAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Executes a Lifestream command as if typed in chat (e.g., "/li goto Limsa Lominsa").
    /// </summary>
    /// <param name="arguments">The command arguments (without the /li prefix).</param>
    public void ExecuteCommand(string arguments)
    {
        if (!IsAvailable || _executeCommand == null) return;

        try
        {
            _executeCommand.InvokeFunc(arguments);
            LogService.Debug(LogCategory.Lifestream, $"ExecuteCommand({arguments})");
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
    }

    /// <summary>
    /// Gets the real territory type (Lifestream's internal tracking, which handles edge cases).
    /// </summary>
    /// <returns>Territory type ID.</returns>
    public uint GetRealTerritoryType()
    {
        if (!IsAvailable || _getRealTerritoryType == null) return 0;

        try
        {
            return _getRealTerritoryType.InvokeFunc();
        }
        catch (Exception)
        {
            IsAvailable = false;
            return 0;
        }
    }

    /// <summary>
    /// Gets the world change aetheryte for a given territory type.
    /// </summary>
    /// <param name="territoryType">Territory type ID.</param>
    /// <returns>Aetheryte ID, or null.</returns>
    public int? GetWorldChangeAetheryteByTerritoryType(uint territoryType)
    {
        if (!IsAvailable || _getWorldChangeAetheryteByTerritoryType == null) return null;

        try
        {
            return _getWorldChangeAetheryteByTerritoryType.InvokeFunc(territoryType);
        }
        catch (Exception)
        {
            IsAvailable = false;
            return null;
        }
    }

    public void Dispose()
    {
        StopRetryTimer();
    }
}
