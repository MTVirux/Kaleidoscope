using Microsoft.Data.Sqlite;
using ECommons.DalamudServices;
using System.Text;
using System.Linq;
using Kaleidoscope.Services;

namespace Kaleidoscope.Gui.MainWindow
{
    internal class GilTrackerHelper
    {
        // Track the last seen local content id so we only auto-switch when
        // the active character actually changes.
        private ulong _lastSeenLocalCid = 0;

        private readonly object _fileLock = new();
        private SqliteConnection? _connection;
        private readonly string? _dbPath;
        private readonly int _maxSamples;
        private readonly List<float> _samples;
        public IReadOnlyList<float> Samples => _samples;

        public float LastValue { get; private set; }
        public DateTime? FirstSampleTime { get; private set; }
        public DateTime? LastSampleTime { get; private set; }
        public List<ulong> AvailableCharacters { get; } = new();
        public ulong SelectedCharacterId { get; private set; }
        public string LastStatusMessage { get; private set; } = string.Empty;

        private void SetStatus(string message)
        {
            // Status messages removed per request: do not record or display messages.
            // Intentionally left blank to suppress any runtime status output.
        }

        public GilTrackerHelper(string? dbPath, int maxSamples = 200, float startingValue = 100000f)
        {
            _dbPath = dbPath;
            _maxSamples = maxSamples;
            _samples = new List<float>();
            LastValue = startingValue;
            EnsureConnection();
            TryLoadSaved();
        }

        private void EnsureConnection()
        {
            if (string.IsNullOrEmpty(_dbPath)) return;
            try
            {
                if (_connection == null)
                {
                    var dir = Path.GetDirectoryName(_dbPath)!;
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = SqliteOpenMode.ReadWriteCreate };
                    _connection = new SqliteConnection(csb.ToString());
                    _connection.Open();
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS series (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    variable TEXT NOT NULL,
    character_id INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS points (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    series_id INTEGER NOT NULL,
    timestamp INTEGER NOT NULL,
    value INTEGER NOT NULL,
    FOREIGN KEY(series_id) REFERENCES series(id)
);

CREATE TABLE IF NOT EXISTS character_names (
    character_id INTEGER PRIMARY KEY,
    name TEXT
);

CREATE INDEX IF NOT EXISTS idx_series_variable_character ON series(variable, character_id);
CREATE INDEX IF NOT EXISTS idx_points_series_timestamp ON points(series_id, timestamp);
";
                    cmd.ExecuteNonQuery();
                    // Perform a one-time cleanup of stored names to remove wrappers like "You (Name)".
                    try
                    {
                        MigrateStoredNames();
                    }
                    catch (Exception ex) { LogService.Debug($"[GilTracker] Name migration failed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { LogService.Error($"[GilTracker] Database initialization failed: {ex.Message}", ex); }
        }

        private void MigrateStoredNames()
        {
            if (_connection == null) return;
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT character_id, name FROM character_names";
                using var rdr = cmd.ExecuteReader();
                var updates = new List<(long cid, string newName)>();
                var deletes = new List<long>();
                while (rdr.Read())
                {
                    var cid = rdr.GetFieldValue<long>(0);
                    string? name = null;
                    if (!rdr.IsDBNull(1)) name = rdr.GetFieldValue<string>(1);
                    var sanitized = SanitizeName(name);
                    // If the stored name contains any digit, treat it as invalid and schedule removal
                    if (!string.IsNullOrEmpty(sanitized) && sanitized.Any(char.IsDigit))
                    {
                        deletes.Add(cid);
                        continue;
                    }
                    // If the stored name sanitizes to just "You", treat it as a placeholder and schedule removal
                    if (!string.IsNullOrEmpty(sanitized) && string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
                    {
                        deletes.Add(cid);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(sanitized) && !string.Equals(sanitized, name, StringComparison.Ordinal))
                    {
                        updates.Add((cid, sanitized));
                    }
                }
                rdr.Close();

                foreach (var u in updates)
                {
                    try
                    {
                        using var up = _connection.CreateCommand();
                        up.CommandText = "UPDATE character_names SET name = $n WHERE character_id = $c";
                        up.Parameters.AddWithValue("$n", u.newName);
                        up.Parameters.AddWithValue("$c", u.cid);
                        up.ExecuteNonQuery();
                    }
                    catch (Exception ex) { LogService.Debug($"[GilTracker] Failed to update name for CID {u.cid}: {ex.Message}"); }
                }

                // Remove placeholder names that resolved to "You" (case-insensitive).
                foreach (var cidToRemove in deletes)
                {
                    try
                    {
                        using var del = _connection.CreateCommand();
                        del.CommandText = "DELETE FROM character_names WHERE character_id = $c";
                        del.Parameters.AddWithValue("$c", cidToRemove);
                        del.ExecuteNonQuery();
                    }
                    catch (Exception ex) { LogService.Debug($"[GilTracker] Failed to delete placeholder name for CID {cidToRemove}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] Name migration query failed: {ex.Message}"); }
        }

        public void PushSample(float v, ulong? sampleCharacterId = null)
        {
            // Persist sample for the runtime/local character (or provided id),
            // but only update the in-memory samples/LastValue when the currently
            // selected character matches the sampled character (or when viewing
            // the aggregated "All" view).
            var cid = sampleCharacterId ?? Svc.PlayerState.ContentId;
            if (cid == 0) return;

            try
            {
                // Check the last persisted value for this character to avoid duplicate writes.
                long? lastPersisted = null;
                try
                {
                    lock (_fileLock)
                    {
                        EnsureConnection();
                        if (_connection != null)
                        {
                            using var lastCmd = _connection.CreateCommand();
                            lastCmd.CommandText = "SELECT p.value FROM points p JOIN series s ON p.series_id=s.id WHERE s.variable=$v AND s.character_id=$c ORDER BY p.timestamp DESC LIMIT 1";
                            lastCmd.Parameters.AddWithValue("$v", "Gil");
                            lastCmd.Parameters.AddWithValue("$c", (long)cid);
                            var r = lastCmd.ExecuteScalar();
                            if (r != null && r != DBNull.Value) lastPersisted = (long)r;
                        }
                    }
                }
                catch (Exception ex) { LogService.Debug($"[GilTracker] Last persisted value lookup failed: {ex.Message}"); lastPersisted = null; }

                if (lastPersisted.HasValue && Math.Abs((double)lastPersisted.Value - v) < 0.0001) 
                {
                    // Nothing new to persist for this character
                    SetStatus("Duplicate sample; skipped.");
                }
                else
                {
                    // Persist the sample (this will also update DB-internal state and available characters)
                    TrySaveFor(cid, (long)Math.Round(v));
                }

                // Update in-memory display only when selection matches the sampled character
                if (SelectedCharacterId == cid)
                {
                    // Avoid storing duplicate consecutive samples in-memory
                    if (!(_samples.Count > 0 && Math.Abs(_samples[^1] - v) < 0.0001f))
                    {
                        _samples.Add(v);
                        if (_samples.Count > _maxSamples) _samples.RemoveAt(0);
                        LastValue = v;
                    }
                }
                else if (SelectedCharacterId == 0)
                {
                    // If viewing aggregate, reload combined series to include this sample
                    try { LoadAllCharacters(); } catch (Exception ex) { LogService.Debug($"[GilTracker] LoadAllCharacters in PushSample failed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] PushSample failed: {ex.Message}"); }
        }

        private void TrySaveFor(ulong cid, long value)
        {
            if (string.IsNullOrEmpty(_dbPath)) return;
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    // Ignore saving when we don't have a valid character/content id (0)
                    if (cid == 0) return;
                    if (_connection == null) return;
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT id FROM series WHERE variable = $v AND character_id = $c LIMIT 1";
                    cmd.Parameters.AddWithValue("$v", "Gil");
                    cmd.Parameters.AddWithValue("$c", (long)cid);
                    var r = cmd.ExecuteScalar();
                    long seriesId;
                    var isNewSeries = false;
                    if (r != null && r != DBNull.Value) seriesId = (long)r;
                    else
                    {
                        cmd.CommandText = "INSERT INTO series(variable, character_id) VALUES($v, $c); SELECT last_insert_rowid();";
                        seriesId = (long)cmd.ExecuteScalar();
                        isNewSeries = true;
                    }

                    // Persist the character name (if available) into a simple mapping table so
                    // exported files and future UI lookups can use the stored name even when
                    // runtime name resolution is unavailable.
                    try
                    {
                        var name = Kaleidoscope.Libs.CharacterLib.GetCharacterName(cid);
                        if (!string.IsNullOrEmpty(name))
                        {
                            try
                            {
                                var sanitized = name;
                                var idxOpen = sanitized.IndexOf('(');
                                var idxClose = sanitized.LastIndexOf(')');
                                if (idxOpen >= 0 && idxClose > idxOpen)
                                {
                                    sanitized = sanitized.Substring(idxOpen + 1, idxClose - idxOpen - 1).Trim();
                                }
                                if (string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
                                {
                                    try { var lp = Svc.Objects.LocalPlayer?.Name.ToString(); if (!string.IsNullOrEmpty(lp)) sanitized = lp; } catch (Exception ex) { LogService.Debug($"[GilTracker] LocalPlayer name lookup failed: {ex.Message}"); }
                                }
                                if (!string.IsNullOrEmpty(sanitized))
                                {
                                    // Validate the sanitized name before persisting. Use the
                                    // centralized CharacterLib.ValidateName so validation rules
                                    // (no digits, exactly one space) are consistent.
                                    try
                                    {
                                        if (Kaleidoscope.Libs.CharacterLib.ValidateName(sanitized))
                                        {
                                            using var nameCmd = _connection.CreateCommand();
                                            nameCmd.CommandText = "INSERT OR REPLACE INTO character_names(character_id, name) VALUES($c, $n)";
                                            nameCmd.Parameters.AddWithValue("$c", (long)cid);
                                            nameCmd.Parameters.AddWithValue("$n", sanitized);
                                            nameCmd.ExecuteNonQuery();
                                        }
                                    }
                                    catch (Exception ex) { LogService.Debug($"[GilTracker] Character name insert failed: {ex.Message}"); }
                                }
                            }
                            catch (Exception ex) { LogService.Debug($"[GilTracker] Character name sanitization failed: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { LogService.Debug($"[GilTracker] Character name resolution failed: {ex.Message}"); }

                    cmd.CommandText = "INSERT INTO points(series_id, timestamp, value) VALUES($s, $t, $v)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("$s", seriesId);
                    cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToUniversalTime().Ticks);
                    cmd.Parameters.AddWithValue("$v", value);
                    cmd.ExecuteNonQuery();

                    // If we created a new series for this cid, add it to the in-memory list
                    // so UI dropdowns update immediately without waiting for a DB refresh.
                    try
                    {
                        if (isNewSeries)
                        {
                            if (!AvailableCharacters.Contains(cid)) AvailableCharacters.Add(cid);
                        }

                        // Do not automatically switch the UI picker to the current character.
                        // Keep stored series and available characters up-to-date, but leave
                        // selection changes to the user's explicit actions.
                    }
                    catch (Exception ex) { LogService.Debug($"[GilTracker] Available characters update failed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] TrySaveFor failed for CID {cid}: {ex.Message}"); }
        }

        private void TryLoadSaved()
        {
            if (string.IsNullOrEmpty(_dbPath)) return;
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    if (_connection == null) return;
                    // Refresh the in-memory list of available characters first.
                    try { RefreshAvailableCharacters(); } catch (Exception ex) { LogService.Debug($"[GilTracker] RefreshAvailableCharacters failed: {ex.Message}"); }

                    // If the UI picker is currently set to 'All' (SelectedCharacterId == 0),
                    // load the aggregate series instead of the local player's series so the
                    // graph matches the picker selection on startup.
                    if (SelectedCharacterId == 0)
                    {
                        try { LoadAllCharacters(); } catch (Exception ex) { LogService.Debug($"[GilTracker] LoadAllCharacters in TryLoadSaved failed: {ex.Message}"); }
                        return;
                    }

                    // Otherwise load the explicitly selected character. If for some reason
                    // no selection is present, fall back to the local player's content id.
                    var cid = SelectedCharacterId;
                    if (cid == 0) cid = Svc.PlayerState.ContentId;
                    // If we still don't have a valid character id, nothing to load.
                    if (cid == 0) return;

                    var arr = new List<(DateTime ts, long value)>();
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
JOIN series s ON p.series_id = s.id
WHERE s.variable = $v AND s.character_id = $c
ORDER BY p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$v", "Gil");
                    cmd.Parameters.AddWithValue("$c", (long)cid);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var ticks = rdr.GetFieldValue<long>(0);
                        var val = rdr.GetFieldValue<long>(1);
                        arr.Add((new DateTime(ticks, DateTimeKind.Utc), val));
                    }
                    if (arr == null || arr.Count == 0) return;
                    _samples.Clear();
                    var start = Math.Max(0, arr.Count - _maxSamples);
                    for (var i = start; i < arr.Count; i++) _samples.Add((float)arr[i].value);
                    LastValue = _samples.Count > 0 ? _samples[^1] : LastValue;
                    if (arr.Count > 0)
                    {
                        FirstSampleTime = arr[start].ts;
                        LastSampleTime = arr[arr.Count - 1].ts;
                    }
                    RefreshAvailableCharacters();
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] TryLoadSaved read error: {ex.Message}"); /* do not crash on read errors */ }
        }

        public void RefreshAvailableCharacters()
        {
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    AvailableCharacters.Clear();
                    if (_connection == null) return;
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT DISTINCT character_id FROM series WHERE variable=$v ORDER BY character_id";
                    cmd.Parameters.AddWithValue("$v", "Gil");
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var cid = rdr.GetFieldValue<long>(0);
                        // Ignore invalid/unknown character ids (0)
                        if (cid == 0) continue;
                        AvailableCharacters.Add((ulong)cid);
                    }
                    if (AvailableCharacters.Count > 0 && !AvailableCharacters.Contains(SelectedCharacterId))
                    {
                        // Prefer selecting the currently-logged-in character if available,
                        // but only auto-switch when the active character actually changed
                        // since the last seen local CID. Do not automatically pick the
                        // first stored character to avoid forcing a selection when the
                        // user hasn't changed characters.
                        // Do not automatically change the SelectedCharacterId here. Prefer
                        // leaving the current selection untouched so the user's picker choice
                        // is not overridden when available characters are refreshed.
                        // otherwise: leave current SelectedCharacterId as-is (may be 0)
                    }
                    else if (AvailableCharacters.Count == 0)
                    {
                        // No valid saved characters — clear selection and samples
                        SelectedCharacterId = 0;
                        _samples.Clear();
                        LastValue = 0f;
                    }
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] RefreshAvailableCharacters failed: {ex.Message}"); }
        }

        /// <summary>
        /// Get a display name for the provided character id.
        /// Prefer the stored name in the `character_names` table, fall back to
        /// runtime lookup via `CharacterLib.GetCharacterName` and finally to the CID string.
        /// </summary>
        public string GetCharacterDisplayName(ulong characterId)
        {
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    if (_connection != null)
                    {
                        using var cmd = _connection.CreateCommand();
                        cmd.CommandText = "SELECT name FROM character_names WHERE character_id = $c LIMIT 1";
                        cmd.Parameters.AddWithValue("$c", (long)characterId);
                        var r = cmd.ExecuteScalar();
                        if (r != null && r != DBNull.Value)
                        {
                            var s = r as string;
                            if (!string.IsNullOrEmpty(s))
                            {
                                var sanitized = SanitizeName(s);
                                if (!string.IsNullOrEmpty(sanitized)) return sanitized;
                                return s;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] DB name lookup for CID {characterId} failed: {ex.Message}"); }

            try
            {
                var runtime = Kaleidoscope.Libs.CharacterLib.GetCharacterName(characterId);
                if (!string.IsNullOrEmpty(runtime))
                {
                    try
                    {
                        var sanitized = runtime;
                        var idxOpen = sanitized.IndexOf('(');
                        var idxClose = sanitized.LastIndexOf(')');
                        if (idxOpen >= 0 && idxClose > idxOpen)
                        {
                            sanitized = sanitized.Substring(idxOpen + 1, idxClose - idxOpen - 1).Trim();
                        }
                        if (string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
                        {
                            try { var lp = Svc.Objects.LocalPlayer?.Name.ToString(); if (!string.IsNullOrEmpty(lp)) sanitized = lp; } catch (Exception ex) { LogService.Debug($"[GilTracker] LocalPlayer fallback failed: {ex.Message}"); }
                        }
                        if (!string.IsNullOrEmpty(sanitized)) return sanitized;
                    }
                    catch (Exception ex) { LogService.Debug($"[GilTracker] Name sanitization in display failed: {ex.Message}"); return runtime; }
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] Runtime name lookup failed: {ex.Message}"); }

            return characterId.ToString();
        }

        private static string? SanitizeName(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            try
            {
                var s = raw.Trim();
                // Look for patterns like "You (Name)" or "You(Name)" and extract the inner name.
                var idxOpen = s.IndexOf('(');
                var idxClose = s.LastIndexOf(')');
                if (idxOpen >= 0 && idxClose > idxOpen)
                {
                    var inner = s.Substring(idxOpen + 1, idxClose - idxOpen - 1).Trim();
                    if (!string.IsNullOrEmpty(inner)) return inner;
                }
                // If it starts with "You " then strip that prefix.
                if (s.StartsWith("You ", StringComparison.OrdinalIgnoreCase))
                {
                    var rem = s.Substring(4).Trim();
                    if (!string.IsNullOrEmpty(rem)) return rem;
                }
                return s;
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] SanitizeName failed: {ex.Message}"); return raw; }
        }

        /// <summary>
        /// Return all stored character ids and names from the `character_names` table.
        /// Used by debug UI to list what has been persisted.
        /// </summary>
        public List<(ulong cid, string? name)> GetAllStoredCharacterNames()
        {
            var res = new List<(ulong cid, string? name)>();
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    if (_connection == null) return res;
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT character_id, name FROM character_names ORDER BY character_id";
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var cid = rdr.GetFieldValue<long>(0);
                        string? name = null;
                        if (!rdr.IsDBNull(1)) name = rdr.GetFieldValue<string>(1);
                        if (cid != 0) res.Add(((ulong)cid, name));
                    }
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] GetAllStoredCharacterNames failed: {ex.Message}"); }
            return res;
        }

        public void LoadForCharacter(ulong characterId)
        {
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    if (_connection == null) return;
                    var arr = new List<(DateTime ts, long value)>();
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
JOIN series s ON p.series_id = s.id
WHERE s.variable = $v AND s.character_id = $c
ORDER BY p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$v", "Gil");
                    cmd.Parameters.AddWithValue("$c", (long)characterId);
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var ticks = rdr.GetFieldValue<long>(0);
                        var val = rdr.GetFieldValue<long>(1);
                        arr.Add((new DateTime(ticks, DateTimeKind.Utc), val));
                    }
                    _samples.Clear();
                    var start = Math.Max(0, arr.Count - _maxSamples);
                    for (var i = start; i < arr.Count; i++) _samples.Add((float)arr[i].value);
                    LastValue = _samples.Count > 0 ? _samples[^1] : LastValue;
                    SelectedCharacterId = characterId;
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] LoadForCharacter failed for CID {characterId}: {ex.Message}"); }
        }

        /// <summary>
        /// Aggregate all stored points across all characters for the 'Gil' variable.
        /// This populates the in-memory samples list with the most-recent up-to-_maxSamples
        /// points across all characters, ordered chronologically.
        /// </summary>
        public void LoadAllCharacters()
        {
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    if (_connection == null) return;
                    // Load all points grouped by character, then build a timeline where each
                    // timestamp reflects the sum of the latest-known gil for every character
                    // at that moment. This produces an 'aggregate over time' series instead
                    // of simply concatenating all points.
                    var charPoints = new Dictionary<ulong, List<(DateTime ts, long value)>>();
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = @"SELECT s.character_id, p.timestamp, p.value FROM points p
JOIN series s ON p.series_id = s.id
WHERE s.variable = $v
ORDER BY s.character_id, p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$v", "Gil");
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var cidLong = rdr.GetFieldValue<long>(0);
                        var ticks = rdr.GetFieldValue<long>(1);
                        var val = rdr.GetFieldValue<long>(2);
                        if (cidLong == 0) continue;
                        var cid = (ulong)cidLong;
                        if (!charPoints.TryGetValue(cid, out var list))
                        {
                            list = new List<(DateTime ts, long value)>();
                            charPoints[cid] = list;
                        }
                        list.Add((new DateTime(ticks, DateTimeKind.Utc), val));
                    }

                    // If there are no characters/points, clear samples and return
                    if (charPoints.Count == 0)
                    {
                        _samples.Clear();
                        LastValue = 0f;
                        SelectedCharacterId = 0;
                        return;
                    }

                    // Collect all unique timestamps in chronological order
                    var allTimestamps = new SortedSet<DateTime>();
                    foreach (var kv in charPoints)
                        foreach (var p in kv.Value)
                            allTimestamps.Add(p.ts);

                    // Maintain per-character indices and current/latest values
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
                        // Advance each character's pointer up to this timestamp
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

                    // Trim to _maxSamples most-recent combined points
                    _samples.Clear();
                    var start = Math.Max(0, combined.Count - _maxSamples);
                    for (var i = start; i < combined.Count; i++) _samples.Add((float)combined[i].sum);
                    LastValue = _samples.Count > 0 ? _samples[^1] : LastValue;
                    if (combined.Count > 0)
                    {
                        FirstSampleTime = combined[start].ts;
                        LastSampleTime = combined[combined.Count - 1].ts;
                    }
                    SelectedCharacterId = 0;
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] LoadAllCharacters failed: {ex.Message}"); }
        }


        public void ClearForSelectedCharacter()
        {
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    if (_connection == null) return;
                    var cid = SelectedCharacterId == 0 ? Svc.PlayerState.ContentId : SelectedCharacterId;
                    // If the resolved character/content id is invalid, nothing to clear
                    if (cid == 0) return;
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable=$v AND character_id=$c);";
                    cmd.Parameters.AddWithValue("$v", "Gil");
                    cmd.Parameters.AddWithValue("$c", (long)cid);
                    cmd.ExecuteNonQuery();
                    cmd.CommandText = "DELETE FROM series WHERE variable=$v AND character_id=$c";
                    cmd.ExecuteNonQuery();
                    _samples.Clear();
                }
            }
            catch (Exception ex) { LogService.Error($"[GilTracker] ClearForSelectedCharacter failed: {ex.Message}", ex); }
        }

            /// <summary>
            /// Clear all stored data for the 'Gil' variable from the DB (all characters).
            /// </summary>
            public void ClearAllData()
            {
                try
                {
                    lock (_fileLock)
                    {
                        EnsureConnection();
                        if (_connection == null) return;
                        using var cmd = _connection.CreateCommand();
                        cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable=$v);";
                        cmd.Parameters.AddWithValue("$v", "Gil");
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "DELETE FROM series WHERE variable=$v";
                        cmd.ExecuteNonQuery();
                        _samples.Clear();
                        AvailableCharacters.Clear();
                        SelectedCharacterId = 0;
                        SetStatus("Cleared GilTracker DB.");
                    }
                }
                catch (Exception ex) { LogService.Error($"[GilTracker] ClearAllData failed: {ex.Message}", ex); }
            }

            /// <summary>
            /// Remove all stored series and points for characters that do not have a
            /// corresponding entry in the `character_names` table (i.e. no name association).
            /// Returns the number of characters removed.
            /// </summary>
            public int CleanUnassociatedCharacterData()
            {
                var removed = 0;
                if (string.IsNullOrEmpty(_dbPath)) return removed;
                try
                {
                    lock (_fileLock)
                    {
                        EnsureConnection();
                        if (_connection == null) return removed;

                        // Find all character_ids which have series data but no name association
                        var idsToRemove = new List<long>();
                        using (var sel = _connection.CreateCommand())
                        {
                            sel.CommandText = "SELECT DISTINCT character_id FROM series WHERE variable=$v AND character_id NOT IN (SELECT character_id FROM character_names)";
                            sel.Parameters.AddWithValue("$v", "Gil");
                            using var rdr = sel.ExecuteReader();
                            while (rdr.Read())
                            {
                                var cid = rdr.GetFieldValue<long>(0);
                                if (cid != 0) idsToRemove.Add(cid);
                            }
                        }

                        if (idsToRemove.Count == 0)
                        {
                            SetStatus("No unassociated characters found.");
                            return 0;
                        }

                        // Delete points and series for those character ids
                        using (var tx = _connection.BeginTransaction())
                        {
                            try
                            {
                                foreach (var cid in idsToRemove)
                                {
                                    using var cmd = _connection.CreateCommand();
                                    cmd.CommandText = "DELETE FROM points WHERE series_id IN (SELECT id FROM series WHERE variable=$v AND character_id=$c);";
                                    cmd.Parameters.AddWithValue("$v", "Gil");
                                    cmd.Parameters.AddWithValue("$c", cid);
                                    cmd.ExecuteNonQuery();

                                    cmd.CommandText = "DELETE FROM series WHERE variable=$v AND character_id=$c";
                                    cmd.ExecuteNonQuery();
                                    removed++;
                                }

                                tx.Commit();
                            }
                            catch (Exception ex)
                            {
                                LogService.Error($"[GilTracker] CleanUnassociatedCharacterData transaction failed: {ex.Message}", ex);
                                try { tx.Rollback(); } catch (Exception rollbackEx) { LogService.Debug($"[GilTracker] Rollback failed: {rollbackEx.Message}"); }
                            }
                        }

                        // Refresh in-memory available characters state
                        try { RefreshAvailableCharacters(); } catch (Exception ex) { LogService.Debug($"[GilTracker] RefreshAvailableCharacters after clean failed: {ex.Message}"); }
                        SetStatus($"Cleaned {removed} unassociated character(s) from DB.");
                    }
                }
                catch (Exception ex) { LogService.Error($"[GilTracker] CleanUnassociatedCharacterData failed: {ex.Message}", ex); }
                return removed;
            }

        public string? ExportCsv(ulong? characterId = null)
        {
            if (string.IsNullOrEmpty(_dbPath)) return null;
            try
            {
                EnsureConnection();
                if (_connection == null) return null;
                var cid = characterId == null || characterId == 0 ? SelectedCharacterId : (ulong)characterId;
                using var cmd = _connection.CreateCommand();
                // If cid == 0 treat as "All" and export across all characters
                if (cid == 0)
                {
                    cmd.CommandText = @"SELECT p.timestamp, p.value, s.character_id FROM points p
JOIN series s ON p.series_id = s.id
WHERE s.variable = $v
ORDER BY p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$v", "Gil");
                    using var rdrAll = cmd.ExecuteReader();
                    var sbAll = new StringBuilder();
                    sbAll.AppendLine("timestamp_utc,value,character_id");
                    while (rdrAll.Read())
                    {
                        var ticks = rdrAll.GetFieldValue<long>(0);
                        var val = rdrAll.GetFieldValue<long>(1);
                        var cidOut = rdrAll.GetFieldValue<long>(2);
                        sbAll.AppendLine($"{new DateTime(ticks, DateTimeKind.Utc):O},{val},{cidOut}");
                    }
                    var saveDirAll = ECommons.DalamudServices.Svc.PluginInterface.GetPluginConfigDirectory();
                    var fileNameAll = Path.Combine(saveDirAll, $"giltracker-gil-all-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv");
                    File.WriteAllText(fileNameAll, sbAll.ToString());
                    SetStatus($"Exported to {fileNameAll}");
                    return fileNameAll;
                }

                // Single-character export
                // Resolve selected character -> if SelectedCharacterId == 0 and no explicit id provided, fallback to local player
                if (cid == 0 && SelectedCharacterId == 0)
                {
                    cid = Svc.PlayerState.ContentId;
                }
                // Do not export when we don't have a valid character/content id
                if (cid == 0) return null;
                cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
JOIN series s ON p.series_id = s.id
WHERE s.variable = $v AND s.character_id = $c
ORDER BY p.timestamp ASC";
                cmd.Parameters.AddWithValue("$v", "Gil");
                cmd.Parameters.AddWithValue("$c", (long)cid);
                using var rdrSingle = cmd.ExecuteReader();
                var sbSingle = new StringBuilder();
                sbSingle.AppendLine("timestamp_utc,value");
                while (rdrSingle.Read())
                {
                    var ticks = rdrSingle.GetFieldValue<long>(0);
                    var val = rdrSingle.GetFieldValue<long>(1);
                    sbSingle.AppendLine($"{new DateTime(ticks, DateTimeKind.Utc):O},{val}");
                }
                var saveDir = ECommons.DalamudServices.Svc.PluginInterface.GetPluginConfigDirectory();
                // Try to include character name in the filename if available to make exported files easier to identify.
                var name = Kaleidoscope.Libs.CharacterLib.GetCharacterName(cid);
                var safeName = string.IsNullOrEmpty(name) ? cid.ToString() : string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')).Replace(' ', '_');
                var fileName = Path.Combine(saveDir, $"giltracker-gil-{safeName}-{cid}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv");
                File.WriteAllText(fileName, sbSingle.ToString());
                SetStatus($"Exported to {fileName}");
                return fileName;
            }
            catch (Exception ex)
            {
                LogService.Error($"[GilTracker] ExportCsv failed: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Retrieve all stored points (timestamp + value) for the provided character id (or selected character if null).
        /// This is used for debug UI to display exact timestamps.
        /// </summary>
        public List<(DateTime ts, long value)> GetPoints(ulong? characterId = null)
        {
            var res = new List<(DateTime ts, long value)>();
            if (string.IsNullOrEmpty(_dbPath)) return res;
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    if (_connection == null) return res;
                    var cid = characterId == null || characterId == 0 ? SelectedCharacterId : (ulong)characterId;
                    using var cmd = _connection.CreateCommand();
                    // If cid == 0 treat as "All" and return all points across characters
                    if (cid == 0)
                    {
                        cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
JOIN series s ON p.series_id = s.id
WHERE s.variable = $v
ORDER BY p.timestamp ASC";
                        cmd.Parameters.AddWithValue("$v", "Gil");
                        using var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var ticks = rdr.GetFieldValue<long>(0);
                            var val = rdr.GetFieldValue<long>(1);
                            res.Add((new DateTime(ticks, DateTimeKind.Utc), val));
                        }
                        return res;
                    }

                    // Single-character retrieval
                    if (cid == 0 && SelectedCharacterId == 0)
                    {
                        cid = Svc.PlayerState.ContentId;
                    }
                    if (cid == 0) return res;
                    cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
JOIN series s ON p.series_id = s.id
WHERE s.variable = $v AND s.character_id = $c
ORDER BY p.timestamp ASC";
                    cmd.Parameters.AddWithValue("$v", "Gil");
                    cmd.Parameters.AddWithValue("$c", (long)cid);
                    using var rdr2 = cmd.ExecuteReader();
                    while (rdr2.Read())
                    {
                        var ticks = rdr2.GetFieldValue<long>(0);
                        var val = rdr2.GetFieldValue<long>(1);
                        res.Add((new DateTime(ticks, DateTimeKind.Utc), val));
                    }
                }
            }
            catch (Exception ex) { LogService.Debug($"[GilTracker] GetPoints failed: {ex.Message}"); }
            return res;
        }

        public void SampleFromGame()
        {
            try
            {
                unsafe
                {
                    // Prefer InventoryManager if available
                    var im = Kaleidoscope.Services.GameStateService.InventoryManagerInstance();
                    if (im != null)
                    {
                        try
                        {
                            var gil = (float)im->GetGil();
                            if (Math.Abs(gil - LastValue) > 0.0001f)
                            {
                                PushSample(gil);
                            }
                            else
                            {
                                SetStatus("Skipping sample identical to last.");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug($"[GilTracker] InventoryManager read failed: {ex.Message}");
                            SetStatus("Failed to read gil from InventoryManager.");
                        }
                        return;
                    }

                    // Fallback to CurrencyManager only if InventoryManager isn't available
                    var cm = Kaleidoscope.Services.GameStateService.CurrencyManagerInstance();
                    if (cm != null)
                    {
                        try
                        {
                            var gil = (float)cm->GetItemCount(1);
                            if (Math.Abs(gil - LastValue) > 0.0001f)
                            {
                                PushSample(gil);
                            }
                            else
                            {
                                SetStatus("Skipping sample identical to last.");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug($"[GilTracker] CurrencyManager read failed: {ex.Message}");
                            SetStatus("Failed to read gil from CurrencyManager.");
                        }
                        return;
                    }

                    SetStatus("No game currency data available; sampling skipped.");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Sampling error: {ex.Message}");
            }
        }
    }
}
