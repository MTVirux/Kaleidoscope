using ECommons.DalamudServices;
using Kaleidoscope.Interfaces;
using Kaleidoscope.Models;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.DataTracker;

/// <summary>
/// Generic helper class for data tracking UI components.
/// Manages in-memory sample data for display and delegates database operations to KaleidoscopeDbService.
/// Implements ICharacterDataSource to allow integration with CharacterPickerWidget.
/// </summary>
public class DataTrackerHelper : ICharacterDataSource
{
    private readonly KaleidoscopeDbService _dbService;
    private readonly TrackedDataRegistry _registry;
    private readonly int _maxSamples;
    private readonly List<float> _samples;

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
        int maxSamples = ConfigStatic.GilTrackerMaxSamples,
        float startingValue = 0f)
    {
        DataType = dataType;
        _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _maxSamples = maxSamples;
        _samples = new List<float>();
        LastValue = startingValue;

        TryLoadSaved();
    }

    #region Sample Management

    /// <summary>
    /// Pushes a new sample value for the specified character (or current player).
    /// Only updates in-memory display if viewing that character.
    /// </summary>
    public void PushSample(float value, ulong? sampleCharacterId = null)
    {
        var cid = sampleCharacterId ?? Svc.PlayerState.ContentId;
        if (cid == 0) return;

        try
        {
            // Check if we should persist (value changed)
            var lastPersisted = _dbService.GetLastValueForCharacter(VariableName, cid);
            if (lastPersisted.HasValue && Math.Abs((double)lastPersisted.Value - value) < ConfigStatic.FloatEpsilon)
            {
                // Duplicate value, skip persistence
                return;
            }

            // Persist the new value
            var inserted = _dbService.SaveSampleIfChanged(VariableName, cid, (long)Math.Round(value));

            if (inserted)
            {
                // Ensure character is in available list
                if (!AvailableCharacters.Contains(cid))
                    AvailableCharacters.Add(cid);

                // Try to save character name
                TrySaveCharacterName(cid);
            }

            // Update in-memory display only when viewing this character
            if (SelectedCharacterId == cid)
            {
                AddToInMemorySamples(value);
            }
            else if (SelectedCharacterId == 0)
            {
                // If viewing aggregate, reload to include this sample
                try { LoadAllCharacters(); }
                catch (Exception ex) { LogService.Debug($"[DataTrackerHelper:{DataType}] LoadAllCharacters in PushSample failed: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] PushSample failed: {ex.Message}");
        }
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

            var sanitized = SanitizeName(name);

            // Try to get local player name if it's just "You"
            if (string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
            {
                var localName = GameStateService.LocalPlayerName;
                if (!string.IsNullOrEmpty(localName))
                    sanitized = localName;
            }

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
    /// Refreshes the list of available characters from the database.
    /// </summary>
    public void RefreshAvailableCharacters()
    {
        try
        {
            var chars = _dbService.GetAvailableCharacters(VariableName);
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
    /// </summary>
    public void LoadForCharacter(ulong characterId)
    {
        try
        {
            var points = _dbService.GetPoints(VariableName, characterId);

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
            var allPoints = _dbService.GetAllPoints(VariableName);

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
        }
        catch (Exception ex)
        {
            LogService.Debug($"[DataTrackerHelper:{DataType}] LoadAllCharacters failed: {ex.Message}");
        }
    }

    #endregion

    #region ICharacterDataSource Implementation

    public void SelectCharacter(ulong characterId)
    {
        SelectedCharacterId = characterId;
        if (characterId == 0)
            LoadAllCharacters();
        else
            LoadForCharacter(characterId);
    }

    public string? GetCharacterName(ulong characterId)
    {
        return _dbService.GetCharacterName(characterId);
    }

    /// <summary>
    /// Gets all stored character names from the database.
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
    }

    /// <summary>
    /// Clears data for a specific character.
    /// </summary>
    public void ClearCharacterData(ulong characterId)
    {
        _dbService.ClearCharacterData(VariableName, characterId);
        AvailableCharacters.Remove(characterId);
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
        return _dbService.CleanUnassociatedCharacters(VariableName);
    }

    /// <summary>
    /// Exports data to a CSV string.
    /// </summary>
    public string? ExportCsv(ulong? characterId = null)
    {
        return _dbService.ExportToCsv(VariableName, characterId);
    }

    /// <summary>
    /// Gets samples filtered by a time cutoff.
    /// </summary>
    public IReadOnlyList<float> GetFilteredSamples(DateTime cutoffTime)
    {
        try
        {
            List<(DateTime ts, long value)> points;

            if (SelectedCharacterId == 0)
            {
                // Get aggregated points with timestamps
                var allPoints = _dbService.GetAllPoints(VariableName);
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
                points = _dbService.GetPoints(VariableName, SelectedCharacterId);
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
            LogService.Debug($"[DataTrackerHelper:{DataType}] GetFilteredSamples failed: {ex.Message}");
            return _samples;
        }
    }

    /// <summary>
    /// Retrieves all stored points for a character (for debug UI).
    /// </summary>
    public List<(DateTime ts, long value)> GetPoints(ulong? characterId = null)
    {
        return _dbService.GetPoints(VariableName, characterId ?? SelectedCharacterId);
    }

    /// <summary>
    /// Gets all character data as separate series (for multi-line display).
    /// </summary>
    public IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> GetAllCharacterSeries(DateTime? cutoffTime = null)
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
            LogService.Debug($"[DataTrackerHelper:{DataType}] GetAllCharacterSeries failed: {ex.Message}");
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

    #region Helpers

    private static string? SanitizeName(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        var s = raw.Trim();

        // Look for patterns like "You (Name)" and extract the inner name
        var idxOpen = s.IndexOf('(');
        var idxClose = s.LastIndexOf(')');
        if (idxOpen >= 0 && idxClose > idxOpen)
        {
            var inner = s.Substring(idxOpen + 1, idxClose - idxOpen - 1).Trim();
            if (!string.IsNullOrEmpty(inner)) return inner;
        }

        return s;
    }

    #endregion
}
