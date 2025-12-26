using Kaleidoscope.Interfaces;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.DataTracker;

/// <summary>
/// Generic helper class for data tracking UI components.
/// Manages in-memory sample data for display and delegates database operations to KaleidoscopeDbService.
/// Implements ICharacterDataSource for consistent character data access patterns.
/// Uses TimeSeriesCacheService for fast reads and background loading to avoid blocking the UI thread.
/// </summary>
public class DataTrackerHelper : ICharacterDataSource
{
    private readonly KaleidoscopeDbService _dbService;
    private readonly TrackedDataRegistry _registry;
    private readonly TimeSeriesCacheService? _cacheService;
    private readonly int _maxSamples;
    private readonly List<float> _samples;

    // Background loading state
    private volatile bool _needsRefresh;
    private readonly object _cacheLock = new();
    
    // Cached multi-character series data
    private IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>? _cachedCharacterSeries;
    private DateTime _lastSeriesCacheTime = DateTime.MinValue;
    private DateTime? _lastSeriesCutoffTime;
    private const int SeriesCacheExpirySeconds = 2; // Cache expires after 2 seconds

    /// <summary>
    /// The data type this helper tracks.
    /// </summary>
    public TrackedDataType DataType { get; }

    /// <summary>
    /// The definition for the tracked data type.
    /// </summary>
    public TrackedDataDefinition? Definition => _registry.GetDefinition(DataType);

    /// <summary>
    /// Variable name used in database queries.
    /// </summary>
    public string VariableName => DataType.ToString();

    public IReadOnlyList<float> Samples => _samples;
    public float LastValue { get; private set; }
    public DateTime? FirstSampleTime { get; private set; }
    public DateTime? LastSampleTime { get; private set; }
    public List<ulong> AvailableCharacters { get; } = new();
    public ulong SelectedCharacterId { get; private set; }
    public string? DbPath => _dbService.DbPath;

    public DataTrackerHelper(
        TrackedDataType dataType,
        KaleidoscopeDbService dbService,
        TrackedDataRegistry registry,
        TimeSeriesCacheService? cacheService = null,
        int maxSamples = ConfigStatic.GilTrackerMaxSamples,
        float startingValue = 0f)
    {
        DataType = dataType;
        _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _cacheService = cacheService;
        _maxSamples = maxSamples;
        _samples = new List<float>();
        LastValue = startingValue;

        TryLoadSaved();
    }

    #region Sample Management

    /// <summary>
    /// Pushes a new sample value for the specified character (or current player).
    /// Only updates in-memory display if viewing that character.
    /// Uses in-memory LastValue check to avoid blocking database reads.
    /// </summary>
    public void PushSample(float value, ulong? sampleCharacterId = null)
    {
        var cid = sampleCharacterId ?? GameStateService.PlayerContentId;
        if (cid == 0) return;

        try
        {
            // Use in-memory LastValue to check for duplicates when viewing this character
            // This avoids a synchronous database read on every sample
            if (SelectedCharacterId == cid && Math.Abs(LastValue - value) < ConfigStatic.FloatEpsilon)
            {
                // Duplicate value, skip persistence
                return;
            }

            // For other characters, let the database's SaveSampleIfChanged handle dedup
            // (it only inserts if value differs from last stored value)
            var inserted = _dbService.SaveSampleIfChanged(VariableName, cid, (long)Math.Round(value));

            if (inserted)
            {
                // Ensure character is in available list
                if (!AvailableCharacters.Contains(cid))
                    AvailableCharacters.Add(cid);

                // Try to save character name
                TrySaveCharacterName(cid);
                
                // Mark cached series as needing refresh
                InvalidateSeriesCache();
            }

            // Update in-memory display only when viewing this character
            if (SelectedCharacterId == cid)
            {
                AddToInMemorySamples(value);
            }
            else if (SelectedCharacterId == 0 && inserted)
            {
                // If viewing aggregate and a new value was inserted, just mark for refresh
                // Don't reload synchronously - let the next Draw cycle handle it
                _needsRefresh = true;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] PushSample failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Invalidates the cached character series data.
    /// </summary>
    private void InvalidateSeriesCache()
    {
        lock (_cacheLock)
        {
            _cachedCharacterSeries = null;
            _lastSeriesCacheTime = DateTime.MinValue;
        }
    }
    
    /// <summary>
    /// Invalidates the cached filtered samples data.
    /// </summary>
    private void InvalidateFilteredCache()
    {
        _cachedFilteredSamples = null;
        _lastFilteredCacheTime = DateTime.MinValue;
    }

    private void AddToInMemorySamples(float value)
    {
        // Avoid storing duplicate consecutive samples in-memory
        if (_samples.Count > 0 && Math.Abs(_samples[^1] - value) < ConfigStatic.FloatEpsilon)
            return;

        _samples.Add(value);
        if (_samples.Count > _maxSamples)
            _samples.RemoveAt(0);
        LastValue = value;
    }

    private void TrySaveCharacterName(ulong characterId)
    {
        try
        {
            var name = Kaleidoscope.Libs.CharacterLib.GetCharacterName(characterId);
            if (string.IsNullOrEmpty(name)) return;

            var sanitized = NameSanitizer.SanitizeWithPlayerFallback(name);

            // Validate and save
            if (!string.IsNullOrEmpty(sanitized) && Kaleidoscope.Libs.CharacterLib.ValidateName(sanitized))
            {
                _dbService.SaveCharacterName(characterId, sanitized);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] Character name save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Samples the current value from the game and pushes it.
    /// </summary>
    public void SampleFromGame()
    {
        try
        {
            var value = _registry.GetCurrentValue(DataType);
            if (value.HasValue)
            {
                var floatValue = (float)value.Value;
                if (Math.Abs(floatValue - LastValue) > ConfigStatic.FloatEpsilon)
                {
                    PushSample(floatValue);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] SampleFromGame error: {ex.Message}");
        }
    }

    #endregion

    #region Data Loading

    private void TryLoadSaved()
    {
        try
        {
            RefreshAvailableCharacters();

            if (SelectedCharacterId == 0)
            {
                LoadAllCharacters();
            }
            else
            {
                LoadForCharacter(SelectedCharacterId);
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] TryLoadSaved failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes the list of available characters from the database or cache.
    /// </summary>
    public void RefreshAvailableCharacters()
    {
        try
        {
            // Try cache first for fast access
            IReadOnlyList<ulong> chars;
            if (_cacheService != null)
            {
                chars = _cacheService.GetAvailableCharacters(VariableName);
            }
            else
            {
                chars = _dbService.GetAvailableCharacters(VariableName);
            }
            
            AvailableCharacters.Clear();
            AvailableCharacters.AddRange(chars);

            if (AvailableCharacters.Count == 0)
            {
                SelectedCharacterId = 0;
                _samples.Clear();
                LastValue = 0f;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] RefreshAvailableCharacters failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads data for a specific character into the in-memory samples.
    /// Uses cache for fast access when available.
    /// </summary>
    public void LoadForCharacter(ulong characterId)
    {
        try
        {
            // Try cache first for fast access
            IReadOnlyList<(DateTime timestamp, long value)> points;
            if (_cacheService != null)
            {
                points = _cacheService.GetCachedPoints(VariableName, characterId);
                // Fall back to DB if cache is empty
                if (points.Count == 0)
                {
                    points = _dbService.GetPoints(VariableName, characterId);
                }
            }
            else
            {
                points = _dbService.GetPoints(VariableName, characterId);
            }

            _samples.Clear();
            var start = Math.Max(0, points.Count - _maxSamples);
            for (var i = start; i < points.Count; i++)
                _samples.Add((float)points[i].value);

            LastValue = _samples.Count > 0 ? _samples[^1] : LastValue;
            SelectedCharacterId = characterId;

            if (points.Count > 0)
            {
                FirstSampleTime = points[0].timestamp;
                LastSampleTime = points[^1].timestamp;
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] LoadForCharacter failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads aggregated data across all characters.
    /// </summary>
    public void LoadAllCharacters()
    {
        try
        {
            // Try cache first for fast access
            IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> allPoints;
            if (_cacheService != null)
            {
                allPoints = _cacheService.GetAllCachedPoints(VariableName);
            }
            else
            {
                allPoints = _dbService.GetAllPoints(VariableName);
            }

            if (allPoints.Count == 0)
            {
                _samples.Clear();
                LastValue = 0f;
                SelectedCharacterId = 0;
                return;
            }

            // Group points by character
            var charPoints = new Dictionary<ulong, List<(DateTime ts, long value)>>();
            foreach (var (charId, ts, value) in allPoints)
            {
                if (!charPoints.TryGetValue(charId, out var list))
                {
                    list = new List<(DateTime, long)>();
                    charPoints[charId] = list;
                }
                list.Add((ts, value));
            }

            // Aggregate by timestamp
            var allTimestamps = new SortedSet<DateTime>();
            foreach (var kv in charPoints)
                foreach (var p in kv.Value)
                    allTimestamps.Add(p.ts);

            var indices = new Dictionary<ulong, int>();
            var currentValues = new Dictionary<ulong, long>();
            foreach (var cid in charPoints.Keys)
            {
                indices[cid] = 0;
                currentValues[cid] = 0L;
            }

            _samples.Clear();
            var start = Math.Max(0, allTimestamps.Count - _maxSamples);
            var i = 0;
            DateTime? firstTs = null;
            DateTime? lastTs = null;

            foreach (var ts in allTimestamps)
            {
                foreach (var kv in charPoints)
                {
                    var cid = kv.Key;
                    var list = kv.Value;
                    var idx = indices[cid];
                    while (idx < list.Count && list[idx].ts <= ts)
                    {
                        currentValues[cid] = list[idx].value;
                        idx++;
                    }
                    indices[cid] = idx;
                }

                long sum = 0L;
                foreach (var v in currentValues.Values) sum += v;

                if (i >= start)
                {
                    _samples.Add((float)sum);
                    firstTs ??= ts;
                    lastTs = ts;
                }
                i++;
            }

            LastValue = _samples.Count > 0 ? _samples[^1] : 0f;
            SelectedCharacterId = 0;
            FirstSampleTime = firstTs;
            LastSampleTime = lastTs;
            
            // Clear the needs refresh flag since we just loaded
            _needsRefresh = false;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] LoadAllCharacters failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Loads aggregated data for a specific subset of characters.
    /// </summary>
    /// <param name="characterIds">The character IDs to load data for. If null or empty, loads all characters.</param>
    public void LoadForCharacters(IReadOnlyList<ulong>? characterIds)
    {
        // Handle null/empty as "all characters"
        if (characterIds == null || characterIds.Count == 0)
        {
            LoadAllCharacters();
            return;
        }
        
        // Single character = simple path
        if (characterIds.Count == 1)
        {
            LoadForCharacter(characterIds[0]);
            return;
        }
        
        try
        {
            // Load data for multiple specific characters
            var charSet = characterIds.ToHashSet();
            
            // Get all points, then filter
            IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> allPoints;
            if (_cacheService != null)
            {
                allPoints = _cacheService.GetAllCachedPoints(VariableName);
            }
            else
            {
                allPoints = _dbService.GetAllPoints(VariableName);
            }
            
            // Filter to only requested characters
            var filteredPoints = allPoints.Where(p => charSet.Contains(p.characterId)).ToList();

            if (filteredPoints.Count == 0)
            {
                _samples.Clear();
                LastValue = 0f;
                SelectedCharacterId = 0;
                return;
            }

            // Group points by character
            var charPoints = new Dictionary<ulong, List<(DateTime ts, long value)>>();
            foreach (var (charId, ts, value) in filteredPoints)
            {
                if (!charPoints.TryGetValue(charId, out var list))
                {
                    list = new List<(DateTime, long)>();
                    charPoints[charId] = list;
                }
                list.Add((ts, value));
            }

            // Aggregate by timestamp
            var allTimestamps = new SortedSet<DateTime>();
            foreach (var kv in charPoints)
                foreach (var p in kv.Value)
                    allTimestamps.Add(p.ts);

            var indices = new Dictionary<ulong, int>();
            var currentValues = new Dictionary<ulong, long>();
            foreach (var cid in charPoints.Keys)
            {
                indices[cid] = 0;
                currentValues[cid] = 0L;
            }

            _samples.Clear();
            var start = Math.Max(0, allTimestamps.Count - _maxSamples);
            var i = 0;
            DateTime? firstTs = null;
            DateTime? lastTs = null;

            foreach (var ts in allTimestamps)
            {
                foreach (var kv in charPoints)
                {
                    var cid = kv.Key;
                    var list = kv.Value;
                    var idx = indices[cid];
                    while (idx < list.Count && list[idx].ts <= ts)
                    {
                        currentValues[cid] = list[idx].value;
                        idx++;
                    }
                    indices[cid] = idx;
                }

                long sum = 0L;
                foreach (var v in currentValues.Values) sum += v;

                if (i >= start)
                {
                    _samples.Add((float)sum);
                    firstTs ??= ts;
                    lastTs = ts;
                }
                i++;
            }

            LastValue = _samples.Count > 0 ? _samples[^1] : 0f;
            SelectedCharacterId = 0; // Multiple characters = "composite" mode
            FirstSampleTime = firstTs;
            LastSampleTime = lastTs;
            
            _needsRefresh = false;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] LoadForCharacters failed: {ex.Message}");
        }
    }

    #endregion

    #region ICharacterDataSource Implementation

    public void SelectCharacter(ulong characterId)
    {
        SelectedCharacterId = characterId;
        InvalidateSeriesCache();
        InvalidateFilteredCache();
        
        if (characterId == 0)
            LoadAllCharacters();
        else
            LoadForCharacter(characterId);
    }
    
    /// <summary>
    /// Selects multiple characters for data loading.
    /// </summary>
    /// <param name="characterIds">The character IDs to select. If null or empty, selects all.</param>
    public void SelectCharacters(IReadOnlyList<ulong>? characterIds)
    {
        InvalidateSeriesCache();
        InvalidateFilteredCache();
        LoadForCharacters(characterIds);
    }

    public string? GetCharacterName(ulong characterId)
    {
        // Try cache first for fast access
        if (_cacheService != null)
        {
            var cachedName = _cacheService.GetCharacterName(characterId);
            if (!string.IsNullOrEmpty(cachedName))
                return cachedName;
        }
        return _dbService.GetCharacterName(characterId);
    }

    /// <summary>
    /// Gets all stored character names from the database or cache.
    /// </summary>
    public List<(ulong cid, string? name)> GetAllStoredCharacterNames()
    {
        return _dbService.GetAllCharacterNames();
    }

    #endregion

    #region Data Management

    /// <summary>
    /// Clears all data for this data type.
    /// </summary>
    public void ClearAllData()
    {
        _dbService.ClearAllData(VariableName);
        _samples.Clear();
        LastValue = 0f;
        FirstSampleTime = null;
        LastSampleTime = null;
        AvailableCharacters.Clear();
        InvalidateSeriesCache();
        InvalidateFilteredCache();
    }

    /// <summary>
    /// Clears data for a specific character.
    /// </summary>
    public void ClearCharacterData(ulong characterId)
    {
        _dbService.ClearCharacterData(VariableName, characterId);
        AvailableCharacters.Remove(characterId);
        InvalidateSeriesCache();
        InvalidateFilteredCache();
        
        if (SelectedCharacterId == characterId)
        {
            SelectedCharacterId = 0;
            LoadAllCharacters();
        }
    }

    /// <summary>
    /// Removes data for characters without a name association.
    /// </summary>
    public int CleanUnassociatedCharacterData()
    {
        var count = _dbService.CleanUnassociatedCharacters(VariableName);
        if (count > 0)
        {
            InvalidateSeriesCache();
            InvalidateFilteredCache();
        }
        return count;
    }

    /// <summary>
    /// Exports data to a CSV string.
    /// </summary>
    public string? ExportCsv(ulong? characterId = null)
    {
        return _dbService.ExportToCsv(VariableName, characterId);
    }

    // Cached filtered samples data
    private IReadOnlyList<float>? _cachedFilteredSamples;
    private DateTime _lastFilteredCacheTime = DateTime.MinValue;
    private DateTime _lastFilteredCutoffTime = DateTime.MinValue;
    private ulong _lastFilteredCharacterId = 0;
    private const int FilteredCacheExpirySeconds = 2;

    /// <summary>
    /// Gets samples filtered by a time cutoff.
    /// Uses TimeSeriesCacheService when available, with local caching as fallback.
    /// </summary>
    public IReadOnlyList<float> GetFilteredSamples(DateTime cutoffTime)
    {
        // When using cache service, query it directly (it's fast in-memory access)
        if (_cacheService != null)
        {
            return LoadFilteredSamplesFromDb(cutoffTime);
        }
        
        // Fallback: Check if we can use local cached data (only when no cache service)
        var now = DateTime.UtcNow;
        var cacheValid = _cachedFilteredSamples != null 
            && (now - _lastFilteredCacheTime).TotalSeconds < FilteredCacheExpirySeconds
            && _lastFilteredCutoffTime == cutoffTime
            && _lastFilteredCharacterId == SelectedCharacterId
            && !_needsRefresh;
        
        if (cacheValid)
        {
            return _cachedFilteredSamples!;
        }
        
        // Load fresh data
        var result = LoadFilteredSamplesFromDb(cutoffTime);
        
        // Update cache
        _cachedFilteredSamples = result;
        _lastFilteredCacheTime = now;
        _lastFilteredCutoffTime = cutoffTime;
        _lastFilteredCharacterId = SelectedCharacterId;
        
        return result;
    }
    
    /// <summary>
    /// Loads filtered samples from the database.
    /// </summary>
    private IReadOnlyList<float> LoadFilteredSamplesFromDb(DateTime cutoffTime)
    {
        try
        {
            List<(DateTime ts, long value)> points;

            if (SelectedCharacterId == 0)
            {
                // Get aggregated points with timestamps
                // Try cache first for fast access
                IReadOnlyList<(ulong characterId, DateTime timestamp, long value)> allPoints;
                if (_cacheService != null)
                {
                    allPoints = _cacheService.GetAllCachedPoints(VariableName);
                }
                else
                {
                    allPoints = _dbService.GetAllPoints(VariableName);
                }
                
                if (allPoints.Count == 0) return Array.Empty<float>();

                // Group by character and build aggregate
                var charPoints = new Dictionary<ulong, List<(DateTime ts, long value)>>();
                foreach (var (charId, ts, value) in allPoints)
                {
                    if (!charPoints.TryGetValue(charId, out var list))
                    {
                        list = new List<(DateTime, long)>();
                        charPoints[charId] = list;
                    }
                    list.Add((ts, value));
                }

                var allTimestamps = new SortedSet<DateTime>();
                foreach (var kv in charPoints)
                    foreach (var p in kv.Value)
                        allTimestamps.Add(p.ts);

                var indices = new Dictionary<ulong, int>();
                var currentValues = new Dictionary<ulong, long>();
                foreach (var cid in charPoints.Keys)
                {
                    indices[cid] = 0;
                    currentValues[cid] = 0L;
                }

                points = new List<(DateTime ts, long value)>();
                foreach (var ts in allTimestamps)
                {
                    foreach (var kv in charPoints)
                    {
                        var cid = kv.Key;
                        var list = kv.Value;
                        var idx = indices[cid];
                        while (idx < list.Count && list[idx].ts <= ts)
                        {
                            currentValues[cid] = list[idx].value;
                            idx++;
                        }
                        indices[cid] = idx;
                    }

                    long sum = 0L;
                    foreach (var v in currentValues.Values) sum += v;
                    points.Add((ts, sum));
                }
            }
            else
            {
                // Try cache first for single character
                if (_cacheService != null)
                {
                    var cachedPoints = _cacheService.GetCachedPoints(VariableName, SelectedCharacterId, cutoffTime);
                    if (cachedPoints.Count > 0)
                    {
                        points = cachedPoints.Select(p => (p.timestamp, p.value)).ToList();
                    }
                    else
                    {
                        // Fall back to DB if cache is empty
                        points = _dbService.GetPoints(VariableName, SelectedCharacterId);
                    }
                }
                else
                {
                    points = _dbService.GetPoints(VariableName, SelectedCharacterId);
                }
            }

            // Filter by cutoff time
            var filtered = points.Where(p => p.ts >= cutoffTime).ToList();
            if (filtered.Count == 0) return Array.Empty<float>();

            var result = new List<float>();
            var start = Math.Max(0, filtered.Count - _maxSamples);
            for (var i = start; i < filtered.Count; i++)
                result.Add((float)filtered[i].value);

            return result;
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] LoadFilteredSamplesFromDb failed: {ex.Message}");
            return _samples;
        }
    }

    /// <summary>
    /// Retrieves all stored points for a character (for debug UI).
    /// Uses cache when available for fast access.
    /// </summary>
    public List<(DateTime ts, long value)> GetPoints(ulong? characterId = null)
    {
        var cid = characterId ?? SelectedCharacterId;
        
        // Try cache first for fast access
        if (_cacheService != null)
        {
            var cachedPoints = _cacheService.GetCachedPoints(VariableName, cid);
            if (cachedPoints.Count > 0)
            {
                return cachedPoints.Select(p => (p.timestamp, p.value)).ToList();
            }
        }
        
        return _dbService.GetPoints(VariableName, cid);
    }

    /// <summary>
    /// Gets all character data as separate series (for multi-line display).
    /// Uses TimeSeriesCacheService when available, with local caching as fallback.
    /// </summary>
    public IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> GetAllCharacterSeries(DateTime? cutoffTime = null)
    {
        // If using cache service, get data directly from it (it handles caching internally)
        if (_cacheService != null)
        {
            var cachedSeries = _cacheService.GetAllCachedCharacterSeries(VariableName, cutoffTime);
            var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();
            
            foreach (var (charId, name, points) in cachedSeries)
            {
                if (points.Count == 0) continue;
                
                var samples = new List<(DateTime ts, float value)>();
                var start = Math.Max(0, points.Count - _maxSamples);
                for (var i = start; i < points.Count; i++)
                    samples.Add((points[i].ts, (float)points[i].value));
                
                result.Add((name, samples));
            }
            
            return result;
        }
        
        // Fallback: Check if we can use local cached data
        lock (_cacheLock)
        {
            var now = DateTime.UtcNow;
            var cacheValid = _cachedCharacterSeries != null 
                && (now - _lastSeriesCacheTime).TotalSeconds < SeriesCacheExpirySeconds
                && _lastSeriesCutoffTime == cutoffTime
                && !_needsRefresh;
            
            if (cacheValid)
            {
                return _cachedCharacterSeries!;
            }
        }

        // Load fresh data from DB
        var dbResult = LoadCharacterSeriesFromDb(cutoffTime);
        
        // Update local cache
        lock (_cacheLock)
        {
            _cachedCharacterSeries = dbResult;
            _lastSeriesCacheTime = DateTime.UtcNow;
            _lastSeriesCutoffTime = cutoffTime;
            _needsRefresh = false;
        }
        
        return dbResult;
    }
    
    /// <summary>
    /// Loads character series data from the database.
    /// </summary>
    private List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> LoadCharacterSeriesFromDb(DateTime? cutoffTime)
    {
        var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();

        try
        {
            var allPoints = _dbService.GetAllPoints(VariableName);
            if (allPoints.Count == 0) return result;

            // Group points by character
            var charPoints = new Dictionary<ulong, List<(DateTime ts, long value)>>();
            foreach (var (charId, ts, value) in allPoints)
            {
                // Apply time filter if specified
                if (cutoffTime.HasValue && ts < cutoffTime.Value)
                    continue;

                if (!charPoints.TryGetValue(charId, out var list))
                {
                    list = new List<(DateTime, long)>();
                    charPoints[charId] = list;
                }
                list.Add((ts, value));
            }

            // Convert each character's data to a series with timestamps
            foreach (var kv in charPoints)
            {
                var charId = kv.Key;
                var points = kv.Value;
                if (points.Count == 0) continue;

                var name = GetCharacterDisplayName(charId);
                var samples = new List<(DateTime ts, float value)>();

                var start = Math.Max(0, points.Count - _maxSamples);
                for (var i = start; i < points.Count; i++)
                    samples.Add((points[i].ts, (float)points[i].value));

                result.Add((name, samples));
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] LoadCharacterSeriesFromDb failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Gets a display name for a character, falling back to a shortened ID if no name is stored.
    /// </summary>
    public string GetCharacterDisplayName(ulong characterId)
    {
        var name = GetCharacterName(characterId);
        if (!string.IsNullOrEmpty(name))
            return name;

        // Fallback to last 6 digits of character ID
        return $"...{characterId % 1_000_000:D6}";
    }

    #endregion
}
