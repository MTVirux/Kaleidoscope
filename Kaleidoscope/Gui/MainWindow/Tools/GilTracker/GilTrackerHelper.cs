using Kaleidoscope.Interfaces;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow.Tools.GilTracker;

/// <summary>
/// Helper class for the GilTracker UI component.
/// Manages in-memory sample data for display and delegates database operations to KaleidoscopeDbService.
/// Implements ICharacterDataSource to allow integration with CharacterPickerWidget.
/// </summary>
public class GilTrackerHelper : ICharacterDataSource
{
        private readonly KaleidoscopeDbService _dbService;
        private readonly int _maxSamples;
        private readonly List<float> _samples;

        public IReadOnlyList<float> Samples => _samples;
        public float LastValue { get; private set; }
        public DateTime? FirstSampleTime { get; private set; }
        public DateTime? LastSampleTime { get; private set; }
        public List<ulong> AvailableCharacters { get; } = new();
        public ulong SelectedCharacterId { get; private set; }
        public string? DbPath => _dbService.DbPath;

        public GilTrackerHelper(string? dbPath, int maxSamples = ConfigStatic.GilTrackerMaxSamples, float startingValue = ConfigStatic.GilTrackerStartingValue)
        {
            _maxSamples = maxSamples;
            _samples = new List<float>();
            LastValue = startingValue;

            // Create or reuse database service
            _dbService = new KaleidoscopeDbService(dbPath);

            // Load saved data on initialization
            TryLoadSaved();
        }

        /// <summary>
        /// Constructor that accepts an existing database service (for sharing with SamplerService).
        /// </summary>
        public GilTrackerHelper(KaleidoscopeDbService dbService, int maxSamples = ConfigStatic.GilTrackerMaxSamples, float startingValue = ConfigStatic.GilTrackerStartingValue)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
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
            var cid = sampleCharacterId ?? GameStateService.PlayerContentId;
            if (cid == 0) return;

            try
            {
                // Check if we should persist (value changed)
                var lastPersisted = _dbService.GetLastValueForCharacter("Gil", cid);
                if (lastPersisted.HasValue && Math.Abs((double)lastPersisted.Value - value) < ConfigStatic.FloatEpsilon)
                {
                    // Duplicate value, skip persistence
                    return;
                }

                // Persist the new value
                var inserted = _dbService.SaveSampleIfChanged("Gil", cid, (long)Math.Round(value));

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
                    catch (Exception ex) { LogService.Debug($"[GilTrackerHelper] LoadAllCharacters in PushSample failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[GilTrackerHelper] PushSample failed: {ex.Message}");
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
                LogService.Debug($"[GilTrackerHelper] Character name save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Samples the current gil value from the game and pushes it.
        /// </summary>
        public void SampleFromGame()
        {
            try
            {
                unsafe
                {
                    var im = GameStateService.InventoryManagerInstance();
                    if (im != null)
                    {
                        float gil = 0;
                        // Use GetInventoryItemCount(1) as primary (matches AutoRetainer approach)
                        try { gil = im->GetInventoryItemCount(1); }
                        catch { gil = 0; }
                        
                        // Fallback to GetGil() if primary returns 0
                        if (gil == 0)
                        {
                            try { gil = im->GetGil(); }
                            catch { gil = 0; }
                        }
                        
                        if (Math.Abs(gil - LastValue) > ConfigStatic.FloatEpsilon)
                        {
                            PushSample(gil);
                        }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[GilTrackerHelper] SampleFromGame error: {ex.Message}");
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
                LogService.Debug($"[GilTrackerHelper] TryLoadSaved failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Refreshes the list of available characters from the database.
        /// </summary>
        public void RefreshAvailableCharacters()
        {
            try
            {
                var chars = _dbService.GetAvailableCharacters("Gil");
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
                LogService.Debug($"[GilTrackerHelper] RefreshAvailableCharacters failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads data for a specific character into the in-memory samples.
        /// </summary>
        public void LoadForCharacter(ulong characterId)
        {
            try
            {
                var points = _dbService.GetPoints("Gil", characterId);

                _samples.Clear();
                var start = Math.Max(0, points.Count - _maxSamples);
                for (var i = start; i < points.Count; i++)
                    _samples.Add((float)points[i].value);

                LastValue = _samples.Count > 0 ? _samples[^1] : LastValue;
                SelectedCharacterId = characterId;

                if (points.Count > 0)
                {
                    FirstSampleTime = points[start].timestamp;
                    LastSampleTime = points[points.Count - 1].timestamp;
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[GilTrackerHelper] LoadForCharacter failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads aggregated data across all characters.
        /// </summary>
        public void LoadAllCharacters()
        {
            try
            {
                var allPoints = _dbService.GetAllPoints("Gil");

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

                // Collect all unique timestamps
                var allTimestamps = new SortedSet<DateTime>();
                foreach (var kv in charPoints)
                    foreach (var p in kv.Value)
                        allTimestamps.Add(p.ts);

                // Build aggregate timeline
                var indices = new Dictionary<ulong, int>();
                var currentValues = new Dictionary<ulong, long>();
                foreach (var cid in charPoints.Keys)
                {
                    indices[cid] = 0;
                    currentValues[cid] = 0L;
                }

                var combined = new List<(DateTime ts, long sum)>();
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
                    combined.Add((ts, sum));
                }

                // Trim to max samples
                _samples.Clear();
                var start = Math.Max(0, combined.Count - _maxSamples);
                for (var i = start; i < combined.Count; i++)
                    _samples.Add((float)combined[i].sum);

                LastValue = _samples.Count > 0 ? _samples[^1] : LastValue;
                if (combined.Count > 0)
                {
                    FirstSampleTime = combined[start].ts;
                    LastSampleTime = combined[combined.Count - 1].ts;
                }
                SelectedCharacterId = 0;
            }
            catch (Exception ex)
            {
                LogService.Debug($"[GilTrackerHelper] LoadAllCharacters failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all character data series for multi-line display.
        /// </summary>
        /// <param name="cutoffTime">Optional cutoff time to filter data.</param>
        /// <returns>List of character names and their sample data with timestamps.</returns>
        public IReadOnlyList<(string name, IReadOnlyList<(DateTime ts, float value)> samples)> GetAllCharacterSeries(DateTime? cutoffTime = null)
        {
            var result = new List<(string name, IReadOnlyList<(DateTime ts, float value)> samples)>();

            try
            {
                var allPoints = _dbService.GetAllPoints("Gil");
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
                LogService.Debug($"[GilTrackerHelper] GetAllCharacterSeries failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Gets samples filtered by a time cutoff.
        /// </summary>
        /// <param name="cutoffTime">Only include samples after this time.</param>
        /// <returns>Filtered sample list.</returns>
        public IReadOnlyList<float> GetFilteredSamples(DateTime cutoffTime)
        {
            try
            {
                List<(DateTime ts, long value)> points;

                if (SelectedCharacterId == 0)
                {
                    // Get aggregated points with timestamps
                    var allPoints = _dbService.GetAllPoints("Gil");
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
                    points = _dbService.GetPoints("Gil", SelectedCharacterId);
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
                LogService.Debug($"[GilTrackerHelper] GetFilteredSamples failed: {ex.Message}");
                return _samples;
            }
        }

        #endregion

        #region Character Display

        /// <summary>
        /// Gets a display name for the provided character ID.
        /// </summary>
        public string GetCharacterDisplayName(ulong characterId)
        {
            // Try database first
            var storedName = _dbService.GetCharacterName(characterId);
            if (!string.IsNullOrEmpty(storedName))
            {
                var sanitized = SanitizeName(storedName);
                return !string.IsNullOrEmpty(sanitized) ? sanitized : storedName;
            }

            // Try runtime lookup
            try
            {
                var runtime = Kaleidoscope.Libs.CharacterLib.GetCharacterName(characterId);
                if (!string.IsNullOrEmpty(runtime))
                {
                    var sanitized = SanitizeName(runtime);

                    if (string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
                    {
                        var localName = GameStateService.LocalPlayerName;
                        if (!string.IsNullOrEmpty(localName))
                            return localName;
                    }

                    return !string.IsNullOrEmpty(sanitized) ? sanitized : runtime;
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[GilTrackerHelper] Runtime name lookup failed: {ex.Message}");
            }

            return characterId.ToString();
        }

        /// <summary>
        /// Returns all stored character names from the database.
        /// </summary>
        public List<(ulong cid, string? name)> GetAllStoredCharacterNames()
        {
            return _dbService.GetAllCharacterNames();
        }

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

            // If it starts with "You " then strip that prefix
            if (s.StartsWith("You ", StringComparison.OrdinalIgnoreCase))
            {
                var rem = s.Substring(4).Trim();
                if (!string.IsNullOrEmpty(rem)) return rem;
            }

            return s;
        }

        #endregion

        #region Data Management

        /// <summary>
        /// Clears data for the currently selected character.
        /// </summary>
        public void ClearForSelectedCharacter()
        {
            try
            {
                var cid = SelectedCharacterId == 0 ? GameStateService.PlayerContentId : SelectedCharacterId;
                if (cid == 0) return;

                _dbService.ClearCharacterData("Gil", cid);
                _samples.Clear();
                RefreshAvailableCharacters();
            }
            catch (Exception ex)
            {
                LogService.Error($"[GilTrackerHelper] ClearForSelectedCharacter failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Clears all stored data for the 'Gil' variable.
        /// </summary>
        public void ClearAllData()
        {
            try
            {
                _dbService.ClearAllData("Gil");
                _samples.Clear();
                AvailableCharacters.Clear();
                SelectedCharacterId = 0;
            }
            catch (Exception ex)
            {
                LogService.Error($"[GilTrackerHelper] ClearAllData failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Removes data for characters without a name association.
        /// Returns the number of characters removed.
        /// </summary>
        public int CleanUnassociatedCharacterData()
        {
            try
            {
                var removed = _dbService.CleanUnassociatedCharacters("Gil");
                if (removed > 0)
                    RefreshAvailableCharacters();
                return removed;
            }
            catch (Exception ex)
            {
                LogService.Error($"[GilTrackerHelper] CleanUnassociatedCharacterData failed: {ex.Message}", ex);
                return 0;
            }
        }

        /// <summary>
        /// Exports data to a CSV file.
        /// </summary>
        public string? ExportCsv(ulong? characterId = null)
        {
            try
            {
                var cid = characterId ?? SelectedCharacterId;

                // Resolve to local player if needed
                if (cid == 0 && SelectedCharacterId == 0)
                    cid = GameStateService.PlayerContentId;

                var csvContent = _dbService.ExportToCsv("Gil", cid == 0 ? null : cid);
                if (string.IsNullOrEmpty(csvContent)) return null;

                var saveDir = FilenameService.Instance?.ConfigDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                string fileName;
                if (cid == 0)
                {
                    fileName = Path.Combine(saveDir, $"giltracker-gil-all-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv");
                }
                else
                {
                    var name = Kaleidoscope.Libs.CharacterLib.GetCharacterName(cid);
                    var safeName = string.IsNullOrEmpty(name)
                        ? cid.ToString()
                        : string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')).Replace(' ', '_');
                    fileName = Path.Combine(saveDir, $"giltracker-gil-{safeName}-{cid}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv");
                }

                File.WriteAllText(fileName, csvContent);
                return fileName;
            }
            catch (Exception ex)
            {
                LogService.Error($"[GilTrackerHelper] ExportCsv failed: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Retrieves all stored points for a character (for debug UI).
        /// </summary>
        public List<(DateTime ts, long value)> GetPoints(ulong? characterId = null)
        {
            var cid = characterId ?? SelectedCharacterId;

        if (cid == 0 && SelectedCharacterId == 0)
            cid = GameStateService.PlayerContentId;

        if (cid == 0)
        {
            // Return all points as simple list (loses character association)
            var allPoints = _dbService.GetAllPoints("Gil");
            return allPoints.Select(p => (p.timestamp, p.value)).ToList();
        }

        return _dbService.GetPoints("Gil", cid);
    }

    #endregion
}
