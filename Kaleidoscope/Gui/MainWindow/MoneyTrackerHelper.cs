using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ECommons.DalamudServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Kaleidoscope.Gui.MainWindow
{
    internal class MoneyTrackerHelper
    {
        private readonly object _fileLock = new();
        private SqliteConnection? _connection;
        private readonly string? _dbPath;
        private readonly int _maxSamples;
        private readonly Random _rnd = new();
        private readonly List<float> _samples;
        public IReadOnlyList<float> Samples => _samples;

        public float LastValue { get; private set; }
        public DateTime? FirstSampleTime { get; private set; }
        public DateTime? LastSampleTime { get; private set; }
        public List<ulong> AvailableCharacters { get; } = new();
        public ulong SelectedCharacterId { get; private set; }
        public string LastStatusMessage { get; private set; } = string.Empty;

        public MoneyTrackerHelper(string? dbPath, int maxSamples = 200, float startingValue = 100000f)
        {
            _dbPath = dbPath;
            _maxSamples = maxSamples;
            _samples = new List<float>();
            LastValue = startingValue;
            // Seed initial values so UI isn't empty
            for (var i = 0; i < 40; i++) _samples.Add(SimulateNext());
            if (_samples.Count > 0) LastValue = _samples[^1];
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

CREATE INDEX IF NOT EXISTS idx_series_variable_character ON series(variable, character_id);
CREATE INDEX IF NOT EXISTS idx_points_series_timestamp ON points(series_id, timestamp);
";
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        public void PushSample(float v)
        {
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
                    if (r != null && r != DBNull.Value) seriesId = (long)r;
                    else
                    {
                        cmd.CommandText = "INSERT INTO series(variable, character_id) VALUES($v, $c); SELECT last_insert_rowid();";
                        seriesId = (long)cmd.ExecuteScalar();
                    }
                    cmd.CommandText = "INSERT INTO points(series_id, timestamp, value) VALUES($s, $t, $v)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("$s", seriesId);
                    cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToUniversalTime().Ticks);
                    cmd.Parameters.AddWithValue("$v", (long)LastValue);
                    cmd.ExecuteNonQuery();
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
                        AvailableCharacters.Add((ulong)cid);
                    }
                    if (AvailableCharacters.Count > 0 && !AvailableCharacters.Contains(SelectedCharacterId))
                    {
                        SelectedCharacterId = AvailableCharacters[0];
                        LoadForCharacter(SelectedCharacterId);
                    }
                }
            }
            catch { }
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

        public float SimulateNext()
        {
            var delta = (float)(_rnd.NextDouble() * 15000.0 - 5000.0);
            var next = LastValue + delta;
            if (next < 0) next = 0;
            return next;
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
                        LastStatusMessage = "Cleared Money Tracker DB.";
                    }
                }
                catch { }
            }

        public string? ExportCsv(ulong? characterId = null)
        {
            if (string.IsNullOrEmpty(_dbPath)) return null;
            try
            {
                EnsureConnection();
                if (_connection == null) return null;
                var cid = characterId == null || characterId == 0 ? SelectedCharacterId == 0 ? Svc.ClientState.LocalContentId : SelectedCharacterId : (ulong)characterId;
                // Do not export when we don't have a valid character/content id
                if (cid == 0) return null;
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT p.timestamp, p.value FROM points p
JOIN series s ON p.series_id = s.id
WHERE s.variable = $v AND s.character_id = $c
ORDER BY p.timestamp ASC";
                cmd.Parameters.AddWithValue("$v", "Gil");
                cmd.Parameters.AddWithValue("$c", (long)cid);
                using var rdr = cmd.ExecuteReader();
                var sb = new StringBuilder();
                sb.AppendLine("timestamp_utc,value");
                while (rdr.Read())
                {
                    var ticks = rdr.GetFieldValue<long>(0);
                    var val = rdr.GetFieldValue<long>(1);
                    sb.AppendLine($"{new DateTime(ticks, DateTimeKind.Utc):O},{val}");
                }
                var saveDir = ECommons.DalamudServices.Svc.PluginInterface.GetPluginConfigDirectory();
                // Try to include character name in the filename if available to make exported files easier to identify.
                var name = Kaleidoscope.Libs.CharacterLib.GetCharacterName(cid);
                var safeName = string.IsNullOrEmpty(name) ? cid.ToString() : string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')).Replace(' ', '_');
                var fileName = Path.Combine(saveDir, $"moneytracker-gil-{safeName}-{cid}-{DateTime.UtcNow:yyyyMMddTHHmmssZ}.csv");
                File.WriteAllText(fileName, sb.ToString());
                LastStatusMessage = $"Exported to {fileName}";
                return fileName;
            }
            catch
            {
                return null;
            }
        }

        public void SampleFromGameOrSimulate()
        {
            try
            {
                unsafe
                {
                    var cm = CurrencyManager.Instance();
                    if (cm != null)
                    {
                        try
                        {
                            uint gil = 0;
                            try
                            {
                                var im = InventoryManager.Instance();
                                if (im != null)
                                    gil = im->GetGil();
                            }
                            catch { gil = 0; }
                            if (gil == 0)
                            {
                                try { gil = cm->GetItemCount(1); } catch { gil = 0; }
                            }
                            if (gil != 0) PushSample((float)gil);
                            else PushSample(SimulateNext());
                        }
                        catch
                        {
                            PushSample(SimulateNext());
                        }
                        return;
                    }
                }
            }
            catch { }
            PushSample(SimulateNext());
        }
    }
}
