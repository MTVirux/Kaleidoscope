using Microsoft.Data.Sqlite;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Text;

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
                    catch { }
                }
            }
            catch { }
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
                    catch { }
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
                    catch { }
                }
            }
            catch { }
        }

        public void PushSample(float v)
        {
            // Avoid storing duplicate consecutive samples for the same character.
            // Also avoid storing if the new value equals the last known value (e.g. when samples list is empty).
            if (Math.Abs(LastValue - v) < 0.0001f)
            {
                SetStatus("Duplicate sample; skipped (matches last value).");
                return;
            }
            if (_samples.Count > 0 && Math.Abs(_samples[^1] - v) < 0.0001f)
            {
                SetStatus("Duplicate sample; skipped.");
                return;
            }
            _samples.Add(v);
            if (_samples.Count > _maxSamples) _samples.RemoveAt(0);
            LastValue = v;
            TrySave();
        }

        private void TrySave()
        {
            if (string.IsNullOrEmpty(_dbPath)) return;
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    var cid = Svc.ClientState.LocalContentId;
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
                            // Strip any "You (Name)" wrapper so we persist only the character name.
                            try
                            {
                                var sanitized = name;
                                var idxOpen = sanitized.IndexOf('(');
                                var idxClose = sanitized.LastIndexOf(')');
                                if (idxOpen >= 0 && idxClose > idxOpen)
                                {
                                    sanitized = sanitized.Substring(idxOpen + 1, idxClose - idxOpen - 1).Trim();
                                }
                                // If the sanitized result is still just "You", try to get the local player name directly.
                                if (string.Equals(sanitized, "You", StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        var lp = Svc.ClientState.LocalPlayer?.Name.ToString();
                                        if (!string.IsNullOrEmpty(lp)) sanitized = lp;
                                    }
                                    catch { }
                                }

                                if (!string.IsNullOrEmpty(sanitized))
                                {
                                    using var nameCmd = _connection.CreateCommand();
                                    nameCmd.CommandText = "INSERT OR REPLACE INTO character_names(character_id, name) VALUES($c, $n)";
                                    nameCmd.Parameters.AddWithValue("$c", (long)cid);
                                    nameCmd.Parameters.AddWithValue("$n", sanitized);
                                    nameCmd.ExecuteNonQuery();
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    cmd.CommandText = "INSERT INTO points(series_id, timestamp, value) VALUES($s, $t, $v)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("$s", seriesId);
                    cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToUniversalTime().Ticks);
                    cmd.Parameters.AddWithValue("$v", (long)LastValue);
                    cmd.ExecuteNonQuery();

                    // If we created a new series for this cid, add it to the in-memory list
                    // so UI dropdowns update immediately without waiting for a DB refresh.
                    try
                    {
                        if (isNewSeries)
                        {
                            if (!AvailableCharacters.Contains(cid)) AvailableCharacters.Add(cid);
                        }

                        // If the sample belongs to the currently-logged-in character,
                        // automatically switch the selection to it only when the
                        // active character actually changed since the last seen value.
                        try
                        {
                            var localCid = Svc.ClientState.LocalContentId;
                            if (localCid != 0 && cid == localCid && localCid != _lastSeenLocalCid)
                            {
                                SelectedCharacterId = cid;
                                LoadForCharacter(cid);
                                _lastSeenLocalCid = localCid;
                            }
                        }
                        catch { }
                    }
                    catch { }
                }
            }
            catch { /* swallow save errors */ }
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
                    var cid = Svc.ClientState.LocalContentId;
                    // If we don't have a valid character/content id, do not attempt to load saved data
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
            catch { /* do not crash on read errors */ }
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
                        var localCid = Svc.ClientState.LocalContentId;
                        if (localCid != 0 && AvailableCharacters.Contains(localCid) && localCid != _lastSeenLocalCid)
                        {
                            SelectedCharacterId = localCid;
                            LoadForCharacter(SelectedCharacterId);
                            _lastSeenLocalCid = localCid;
                        }
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
            catch { }
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
            catch { }

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
                            try { var lp = Svc.ClientState.LocalPlayer?.Name.ToString(); if (!string.IsNullOrEmpty(lp)) sanitized = lp; } catch { }
                        }
                        if (!string.IsNullOrEmpty(sanitized)) return sanitized;
                    }
                    catch { return runtime; }
                }
            }
            catch { }

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
            catch { return raw; }
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
            catch { }
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
            catch { }
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
                    var arr = new List<(DateTime ts, long value)>();
                    using var cmd = _connection.CreateCommand();
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
                        arr.Add((new DateTime(ticks, DateTimeKind.Utc), val));
                    }

                    _samples.Clear();
                    var start = Math.Max(0, arr.Count - _maxSamples);
                    for (var i = start; i < arr.Count; i++) _samples.Add((float)arr[i].value);
                    LastValue = _samples.Count > 0 ? _samples[^1] : LastValue;
                    if (arr.Count > 0)
                    {
                        FirstSampleTime = arr[start].ts;
                        LastSampleTime = arr[arr.Count - 1].ts;
                    }
                    // Represent aggregated selection with character id 0
                    SelectedCharacterId = 0;
                }
            }
            catch { }
        }


        public void ClearForSelectedCharacter()
        {
            try
            {
                lock (_fileLock)
                {
                    EnsureConnection();
                    if (_connection == null) return;
                    var cid = SelectedCharacterId == 0 ? Svc.ClientState.LocalContentId : SelectedCharacterId;
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
            catch { }
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
                catch { }
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
                            catch
                            {
                                try { tx.Rollback(); } catch { }
                            }
                        }

                        // Refresh in-memory available characters state
                        try { RefreshAvailableCharacters(); } catch { }
                        SetStatus($"Cleaned {removed} unassociated character(s) from DB.");
                    }
                }
                catch { }
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
                    cid = Svc.ClientState.LocalContentId;
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
            catch
            {
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
                        cid = Svc.ClientState.LocalContentId;
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
            catch { }
            return res;
        }

        public void SampleFromGame()
        {
            try
            {
                unsafe
                {
                    // Prefer InventoryManager if available
                    var im = InventoryManager.Instance();
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
                        catch
                        {
                            SetStatus("Failed to read gil from InventoryManager.");
                        }
                        return;
                    }

                    // Fallback to CurrencyManager only if InventoryManager isn't available
                    var cm = CurrencyManager.Instance();
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
                        catch
                        {
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
