using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Kaleidoscope.Services.Common;
using Newtonsoft.Json.Linq;
using OtterGui.Services;

namespace Kaleidoscope.Services;

public record AutoRetainerRetainerData(
    string Name,
    long VentureEndsAt,
    int Level,
    uint Job,
    bool HasVenture,
    ulong RetainerId,
    long Gil);

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
public sealed class AutoRetainerService : IDisposable, IService
{
    private readonly IDalamudPluginInterface _pluginInterface;
    
    private ICallGateSubscriber<List<ulong>>? _getRegisteredCIDs;
    private ICallGateSubscriber<ulong, object?>? _getOfflineCharacterData;
    private ICallGateSubscriber<object?, object?>? _writeOfflineCharacterData;
    private ICallGateSubscriber<Dictionary<ulong, HashSet<string>>>? _getEnabledRetainers;
    
    private ICallGateSubscriber<bool>? _isBusy;
    private ICallGateSubscriber<bool>? _getSuppressed;
    private ICallGateSubscriber<bool>? _getMultiModeEnabled;
    private ICallGateSubscriber<bool>? _areAnyRetainersAvailable;
    private ICallGateSubscriber<int>? _getInventoryFreeSlotCount;
    private ICallGateSubscriber<bool>? _canAutoLogin;
    private ICallGateSubscriber<ulong, long?>? _getClosestRetainerVentureSecondsRemaining;
    
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

    public AutoRetainerService(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;
        
        try
        {
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
            
            try
            {
                var cids = _getRegisteredCIDs.InvokeFunc();
                IsAvailable = true;
                StopRetryTimer();
            }
            catch (Exception)
            {
                IsAvailable = false;
                StartRetryTimer();
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
            // Re-create subscribers in case AutoRetainer was loaded after us
            _getRegisteredCIDs = _pluginInterface.GetIpcSubscriber<List<ulong>>("AutoRetainer.GetRegisteredCIDs");
            
            var cids = _getRegisteredCIDs.InvokeFunc();
            IsAvailable = true;
            StopRetryTimer();
            
            // Re-initialize all subscribers now that AutoRetainer is available
            _initialized = false;
            Initialize();
        }
        catch
        {
            // Still not available, timer will try again
        }
    }

    public List<ulong>? GetRegisteredCharacterIds()
    {
        if (!IsAvailable || _getRegisteredCIDs == null) return null;
        
        try
        {
            var cids = _getRegisteredCIDs.InvokeFunc();
            return cids;
        }
        catch (Exception)
        {
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

    public AutoRetainerCharacterData? GetFullCharacterData(ulong cid)
    {
        if (!IsAvailable || _getOfflineCharacterData == null) return null;

        try
        {
            var data = _getOfflineCharacterData.InvokeFunc(cid);
            if (data == null)
            {
                return null;
            }

            // Single field-access path regardless of whether AutoRetainer hands us a JObject or a
            // plain reflected object — the reader hides the representation difference.
            var reader = data is JObject jObject
                ? (FieldReader)new JTokenFieldReader(jObject)
                : new ReflectionFieldReader(data);

            var retainers = new List<AutoRetainerRetainerData>();
            foreach (var retainer in reader.GetArray("RetainerData"))
            {
                var retainerName = retainer.GetString("Name");
                if (string.IsNullOrEmpty(retainerName)) continue;

                retainers.Add(new AutoRetainerRetainerData(
                    retainerName,
                    retainer.GetLong("VentureEndsAt"),
                    retainer.GetInt("Level"),
                    retainer.GetUInt("Job"),
                    retainer.GetBool("HasVenture"),
                    // RetainerId — try multiple field names (AR may expose any of these)
                    retainer.GetFirstNonZeroUlong("RetainerID", "RetainerId", "Id"),
                    retainer.GetLong("Gil")));
            }

            var vessels = new List<AutoRetainerVesselData>();
            ParseVessels(reader, "OfflineSubmarineData", vessels, isSubmersible: true);
            ParseVessels(reader, "OfflineAirshipData", vessels, isSubmersible: false);

            return new AutoRetainerCharacterData(
                reader.GetString("Name"),
                reader.GetString("World"),
                reader.GetLong("Gil"),
                cid,
                reader.GetBool("Enabled"),
                reader.GetBool("WorkshopEnabled"),
                retainers,
                vessels,
                reader.GetUlong("FCID"),
                ReadFreeCompanyGil(reader));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads Free Company gil, probing multiple likely field names since AutoRetainer's exact key
    /// is unconfirmed. Checks direct fields first, then a nested FC object.
    /// </summary>
    private static long ReadFreeCompanyGil(FieldReader reader)
    {
        if (reader.TryGetField<long>("FCGil", out var direct) || reader.TryGetField<long>("FreeCompanyGil", out direct))
        {
            return direct;
        }

        foreach (var nested in new[] { "FCData", "FreeCompanyData", "OfflineFCData" })
        {
            var fcObj = reader.GetChild(nested);
            if (fcObj == null) continue;
            if (fcObj.TryGetField<long>("Gil", out var inner) || fcObj.TryGetField<long>("FCGil", out inner))
            {
                return inner;
            }
        }

        return 0;
    }

    public List<(string Name, string World, long Gil, ulong CID)> GetAllCharacterData()
    {
        return GetAllFullCharacterData()
            .Select(c => (c.Name, c.World, c.Gil, c.CID))
            .ToList();
    }

    public List<AutoRetainerCharacterData> GetAllFullCharacterData()
    {
        var result = new List<AutoRetainerCharacterData>();
        
        var cids = GetRegisteredCharacterIds();
        if (cids == null || cids.Count == 0)
        {
            return result;
        }
        
        
        foreach (var cid in cids)
        {
            var charData = GetFullCharacterData(cid);
            if (charData != null && !string.IsNullOrEmpty(charData.Name))
            {
                result.Add(charData);
            }
        }
        
        return result;
    }

    public void Refresh()
    {
        _initialized = false;
        Initialize();
    }

    private T? SafeInvoke<T>(ICallGateSubscriber<T>? subscriber) where T : class
        => IpcInvoker.Invoke<T?>(IsAvailable && subscriber != null, () => subscriber!.InvokeFunc(), null);

    private T? SafeInvokeValue<T>(ICallGateSubscriber<T>? subscriber) where T : struct
        => IpcInvoker.Invoke<T?>(IsAvailable && subscriber != null, () => subscriber!.InvokeFunc(), null);

    private TResult? SafeInvokeValue<TArg, TResult>(ICallGateSubscriber<TArg, TResult>? subscriber, TArg arg) where TResult : struct
        => IpcInvoker.Invoke<TResult?>(IsAvailable && subscriber != null, () => subscriber!.InvokeFunc(arg), null);

    private TResult? SafeInvokeNullable<TArg, TResult>(ICallGateSubscriber<TArg, TResult?>? subscriber, TArg arg) where TResult : struct
        => IpcInvoker.Invoke<TResult?>(IsAvailable && subscriber != null, () => subscriber!.InvokeFunc(arg), null);

    private bool SafeInvokeAction(ICallGateSubscriber<object?>? subscriber)
        => IpcInvoker.TryInvoke(IsAvailable && subscriber != null, () => subscriber!.InvokeAction());

    private bool SafeInvokeAction<TArg>(ICallGateSubscriber<TArg, object?>? subscriber, TArg arg)
        => IpcInvoker.TryInvoke(IsAvailable && subscriber != null, () => subscriber!.InvokeAction(arg));

    private static void ParseVessels(FieldReader reader, string field, List<AutoRetainerVesselData> vessels, bool isSubmersible)
    {
        foreach (var vessel in reader.GetArray(field))
        {
            var vesselName = vessel.GetString("Name");
            if (string.IsNullOrEmpty(vesselName)) continue;

            vessels.Add(new AutoRetainerVesselData(vesselName, vessel.GetLong("ReturnTime"), isSubmersible));
        }
    }

    public bool? IsBusy() => SafeInvokeValue(_isBusy);
    public bool? GetSuppressed() => SafeInvokeValue(_getSuppressed);
    public bool? GetMultiModeEnabled() => SafeInvokeValue(_getMultiModeEnabled);
    public bool? AreAnyRetainersAvailable() => SafeInvokeValue(_areAnyRetainersAvailable);
    public int? GetInventoryFreeSlotCount() => SafeInvokeValue(_getInventoryFreeSlotCount);
    public bool? CanAutoLogin() => SafeInvokeValue(_canAutoLogin);

    public long? GetClosestRetainerVentureSecondsRemaining(ulong cid)
        => SafeInvokeNullable(_getClosestRetainerVentureSecondsRemaining, cid);

    public Dictionary<ulong, HashSet<string>>? GetEnabledRetainers()
        => SafeInvoke(_getEnabledRetainers);

    public HashSet<string> GetEnabledRetainersForCharacter(ulong cid)
    {
        var allEnabled = GetEnabledRetainers();
        if (allEnabled != null && allEnabled.TryGetValue(cid, out var retainerNames))
        {
            return retainerNames;
        }
        return new HashSet<string>();
    }

    public bool IsRetainerEnabled(ulong cid, string retainerName)
    {
        var enabledRetainers = GetEnabledRetainersForCharacter(cid);
        return enabledRetainers.Contains(retainerName);
    }

    public bool SetSuppressed(bool suppressed) => SafeInvokeAction(_setSuppressed, suppressed);

    public bool SetMultiModeEnabled(bool enabled) => SafeInvokeAction(_setMultiModeEnabled, enabled);

    public bool AbortAllTasks() => SafeInvokeAction(_abortAllTasks);

    /// <summary>
    /// Disables all AutoRetainer functions (Multi-Mode, Scheduler, Voyage Scheduler).
    /// </summary>
    public bool DisableAllFunctions() => SafeInvokeAction(_disableAllFunctions);

    public bool EnableMultiMode() => SafeInvokeAction(_enableMultiMode);

    /// <summary>
    /// Relogs to a specific character.
    /// </summary>
    /// <param name="characterNameWithWorld">Character name in format "Name@World"</param>
    /// <returns>True if relog was initiated successfully</returns>
    public bool Relog(string characterNameWithWorld)
        => IpcInvoker.Invoke(IsAvailable && _relog != null, () => _relog!.InvokeFunc(characterNameWithWorld), false);

    public bool SetCharacterRetainersEnabled(ulong cid, bool enabled)
    {
        if (!IsAvailable || _getOfflineCharacterData == null || _writeOfflineCharacterData == null) return false;
        
        try
        {
            var data = _getOfflineCharacterData.InvokeFunc(cid);
            if (data == null) return false;
            
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
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool SetCharacterDeployablesEnabled(ulong cid, bool enabled)
    {
        if (!IsAvailable || _getOfflineCharacterData == null || _writeOfflineCharacterData == null) return false;
        
        try
        {
            var data = _getOfflineCharacterData.InvokeFunc(cid);
            if (data == null) return false;
            
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
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        StopRetryTimer();
    }

    /// <summary>
    /// Uniform typed field access over AutoRetainer's offline character data, which arrives either
    /// as a Newtonsoft <see cref="JObject"/> or as a plain reflected object. Concrete readers hide
    /// that difference so the parsing in <see cref="GetFullCharacterData"/> exists only once.
    /// </summary>
    private abstract class FieldReader
    {
        public abstract bool TryGetField<T>(string name, out T value);
        public abstract FieldReader? GetChild(string name);
        public abstract IEnumerable<FieldReader> GetArray(string name);

        public string GetString(string name) => TryGetField<string>(name, out var v) && v != null ? v : "";
        public long GetLong(string name) => TryGetField<long>(name, out var v) ? v : 0L;
        public int GetInt(string name) => TryGetField<int>(name, out var v) ? v : 0;
        public uint GetUInt(string name) => TryGetField<uint>(name, out var v) ? v : 0u;
        public bool GetBool(string name) => TryGetField<bool>(name, out var v) && v;
        public ulong GetUlong(string name) => TryGetField<ulong>(name, out var v) ? v : 0UL;

        /// <summary>Returns the first field (by the given names) that parses to a non-zero ulong, else 0.</summary>
        public ulong GetFirstNonZeroUlong(params string[] names)
        {
            foreach (var name in names)
            {
                if (TryGetField<ulong>(name, out var v) && v != 0) return v;
            }
            return 0;
        }
    }

    private sealed class JTokenFieldReader : FieldReader
    {
        private readonly JToken _token;
        public JTokenFieldReader(JToken token) => _token = token;

        public override bool TryGetField<T>(string name, out T value)
        {
            value = default!;
            var token = _token[name];
            if (token == null || token.Type == JTokenType.Null) return false;
            try { value = token.Value<T>()!; return true; }
            catch { return false; }
        }

        public override FieldReader? GetChild(string name)
            => _token[name] is JObject o ? new JTokenFieldReader(o) : null;

        public override IEnumerable<FieldReader> GetArray(string name)
        {
            if (_token[name] is JArray arr)
            {
                foreach (var element in arr)
                {
                    yield return new JTokenFieldReader(element);
                }
            }
        }
    }

    private sealed class ReflectionFieldReader : FieldReader
    {
        private readonly object _obj;
        private readonly Type _type;
        public ReflectionFieldReader(object obj) { _obj = obj; _type = obj.GetType(); }

        public override bool TryGetField<T>(string name, out T value)
        {
            value = default!;
            var raw = _type.GetProperty(name)?.GetValue(_obj);
            if (raw == null) return false;
            try
            {
                if (raw is T typed) { value = typed; return true; }
                value = (T)Convert.ChangeType(raw, typeof(T));
                return true;
            }
            catch { return false; }
        }

        public override FieldReader? GetChild(string name)
        {
            var raw = _type.GetProperty(name)?.GetValue(_obj);
            return raw == null ? null : new ReflectionFieldReader(raw);
        }

        public override IEnumerable<FieldReader> GetArray(string name)
        {
            if (_type.GetProperty(name)?.GetValue(_obj) is System.Collections.IEnumerable list)
            {
                foreach (var element in list)
                {
                    if (element != null) yield return new ReflectionFieldReader(element);
                }
            }
        }
    }
}


