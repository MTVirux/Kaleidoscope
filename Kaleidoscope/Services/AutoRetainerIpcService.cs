using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json.Linq;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Retainer data from AutoRetainer.
/// </summary>
public record AutoRetainerRetainerData(
    string Name,
    long VentureEndsAt,
    int Level,
    uint Job,
    bool HasVenture);

/// <summary>
/// Vessel (airship/submersible) data from AutoRetainer.
/// </summary>
public record AutoRetainerVesselData(
    string Name,
    long ReturnTime,
    bool IsSubmersible);

/// <summary>
/// Extended character data from AutoRetainer including retainer and deployable information.
/// </summary>
public record AutoRetainerCharacterData(
    string Name,
    string World,
    long Gil,
    ulong CID,
    bool Enabled,
    bool WorkshopEnabled,
    List<AutoRetainerRetainerData> Retainers,
    List<AutoRetainerVesselData> Vessels,
    ulong FCID = 0,
    long FCGil = 0);

/// <summary>
/// IPC service for communicating with AutoRetainer plugin.
/// Provides access to character data and control capabilities via IPC.
/// </summary>
/// <remarks>
/// Registered as a singleton service to avoid creating multiple IPC subscriptions.
/// Automatically initializes on first access. Retries connection every 5 seconds if unavailable.
/// </remarks>
public sealed class AutoRetainerIpcService : IDisposable, IService
{
    private readonly IDalamudPluginInterface _pluginInterface;
    
    // Data access
    private ICallGateSubscriber<List<ulong>>? _getRegisteredCIDs;
    private ICallGateSubscriber<ulong, object?>? _getOfflineCharacterData;
    private ICallGateSubscriber<object?, object?>? _writeOfflineCharacterData;
    private ICallGateSubscriber<Dictionary<ulong, HashSet<string>>>? _getEnabledRetainers;
    
    // Plugin state queries
    private ICallGateSubscriber<bool>? _isBusy;
    private ICallGateSubscriber<bool>? _getSuppressed;
    private ICallGateSubscriber<bool>? _getMultiModeEnabled;
    private ICallGateSubscriber<bool>? _areAnyRetainersAvailable;
    private ICallGateSubscriber<int>? _getInventoryFreeSlotCount;
    private ICallGateSubscriber<bool>? _canAutoLogin;
    private ICallGateSubscriber<ulong, long?>? _getClosestRetainerVentureSecondsRemaining;
    
    // Plugin control actions
    private ICallGateSubscriber<bool, object?>? _setSuppressed;
    private ICallGateSubscriber<bool, object?>? _setMultiModeEnabled;
    private ICallGateSubscriber<object?>? _abortAllTasks;
    private ICallGateSubscriber<object?>? _disableAllFunctions;
    private ICallGateSubscriber<object?>? _enableMultiMode;
    private ICallGateSubscriber<string, bool>? _relog;
    
    private bool _initialized = false;
    private Timer? _retryTimer;
    private const int RetryIntervalMs = 5000;

    public bool IsAvailable { get; private set; } = false;

    /// <summary>
    /// Creates and initializes the AutoRetainer IPC service.
    /// </summary>
    public AutoRetainerIpcService(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;
        
        try
        {
            // Data access subscribers
            _getRegisteredCIDs = _pluginInterface.GetIpcSubscriber<List<ulong>>("AutoRetainer.GetRegisteredCIDs");
            _getOfflineCharacterData = _pluginInterface.GetIpcSubscriber<ulong, object?>("AutoRetainer.GetOfflineCharacterData");
            _writeOfflineCharacterData = _pluginInterface.GetIpcSubscriber<object?, object?>("AutoRetainer.WriteOfflineCharacterData");
            
            // Plugin state subscribers (AutoRetainer.PluginState.*)
            _getEnabledRetainers = _pluginInterface.GetIpcSubscriber<Dictionary<ulong, HashSet<string>>>("AutoRetainer.PluginState.GetEnabledRetainers");
            _isBusy = _pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy");
            _areAnyRetainersAvailable = _pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.AreAnyRetainersAvailableForCurrentChara");
            _getInventoryFreeSlotCount = _pluginInterface.GetIpcSubscriber<int>("AutoRetainer.PluginState.GetInventoryFreeSlotCount");
            _canAutoLogin = _pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.CanAutoLogin");
            _getClosestRetainerVentureSecondsRemaining = _pluginInterface.GetIpcSubscriber<ulong, long?>("AutoRetainer.PluginState.GetClosestRetainerVentureSecondsRemaining");
            
            // Legacy API subscribers
            _getSuppressed = _pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetSuppressed");
            _getMultiModeEnabled = _pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetMultiModeEnabled");
            _setSuppressed = _pluginInterface.GetIpcSubscriber<bool, object?>("AutoRetainer.SetSuppressed");
            _setMultiModeEnabled = _pluginInterface.GetIpcSubscriber<bool, object?>("AutoRetainer.SetMultiModeEnabled");
            
            // Plugin control subscribers (AutoRetainer.PluginState.*)
            _abortAllTasks = _pluginInterface.GetIpcSubscriber<object?>("AutoRetainer.PluginState.AbortAllTasks");
            _disableAllFunctions = _pluginInterface.GetIpcSubscriber<object?>("AutoRetainer.PluginState.DisableAllFunctions");
            _enableMultiMode = _pluginInterface.GetIpcSubscriber<object?>("AutoRetainer.PluginState.EnableMultiMode");
            _relog = _pluginInterface.GetIpcSubscriber<string, bool>("AutoRetainer.PluginState.Relog");
            
            // Test if AutoRetainer is available by trying to call the IPC
            try
            {
                var cids = _getRegisteredCIDs.InvokeFunc();
                IsAvailable = true;
                StopRetryTimer();
#if AUTORETAINER_VERBOSE_LOGGING
                LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer IPC connected, found {cids?.Count ?? 0} registered CIDs");
#endif
            }
            catch (Exception)
            {
                IsAvailable = false;
                StartRetryTimer();
#if AUTORETAINER_VERBOSE_LOGGING
                LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer not available: {ex.Message}");
#endif
            }
            
            _initialized = true;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to initialize AutoRetainer IPC: {ex.Message}");
#endif
            IsAvailable = false;
            StartRetryTimer();
        }
    }

    private void StartRetryTimer()
    {
        if (_retryTimer != null) return;
        
        _retryTimer = new Timer(_ => TryReconnect(), null, RetryIntervalMs, RetryIntervalMs);
#if AUTORETAINER_VERBOSE_LOGGING
        LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer IPC retry timer started (interval: {RetryIntervalMs}ms)");
#endif
    }

    private void StopRetryTimer()
    {
        if (_retryTimer == null) return;
        
        _retryTimer.Dispose();
        _retryTimer = null;
#if AUTORETAINER_VERBOSE_LOGGING
        LogService.Verbose(LogCategory.AutoRetainer, "AutoRetainer IPC retry timer stopped");
#endif
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
            // Re-create subscribers in case AutoRetainer was loaded after us
            _getRegisteredCIDs = _pluginInterface.GetIpcSubscriber<List<ulong>>("AutoRetainer.GetRegisteredCIDs");
            
            var cids = _getRegisteredCIDs.InvokeFunc();
            IsAvailable = true;
            StopRetryTimer();
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer IPC reconnected, found {cids?.Count ?? 0} registered CIDs");
#endif
            
            // Re-initialize all subscribers now that AutoRetainer is available
            _initialized = false;
            Initialize();
        }
        catch
        {
            // Still not available, timer will try again
        }
    }

    /// <summary>
    /// Gets all registered character IDs from AutoRetainer.
    /// </summary>
    public List<ulong>? GetRegisteredCharacterIds()
    {
        if (!IsAvailable || _getRegisteredCIDs == null) return null;
        
        try
        {
            var cids = _getRegisteredCIDs.InvokeFunc();
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer returned {cids?.Count ?? 0} CIDs");
#endif
            return cids;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to get registered CIDs from AutoRetainer: {ex.Message}");
#endif
            IsAvailable = false;
            return null;
        }
    }

    /// <summary>
    /// Gets offline character data from AutoRetainer for a specific character.
    /// Returns a dynamic object with Name, World, Gil, CID properties.
    /// </summary>
    public (string Name, string World, long Gil, ulong CID)? GetCharacterData(ulong cid)
    {
        var fullData = GetFullCharacterData(cid);
        if (fullData == null) return null;
        return (fullData.Name, fullData.World, fullData.Gil, fullData.CID);
    }

    /// <summary>
    /// Gets full offline character data from AutoRetainer for a specific character,
    /// including retainer information.
    /// </summary>
    public AutoRetainerCharacterData? GetFullCharacterData(ulong cid)
    {
        if (!IsAvailable || _getOfflineCharacterData == null) return null;
        
        try
        {
            var data = _getOfflineCharacterData.InvokeFunc(cid);
            if (data == null)
            {
#if AUTORETAINER_VERBOSE_LOGGING
                LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer returned null data for CID {cid}");
#endif
                return null;
            }
            
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer data type for CID {cid}: {data.GetType().FullName}");
#endif
            
            string name = "";
            string world = "";
            long gil = 0;
            bool enabled = false;
            bool workshopEnabled = false;
            ulong fcid = 0;
            var retainers = new List<AutoRetainerRetainerData>();
            var vessels = new List<AutoRetainerVesselData>();
            
            // Check if the data is a JObject (JSON)
            if (data is JObject jObject)
            {
                name = jObject["Name"]?.Value<string>() ?? "";
                world = jObject["World"]?.Value<string>() ?? "";
                gil = jObject["Gil"]?.Value<long>() ?? 0L;
                enabled = jObject["Enabled"]?.Value<bool>() ?? false;
                workshopEnabled = jObject["WorkshopEnabled"]?.Value<bool>() ?? false;
                
                // FCID needs careful parsing as it's a ulong
                var fcidToken = jObject["FCID"];
                if (fcidToken != null)
                {
                    try { fcid = fcidToken.Value<ulong>(); }
                    catch (Exception) { fcid = 0; } // FCID can be in various formats, default to 0 on parse failure
                }
                
                // Parse retainer data
                var retainerData = jObject["RetainerData"] as JArray;
                if (retainerData != null)
                {
                    foreach (var retainer in retainerData)
                    {
                        var retainerName = retainer["Name"]?.Value<string>() ?? "";
                        var ventureEndsAt = retainer["VentureEndsAt"]?.Value<long>() ?? 0L;
                        var level = retainer["Level"]?.Value<int>() ?? 0;
                        var job = retainer["Job"]?.Value<uint>() ?? 0;
                        var hasVenture = retainer["HasVenture"]?.Value<bool>() ?? false;
                        
                        if (!string.IsNullOrEmpty(retainerName))
                        {
                            retainers.Add(new AutoRetainerRetainerData(retainerName, ventureEndsAt, level, job, hasVenture));
                        }
                    }
                }
                
                // Parse vessel data (submersibles and airships)
                ParseVesselsFromJArray(jObject["OfflineSubmarineData"] as JArray, vessels, isSubmersible: true);
                ParseVesselsFromJArray(jObject["OfflineAirshipData"] as JArray, vessels, isSubmersible: false);
            }
            else
            {
                // Fallback to reflection for regular objects
                var type = data.GetType();
                
                name = type.GetProperty("Name")?.GetValue(data) as string ?? "";
                world = type.GetProperty("World")?.GetValue(data) as string ?? "";
                var gilProp = type.GetProperty("Gil")?.GetValue(data);
                gil = gilProp != null ? Convert.ToInt64(gilProp) : 0L;
                
                var enabledProp = type.GetProperty("Enabled")?.GetValue(data);
                enabled = enabledProp is bool b && b;
                
                var workshopProp = type.GetProperty("WorkshopEnabled")?.GetValue(data);
                workshopEnabled = workshopProp is bool w && w;
                
                // FCID needs careful parsing
                try
                {
                    var fcidProp = type.GetProperty("FCID")?.GetValue(data);
                    fcid = fcidProp != null ? Convert.ToUInt64(fcidProp) : 0UL;
                }
                catch (Exception) { fcid = 0; } // FCID type may vary, default to 0 on conversion failure
                
                // Try to get retainer data via reflection
                var retainerDataProp = type.GetProperty("RetainerData")?.GetValue(data);
                if (retainerDataProp is System.Collections.IEnumerable retainerList)
                {
                    foreach (var retainer in retainerList)
                    {
                        var rType = retainer.GetType();
                        var retainerName = rType.GetProperty("Name")?.GetValue(retainer) as string ?? "";
                        var ventureEndsAtProp = rType.GetProperty("VentureEndsAt")?.GetValue(retainer);
                        var ventureEndsAt = ventureEndsAtProp != null ? Convert.ToInt64(ventureEndsAtProp) : 0L;
                        var levelProp = rType.GetProperty("Level")?.GetValue(retainer);
                        var level = levelProp != null ? Convert.ToInt32(levelProp) : 0;
                        var jobProp = rType.GetProperty("Job")?.GetValue(retainer);
                        var job = jobProp != null ? Convert.ToUInt32(jobProp) : 0u;
                        var hasVentureProp = rType.GetProperty("HasVenture")?.GetValue(retainer);
                        var hasVenture = hasVentureProp is bool hv && hv;
                        
                        if (!string.IsNullOrEmpty(retainerName))
                        {
                            retainers.Add(new AutoRetainerRetainerData(retainerName, ventureEndsAt, level, job, hasVenture));
                        }
                    }
                }
                
                // Parse vessel data via reflection (submersibles and airships)
                ParseVesselsFromReflection(
                    type.GetProperty("OfflineSubmarineData")?.GetValue(data) as System.Collections.IEnumerable, 
                    vessels, 
                    isSubmersible: true);
                ParseVesselsFromReflection(
                    type.GetProperty("OfflineAirshipData")?.GetValue(data) as System.Collections.IEnumerable, 
                    vessels, 
                    isSubmersible: false);
            }
            
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer character: {name}@{world}, Gil: {gil}, FCID: {fcid}, Retainers: {retainers.Count}, Vessels: {vessels.Count}");
#endif
            
            // Note: FC gil is not available via IPC - FCData is stored separately in AutoRetainer
            return new AutoRetainerCharacterData(name, world, gil, cid, enabled, workshopEnabled, retainers, vessels, fcid);
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to get character data from AutoRetainer for CID {cid}: {ex.Message}\n{ex.StackTrace}");
#endif
            return null;
        }
    }

    /// <summary>
    /// Gets all character data from AutoRetainer.
    /// </summary>
    public List<(string Name, string World, long Gil, ulong CID)> GetAllCharacterData()
    {
        return GetAllFullCharacterData()
            .Select(c => (c.Name, c.World, c.Gil, c.CID))
            .ToList();
    }

    /// <summary>
    /// Gets all character data from AutoRetainer with full retainer information.
    /// </summary>
    public List<AutoRetainerCharacterData> GetAllFullCharacterData()
    {
        var result = new List<AutoRetainerCharacterData>();
        
        var cids = GetRegisteredCharacterIds();
        if (cids == null || cids.Count == 0)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, "AutoRetainer returned no CIDs");
#endif
            return result;
        }
        
#if AUTORETAINER_VERBOSE_LOGGING
        LogService.Verbose(LogCategory.AutoRetainer, $"Processing {cids.Count} CIDs from AutoRetainer");
#endif
        
        foreach (var cid in cids)
        {
            var charData = GetFullCharacterData(cid);
            if (charData != null && !string.IsNullOrEmpty(charData.Name))
            {
                result.Add(charData);
            }
        }
        
#if AUTORETAINER_VERBOSE_LOGGING
        LogService.Verbose(LogCategory.AutoRetainer, $"Returning {result.Count} characters from AutoRetainer");
#endif
        return result;
    }

    /// <summary>
    /// Refreshes the connection to AutoRetainer.
    /// </summary>
    public void Refresh()
    {
        _initialized = false;
        Initialize();
    }

    #region IPC Helper Methods

    /// <summary>
    /// Safely invokes an IPC function, returning null on failure.
    /// </summary>
    private T? SafeInvoke<T>(ICallGateSubscriber<T>? subscriber, string methodName) where T : class
    {
        if (!IsAvailable || subscriber == null) return null;
        
        try
        {
            return subscriber.InvokeFunc();
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to invoke {methodName} from AutoRetainer: {ex.Message}");
#endif
            return null;
        }
    }

    /// <summary>
    /// Safely invokes an IPC function that returns a value type, returning null on failure.
    /// </summary>
    private T? SafeInvokeValue<T>(ICallGateSubscriber<T>? subscriber, string methodName) where T : struct
    {
        if (!IsAvailable || subscriber == null) return null;
        
        try
        {
            return subscriber.InvokeFunc();
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to invoke {methodName} from AutoRetainer: {ex.Message}");
#endif
            return null;
        }
    }

    /// <summary>
    /// Safely invokes an IPC function with one argument, returning null on failure.
    /// </summary>
    private TResult? SafeInvokeValue<TArg, TResult>(ICallGateSubscriber<TArg, TResult>? subscriber, TArg arg, string methodName) where TResult : struct
    {
        if (!IsAvailable || subscriber == null) return null;
        
        try
        {
            return subscriber.InvokeFunc(arg);
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to invoke {methodName} from AutoRetainer: {ex.Message}");
#endif
            return null;
        }
    }

    /// <summary>
    /// Safely invokes an IPC function with one argument that already returns a nullable type.
    /// </summary>
    private TResult? SafeInvokeNullable<TArg, TResult>(ICallGateSubscriber<TArg, TResult?>? subscriber, TArg arg, string methodName) where TResult : struct
    {
        if (!IsAvailable || subscriber == null) return null;
        
        try
        {
            return subscriber.InvokeFunc(arg);
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to invoke {methodName} from AutoRetainer: {ex.Message}");
#endif
            return null;
        }
    }

    /// <summary>
    /// Parses vessel (submersible/airship) data from a JArray.
    /// </summary>
    private static void ParseVesselsFromJArray(JArray? vesselArray, List<AutoRetainerVesselData> vessels, bool isSubmersible)
    {
        if (vesselArray == null) return;
        
        foreach (var vessel in vesselArray)
        {
            var vesselName = vessel["Name"]?.Value<string>() ?? "";
            var returnTime = vessel["ReturnTime"]?.Value<long>() ?? 0L;
            
            if (!string.IsNullOrEmpty(vesselName))
            {
                vessels.Add(new AutoRetainerVesselData(vesselName, returnTime, isSubmersible));
            }
        }
    }

    /// <summary>
    /// Parses vessel (submersible/airship) data via reflection.
    /// </summary>
    private static void ParseVesselsFromReflection(System.Collections.IEnumerable? vesselList, List<AutoRetainerVesselData> vessels, bool isSubmersible)
    {
        if (vesselList == null) return;
        
        foreach (var vessel in vesselList)
        {
            var vType = vessel.GetType();
            var vesselName = vType.GetProperty("Name")?.GetValue(vessel) as string ?? "";
            var returnTimeProp = vType.GetProperty("ReturnTime")?.GetValue(vessel);
            var returnTime = returnTimeProp != null ? Convert.ToInt64(returnTimeProp) : 0L;
            
            if (!string.IsNullOrEmpty(vesselName))
            {
                vessels.Add(new AutoRetainerVesselData(vesselName, returnTime, isSubmersible));
            }
        }
    }

    #endregion

    #region Plugin State Queries

    /// <summary>
    /// Checks if AutoRetainer is currently busy processing tasks.
    /// </summary>
    public bool? IsBusy() => SafeInvokeValue(_isBusy, nameof(IsBusy));

    /// <summary>
    /// Checks if AutoRetainer is currently suppressed.
    /// </summary>
    public bool? GetSuppressed() => SafeInvokeValue(_getSuppressed, nameof(GetSuppressed));

    /// <summary>
    /// Checks if Multi-Mode is currently enabled.
    /// </summary>
    public bool? GetMultiModeEnabled() => SafeInvokeValue(_getMultiModeEnabled, nameof(GetMultiModeEnabled));

    /// <summary>
    /// Checks if any retainers are available for the current character.
    /// </summary>
    public bool? AreAnyRetainersAvailable() => SafeInvokeValue(_areAnyRetainersAvailable, nameof(AreAnyRetainersAvailable));

    /// <summary>
    /// Gets the number of free inventory slots.
    /// </summary>
    public int? GetInventoryFreeSlotCount() => SafeInvokeValue(_getInventoryFreeSlotCount, nameof(GetInventoryFreeSlotCount));

    /// <summary>
    /// Checks if auto-login is possible.
    /// </summary>
    public bool? CanAutoLogin() => SafeInvokeValue(_canAutoLogin, nameof(CanAutoLogin));

    /// <summary>
    /// Gets the seconds remaining until the closest retainer venture completes.
    /// </summary>
    public long? GetClosestRetainerVentureSecondsRemaining(ulong cid) 
        => SafeInvokeNullable(_getClosestRetainerVentureSecondsRemaining, cid, nameof(GetClosestRetainerVentureSecondsRemaining));

    /// <summary>
    /// Gets enabled retainers for all characters.
    /// </summary>
    public Dictionary<ulong, HashSet<string>>? GetEnabledRetainers() 
        => SafeInvoke(_getEnabledRetainers, nameof(GetEnabledRetainers));

    /// <summary>
    /// Gets the set of enabled retainer names for a specific character.
    /// </summary>
    /// <param name="cid">Character content ID</param>
    /// <returns>HashSet of enabled retainer names, or empty set if none/not available</returns>
    public HashSet<string> GetEnabledRetainersForCharacter(ulong cid)
    {
        var allEnabled = GetEnabledRetainers();
        if (allEnabled != null && allEnabled.TryGetValue(cid, out var retainerNames))
        {
            return retainerNames;
        }
        return new HashSet<string>();
    }

    /// <summary>
    /// Checks if a specific retainer is enabled for a character.
    /// </summary>
    /// <param name="cid">Character content ID</param>
    /// <param name="retainerName">Retainer name</param>
    /// <returns>True if the retainer is enabled</returns>
    public bool IsRetainerEnabled(ulong cid, string retainerName)
    {
        var enabledRetainers = GetEnabledRetainersForCharacter(cid);
        return enabledRetainers.Contains(retainerName);
    }

    #endregion

    #region Plugin Control Actions

    /// <summary>
    /// Sets the suppressed state of AutoRetainer.
    /// </summary>
    public bool SetSuppressed(bool suppressed)
    {
        if (!IsAvailable || _setSuppressed == null) return false;
        
        try
        {
            _setSuppressed.InvokeAction(suppressed);
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer suppressed set to: {suppressed}");
#endif
            return true;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to set suppressed on AutoRetainer: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Enables or disables Multi-Mode.
    /// </summary>
    public bool SetMultiModeEnabled(bool enabled)
    {
        if (!IsAvailable || _setMultiModeEnabled == null) return false;
        
        try
        {
            _setMultiModeEnabled.InvokeAction(enabled);
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer Multi-Mode set to: {enabled}");
#endif
            return true;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to set Multi-Mode on AutoRetainer: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Aborts all pending tasks in AutoRetainer.
    /// </summary>
    public bool AbortAllTasks()
    {
        if (!IsAvailable || _abortAllTasks == null) return false;
        
        try
        {
            _abortAllTasks.InvokeAction();
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, "AutoRetainer tasks aborted");
#endif
            return true;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to abort tasks on AutoRetainer: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Disables all AutoRetainer functions (Multi-Mode, Scheduler, Voyage Scheduler).
    /// </summary>
    public bool DisableAllFunctions()
    {
        if (!IsAvailable || _disableAllFunctions == null) return false;
        
        try
        {
            _disableAllFunctions.InvokeAction();
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, "AutoRetainer all functions disabled");
#endif
            return true;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to disable all functions on AutoRetainer: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Enables Multi-Mode via command.
    /// </summary>
    public bool EnableMultiMode()
    {
        if (!IsAvailable || _enableMultiMode == null) return false;
        
        try
        {
            _enableMultiMode.InvokeAction();
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, "AutoRetainer Multi-Mode enabled");
#endif
            return true;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to enable Multi-Mode on AutoRetainer: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Relogs to a specific character.
    /// </summary>
    /// <param name="characterNameWithWorld">Character name in format "Name@World"</param>
    /// <returns>True if relog was initiated successfully</returns>
    public bool Relog(string characterNameWithWorld)
    {
        if (!IsAvailable || _relog == null) return false;
        
        try
        {
            var result = _relog.InvokeFunc(characterNameWithWorld);
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer relog to {characterNameWithWorld}: {result}");
#endif
            return result;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to relog via AutoRetainer: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Sets whether a character is enabled for retainer multi-mode.
    /// </summary>
    /// <param name="cid">Character content ID</param>
    /// <param name="enabled">Whether retainer multi-mode is enabled</param>
    /// <returns>True if the setting was updated successfully</returns>
    public bool SetCharacterRetainersEnabled(ulong cid, bool enabled)
    {
        if (!IsAvailable || _getOfflineCharacterData == null || _writeOfflineCharacterData == null) return false;
        
        try
        {
            var data = _getOfflineCharacterData.InvokeFunc(cid);
            if (data == null) return false;
            
            // Modify the Enabled property via reflection or JObject
            if (data is JObject jObject)
            {
                jObject["Enabled"] = enabled;
                _writeOfflineCharacterData.InvokeAction(jObject);
            }
            else
            {
                var type = data.GetType();
                var enabledField = type.GetField("Enabled") ?? type.GetProperty("Enabled")?.DeclaringType?.GetField("Enabled");
                if (enabledField != null)
                {
                    enabledField.SetValue(data, enabled);
                    _writeOfflineCharacterData.InvokeAction(data);
                }
                else
                {
                    return false;
                }
            }
            
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer character {cid} retainers enabled set to: {enabled}");
#endif
            return true;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to set character retainers enabled on AutoRetainer: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Sets whether a character is enabled for deployables/workshop multi-mode.
    /// </summary>
    /// <param name="cid">Character content ID</param>
    /// <param name="enabled">Whether deployables multi-mode is enabled</param>
    /// <returns>True if the setting was updated successfully</returns>
    public bool SetCharacterDeployablesEnabled(ulong cid, bool enabled)
    {
        if (!IsAvailable || _getOfflineCharacterData == null || _writeOfflineCharacterData == null) return false;
        
        try
        {
            var data = _getOfflineCharacterData.InvokeFunc(cid);
            if (data == null) return false;
            
            // Modify the WorkshopEnabled property via reflection or JObject
            if (data is JObject jObject)
            {
                jObject["WorkshopEnabled"] = enabled;
                _writeOfflineCharacterData.InvokeAction(jObject);
            }
            else
            {
                var type = data.GetType();
                var workshopField = type.GetField("WorkshopEnabled") ?? type.GetProperty("WorkshopEnabled")?.DeclaringType?.GetField("WorkshopEnabled");
                if (workshopField != null)
                {
                    workshopField.SetValue(data, enabled);
                    _writeOfflineCharacterData.InvokeAction(data);
                }
                else
                {
                    return false;
                }
            }
            
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"AutoRetainer character {cid} deployables enabled set to: {enabled}");
#endif
            return true;
        }
        catch (Exception)
        {
#if AUTORETAINER_VERBOSE_LOGGING
            LogService.Verbose(LogCategory.AutoRetainer, $"Failed to set character deployables enabled on AutoRetainer: {ex.Message}");
#endif
            return false;
        }
    }

    /// <summary>
    /// Sets whether an individual retainer is enabled for a character.
    /// Note: AutoRetainer does not expose an IPC to modify individual retainer enabled state.
    /// This method always returns false as the functionality is not available via IPC.
    /// </summary>
    /// <param name="cid">Character content ID</param>
    /// <param name="retainerName">Retainer name</param>
    /// <param name="enabled">Whether the retainer is enabled</param>
    /// <returns>Always returns false - individual retainer control not available via IPC</returns>
    public bool SetRetainerEnabled(ulong cid, string retainerName, bool enabled)
    {
        // AutoRetainer stores SelectedRetainers in its Config (C.SelectedRetainers), which is
        // a Dictionary<ulong, HashSet<string>> mapping CID to enabled retainer names.
        // This is separate from OfflineCharacterData and there's no IPC exposed to modify it.
        // The WriteOfflineCharacterData IPC only writes character data, not the config.
#if AUTORETAINER_VERBOSE_LOGGING
        LogService.Verbose(LogCategory.AutoRetainer, $"Cannot set retainer '{retainerName}' enabled state via IPC - AutoRetainer does not expose this functionality. Use AutoRetainer UI directly.");
#endif
        return false;
    }

    #endregion

    public void Dispose()
    {
        StopRetryTimer();
    }
}


