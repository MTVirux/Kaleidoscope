using ECommons.DalamudServices;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json.Linq;

namespace Kaleidoscope.Services;

/// <summary>
/// IPC service for communicating with AutoRetainer plugin.
/// Provides access to character data tracked by AutoRetainer.
/// </summary>
public class AutoRetainerIpcService
{
    private ICallGateSubscriber<List<ulong>>? _getRegisteredCIDs;
    private ICallGateSubscriber<ulong, object?>? _getOfflineCharacterData;
    
    private bool _initialized = false;

    public bool IsAvailable { get; private set; } = false;

    public void Initialize()
    {
        if (_initialized) return;
        
        try
        {
            _getRegisteredCIDs = Svc.PluginInterface.GetIpcSubscriber<List<ulong>>("AutoRetainer.GetRegisteredCIDs");
            _getOfflineCharacterData = Svc.PluginInterface.GetIpcSubscriber<ulong, object?>("AutoRetainer.GetOfflineCharacterData");
            
            // Test if AutoRetainer is available by trying to call the IPC
            try
            {
                var cids = _getRegisteredCIDs.InvokeFunc();
                IsAvailable = true;
                LogService.Info($"AutoRetainer IPC connected successfully, found {cids?.Count ?? 0} registered CIDs");
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                LogService.Debug($"AutoRetainer not available: {ex.Message}");
            }
            
            _initialized = true;
        }
        catch (Exception ex)
        {
            LogService.Debug($"Failed to initialize AutoRetainer IPC: {ex.Message}");
            IsAvailable = false;
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
            LogService.Debug($"AutoRetainer returned {cids?.Count ?? 0} CIDs");
            return cids;
        }
        catch (Exception ex)
        {
            LogService.Debug($"Failed to get registered CIDs from AutoRetainer: {ex.Message}");
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
        if (!IsAvailable || _getOfflineCharacterData == null) return null;
        
        try
        {
            var data = _getOfflineCharacterData.InvokeFunc(cid);
            if (data == null)
            {
                LogService.Debug($"AutoRetainer returned null data for CID {cid}");
                return null;
            }
            
            string name = "";
            string world = "";
            long gil = 0;
            
            // Check if the data is a JObject (JSON)
            if (data is JObject jObject)
            {
                name = jObject["Name"]?.Value<string>() ?? "";
                world = jObject["World"]?.Value<string>() ?? "";
                gil = jObject["Gil"]?.Value<long>() ?? 0L;
            }
            else
            {
                // Fallback to reflection for regular objects
                var type = data.GetType();
                LogService.Debug($"AutoRetainer data type: {type.FullName}");
                
                name = type.GetProperty("Name")?.GetValue(data) as string ?? "";
                world = type.GetProperty("World")?.GetValue(data) as string ?? "";
                var gilProp = type.GetProperty("Gil")?.GetValue(data);
                gil = gilProp != null ? Convert.ToInt64(gilProp) : 0L;
            }
            
            LogService.Debug($"AutoRetainer character: {name}@{world}, Gil: {gil}");
            
            return (name, world, gil, cid);
        }
        catch (Exception ex)
        {
            LogService.Debug($"Failed to get character data from AutoRetainer for CID {cid}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets all character data from AutoRetainer.
    /// </summary>
    public List<(string Name, string World, long Gil, ulong CID)> GetAllCharacterData()
    {
        var result = new List<(string Name, string World, long Gil, ulong CID)>();
        
        var cids = GetRegisteredCharacterIds();
        if (cids == null || cids.Count == 0)
        {
            LogService.Debug("AutoRetainer returned no CIDs");
            return result;
        }
        
        LogService.Debug($"Processing {cids.Count} CIDs from AutoRetainer");
        
        foreach (var cid in cids)
        {
            var charData = GetCharacterData(cid);
            if (charData.HasValue && !string.IsNullOrEmpty(charData.Value.Name))
            {
                result.Add(charData.Value);
            }
        }
        
        LogService.Debug($"Returning {result.Count} characters from AutoRetainer");
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
}
