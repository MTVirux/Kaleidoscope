using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.MainWindow;
using Newtonsoft.Json;
using OtterGui.Services;
using System.Timers;

namespace Kaleidoscope.Services;

/// <summary>
/// Pending action blocked due to unsaved layout changes.
/// </summary>
public sealed class PendingLayoutAction
{
    public string Description { get; set; } = string.Empty;
    public Action? ContinueAction { get; set; }
    public Action? CancelAction { get; set; }
}

/// <summary>
/// Manages layout editing with explicit save semantics (like a file editor).
/// Changes are applied immediately but only persisted on explicit Save.
/// </summary>
/// <remarks>
/// This follows the "dirty flag" pattern common in document editors. The working
/// layout is kept in memory and a snapshot is persisted to disk for crash recovery.
/// </remarks>
public sealed class LayoutEditingService : IDisposable, IService
{
    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly FilenameService _filenameService;

    private bool _isDirty;
    private List<ToolLayoutState>? _workingLayout;
    private LayoutGridSettings? _workingGridSettings;
    private string _currentLayoutName = string.Empty;
    private LayoutType _currentLayoutType = LayoutType.Windowed;
    private PendingLayoutAction? _pendingAction;
    private bool _showUnsavedChangesDialog;

    // Phase 6: Debounced snapshot saves
    private System.Timers.Timer? _snapshotDebounceTimer;
    private readonly object _snapshotLock = new();
    private const int SnapshotDebounceMs = 1000; // Wait 1s before writing snapshot
    
    // Phase 6: Tool lookup cache
    private Dictionary<string, ToolLayoutState>? _toolByNameCache;
    private bool _toolCacheValid;
    
    // Phase 6: Pre-computed grid dimensions cache
    private (int Columns, int Rows, float AspectWidth, float AspectHeight)? _cachedGridDimensions;
    
    // Phase 6: Statistics
    private long _saveCount;
    private long _discardCount;
    private long _snapshotWriteCount;
    private long _snapshotSkippedCount;
    private long _dirtyMarkCount;
    private DateTime? _lastSaveTime;
    private DateTime? _lastSnapshotTime;

    private string DirtySnapshotPath => Path.Combine(_filenameService.ConfigDirectory, "layout_dirty_snapshot.json");

    public event Action<bool>? OnDirtyStateChanged;
    public event Action? OnShowUnsavedChangesDialog;
    
    /// <summary>
    /// Raised when the layout is reverted to persisted state (e.g., after Discard).
    /// UI should refresh to reflect the reverted layout.
    /// </summary>
    public event Action? OnLayoutReverted;

    public bool IsDirty => _isDirty;
    public bool ShowUnsavedChangesDialog => _showUnsavedChangesDialog;
    public PendingLayoutAction? PendingAction => _pendingAction;
    public string CurrentLayoutName => _currentLayoutName;
    public LayoutType CurrentLayoutType => _currentLayoutType;
    public List<ToolLayoutState>? WorkingLayout => _workingLayout;
    public LayoutGridSettings? WorkingGridSettings => _workingGridSettings;

    public LayoutEditingService(IPluginLog log, ConfigurationService configService, FilenameService filenameService)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _filenameService = filenameService ?? throw new ArgumentNullException(nameof(filenameService));

        _snapshotDebounceTimer = new System.Timers.Timer(SnapshotDebounceMs);
        _snapshotDebounceTimer.Elapsed += OnSnapshotDebounceElapsed;
        _snapshotDebounceTimer.AutoReset = false;

        TryRestoreDirtySnapshot();
        _log.Debug("LayoutEditingService initialized with debounced snapshot saves");
    }

    private void OnSnapshotDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        lock (_snapshotLock)
        {
            if (_isDirty)
            {
                SaveDirtySnapshotInternal();
            }
        }
    }

    /// <summary>
    /// Initializes the working layout from persisted state.
    /// </summary>
    public void InitializeFromPersisted(string layoutName, LayoutType layoutType, List<ToolLayoutState>? tools, LayoutGridSettings? gridSettings)
    {
        // Keep restored dirty state if present
        if (_isDirty && _workingLayout != null)
        {
            _log.Debug($"Keeping restored dirty state for '{_currentLayoutName}'");
            return;
        }

        _currentLayoutName = layoutName;
        _currentLayoutType = layoutType;
        _workingLayout = tools != null ? CloneToolList(tools) : new List<ToolLayoutState>();
        _workingGridSettings = gridSettings?.Clone();
        _isDirty = false;

        _log.Debug($"Initialized from persisted layout '{layoutName}' ({_workingLayout.Count} tools)");
    }

    /// <summary>
    /// Marks the working layout as changed.
    /// If auto-save is enabled, saves immediately instead of marking dirty.
    /// </summary>
    public void MarkDirty(List<ToolLayoutState>? currentTools, LayoutGridSettings? currentGridSettings)
    {
        _workingLayout = currentTools != null ? CloneToolList(currentTools) : _workingLayout;
        _workingGridSettings = currentGridSettings?.Clone() ?? _workingGridSettings;

        // Auto-save if enabled
        if (_configService.Config.AutoSaveLayoutChanges)
        {
            // Temporarily set dirty to allow Save() to work
            var wasDirty = _isDirty;
            _isDirty = true;
            Save();
            _log.Debug($"Layout auto-saved for '{_currentLayoutName}'");
            return;
        }

        if (!_isDirty)
        {
            _isDirty = true;
            _log.Debug($"Layout marked dirty for '{_currentLayoutName}'");
            OnDirtyStateChanged?.Invoke(true);
        }

        Interlocked.Increment(ref _dirtyMarkCount);
        InvalidateToolCache();
        InvalidateGridCache();
        ScheduleDirtySnapshot();
    }

    /// <summary>
    /// Updates the working layout without marking dirty.
    /// </summary>
    public void UpdateWorkingLayout(List<ToolLayoutState>? tools, LayoutGridSettings? gridSettings)
    {
        _workingLayout = tools != null ? CloneToolList(tools) : _workingLayout;
        _workingGridSettings = gridSettings?.Clone() ?? _workingGridSettings;
        InvalidateToolCache();
        InvalidateGridCache();

        if (_isDirty)
            ScheduleDirtySnapshot();
    }

    /// <summary>
    /// Saves the current working layout to persistent storage.
    /// </summary>
    public void Save()
    {
        if (!_isDirty)
        {
            _log.Debug("Save called but layout is not dirty");
            return;
        }

        try
        {
            var layouts = _configService.Config.Layouts ??= new List<ContentLayoutState>();
            var existing = layouts.Find(x => x.Name == _currentLayoutName && x.Type == _currentLayoutType);

            if (existing == null)
            {
                existing = new ContentLayoutState { Name = _currentLayoutName, Type = _currentLayoutType };
                layouts.Add(existing);
            }

            existing.Tools = _workingLayout != null ? CloneToolList(_workingLayout) : new List<ToolLayoutState>();
            _workingGridSettings?.ApplyToLayoutState(existing);

            // Update active layout name
            if (_currentLayoutType == LayoutType.Windowed)
                _configService.Config.ActiveWindowedLayoutName = _currentLayoutName;
                else
                    _configService.Config.ActiveFullscreenLayoutName = _currentLayoutName;
                
                _configService.Save();
                _configService.SaveLayouts();
                
                _isDirty = false;
                ClearDirtySnapshot();
                
                Interlocked.Increment(ref _saveCount);
                _lastSaveTime = DateTime.UtcNow;
                
                _log.Information($"LayoutEditingService: Saved layout '{_currentLayoutName}' ({existing.Tools.Count} tools)");
                OnDirtyStateChanged?.Invoke(false);
            }
            catch (Exception ex)
            {
                _log.Error($"LayoutEditingService: Failed to save layout: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Discards unsaved changes and reverts to the persisted layout.
        /// </summary>
        public void DiscardChanges()
        {
            if (!_isDirty)
            {
                _log.Debug("LayoutEditingService: DiscardChanges called but layout is not dirty");
                return;
            }
            
            Interlocked.Increment(ref _discardCount);
            
            try
            {
                // Reload from persisted
                var layouts = _configService.Config.Layouts ?? new List<ContentLayoutState>();
                var persisted = layouts.Find(x => x.Name == _currentLayoutName && x.Type == _currentLayoutType);
                
                if (persisted != null)
                {
                    _workingLayout = persisted.Tools != null ? CloneToolList(persisted.Tools) : new List<ToolLayoutState>();
                    _workingGridSettings = LayoutGridSettings.FromLayoutState(persisted);
                }
                else
                {
                    _workingLayout = new List<ToolLayoutState>();
                    _workingGridSettings = new LayoutGridSettings();
                }
                
                _isDirty = false;
                ClearDirtySnapshot();
                
                _log.Information($"LayoutEditingService: Discarded changes, reverted to persisted layout '{_currentLayoutName}'");
                OnDirtyStateChanged?.Invoke(false);
                OnLayoutReverted?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Error($"LayoutEditingService: Failed to discard changes: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Attempts to perform an action that would discard unsaved changes.
        /// If dirty, blocks the action and shows the unsaved changes dialog.
        /// Returns true if the action proceeded immediately (not dirty).
        /// </summary>
        public bool TryPerformDestructiveAction(string description, Action continueAction, Action? cancelAction = null)
        {
            if (!_isDirty)
            {
                // Not dirty, invoke action immediately and return true
                continueAction();
                return true;
            }
            
            // Block the action and show dialog
            _pendingAction = new PendingLayoutAction
            {
                Description = description,
                ContinueAction = continueAction,
                CancelAction = cancelAction
            };
            _showUnsavedChangesDialog = true;
            OnShowUnsavedChangesDialog?.Invoke();
            
            _log.Debug($"LayoutEditingService: Blocked destructive action '{description}' due to unsaved changes");
            return false;
        }
        
        /// <summary>
        /// Handles the user's choice in the unsaved changes dialog.
        /// </summary>
        public void HandleUnsavedChangesChoice(UnsavedChangesChoice choice)
        {
            var pending = _pendingAction;
            _pendingAction = null;
            _showUnsavedChangesDialog = false;
            
            switch (choice)
            {
                case UnsavedChangesChoice.Save:
                    Save();
                    pending?.ContinueAction?.Invoke();
                    _log.Debug("LayoutEditingService: User chose Save, continuing action");
                    break;
                    
                case UnsavedChangesChoice.Discard:
                    DiscardChanges();
                    pending?.ContinueAction?.Invoke();
                    _log.Debug("LayoutEditingService: User chose Discard, continuing action");
                    break;
                    
                case UnsavedChangesChoice.Cancel:
                    pending?.CancelAction?.Invoke();
                    _log.Debug("LayoutEditingService: User chose Cancel, action aborted");
                    break;
            }
        }
        
        /// <summary>
        /// Closes the unsaved changes dialog without taking action.
        /// </summary>
        public void CloseUnsavedChangesDialog()
        {
            _pendingAction?.CancelAction?.Invoke();
            _pendingAction = null;
            _showUnsavedChangesDialog = false;
        }
        
        /// <summary>
        /// Switches to a different layout. If dirty, prompts to save first.
        /// </summary>
        public bool TrySwitchLayout(string newLayoutName, LayoutType newLayoutType, Action applyLayoutAction)
        {
            if (newLayoutName == _currentLayoutName && newLayoutType == _currentLayoutType)
            {
                // Same layout, no need to switch
                return true;
            }
            
            return TryPerformDestructiveAction(
                $"switch to layout '{newLayoutName}'",
                () =>
                {
                    // Clear current state
                    _currentLayoutName = newLayoutName;
                    _currentLayoutType = newLayoutType;
                    _isDirty = false;
                    _workingLayout = null;
                    _workingGridSettings = null;
                    ClearDirtySnapshot();
                    
                    // Apply the new layout
                    applyLayoutAction();
                }
            );
        }
        
        #region Dirty Snapshot Persistence
        
        private class DirtySnapshot
        {
            public string LayoutName { get; set; } = string.Empty;
            public LayoutType LayoutType { get; set; } = LayoutType.Windowed;
            public List<ToolLayoutState>? Tools { get; set; }
            public LayoutGridSettings? GridSettings { get; set; }
        }
        
        /// <summary>
        /// Schedules a debounced dirty snapshot save.
        /// </summary>
        private void ScheduleDirtySnapshot()
        {
            lock (_snapshotLock)
            {
                _snapshotDebounceTimer?.Stop();
                _snapshotDebounceTimer?.Start();
                Interlocked.Increment(ref _snapshotSkippedCount);
            }
        }
        
        /// <summary>
        /// Immediately saves the dirty snapshot (bypassing debounce).
        /// </summary>
        public void FlushDirtySnapshot()
        {
            lock (_snapshotLock)
            {
                _snapshotDebounceTimer?.Stop();
                if (_isDirty)
                {
                    SaveDirtySnapshotInternal();
                }
            }
        }
        
        private void SaveDirtySnapshotInternal()
        {
            try
            {
                var snapshot = new DirtySnapshot
                {
                    LayoutName = _currentLayoutName,
                    LayoutType = _currentLayoutType,
                    Tools = _workingLayout,
                    GridSettings = _workingGridSettings
                };
                
                var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                File.WriteAllText(DirtySnapshotPath, json);
                
                Interlocked.Increment(ref _snapshotWriteCount);
                _lastSnapshotTime = DateTime.UtcNow;
                
                _log.Verbose("LayoutEditingService: Saved dirty snapshot");
            }
            catch (Exception ex)
            {
                _log.Warning($"LayoutEditingService: Failed to save dirty snapshot: {ex.Message}");
            }
        }
        
        private void ClearDirtySnapshot()
        {
            try
            {
                if (File.Exists(DirtySnapshotPath))
                {
                    File.Delete(DirtySnapshotPath);
                    _log.Verbose("LayoutEditingService: Cleared dirty snapshot");
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"LayoutEditingService: Failed to clear dirty snapshot: {ex.Message}");
            }
        }
        
        private void TryRestoreDirtySnapshot()
        {
            try
            {
                if (!File.Exists(DirtySnapshotPath))
                    return;
                
                var json = File.ReadAllText(DirtySnapshotPath);
                var snapshot = JsonConvert.DeserializeObject<DirtySnapshot>(json);
                
                if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.LayoutName))
                {
                    _currentLayoutName = snapshot.LayoutName;
                    _currentLayoutType = snapshot.LayoutType;
                    _workingLayout = snapshot.Tools;
                    _workingGridSettings = snapshot.GridSettings;
                    _isDirty = true;
                    
                    _log.Information($"LayoutEditingService: Restored dirty snapshot for layout '{_currentLayoutName}'");
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"LayoutEditingService: Failed to restore dirty snapshot: {ex.Message}");
                // Clean up corrupt snapshot
                ClearDirtySnapshot();
            }
        }
        
        #endregion
        
        #region Helpers
        
        private static List<ToolLayoutState> CloneToolList(List<ToolLayoutState> source)
        {
            // Deep clone by serializing/deserializing
            var json = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<List<ToolLayoutState>>(json) ?? new List<ToolLayoutState>();
        }
        
        #endregion
        
        #region Tool Lookup Cache
        
        /// <summary>
        /// Gets a tool by name from the working layout using a cached lookup.
        /// </summary>
        public ToolLayoutState? GetToolByName(string toolName)
        {
            EnsureToolCacheValid();
            if (_toolByNameCache != null && _toolByNameCache.TryGetValue(toolName, out var tool))
            {
                return tool;
            }
            return null;
        }
        
        /// <summary>
        /// Gets all tool names in the current layout.
        /// </summary>
        public IReadOnlyList<string> GetToolNames()
        {
            EnsureToolCacheValid();
            return _toolByNameCache?.Keys.ToList() ?? new List<string>();
        }
        
        /// <summary>
        /// Checks if a tool exists by name.
        /// </summary>
        public bool HasTool(string toolName)
        {
            EnsureToolCacheValid();
            return _toolByNameCache?.ContainsKey(toolName) ?? false;
        }
        
        private void EnsureToolCacheValid()
        {
            if (_toolCacheValid && _toolByNameCache != null)
                return;
            
            _toolByNameCache = new Dictionary<string, ToolLayoutState>(StringComparer.OrdinalIgnoreCase);
            if (_workingLayout != null)
            {
                foreach (var tool in _workingLayout)
                {
                    if (!string.IsNullOrEmpty(tool.Title))
                    {
                        _toolByNameCache[tool.Title] = tool;
                    }
                }
            }
            _toolCacheValid = true;
        }
        
        private void InvalidateToolCache()
        {
            _toolCacheValid = false;
            _toolByNameCache = null;
        }
        
        #endregion
        
        #region Grid Dimensions Cache
        
        /// <summary>
        /// Gets the effective grid dimensions (cached).
        /// </summary>
        public (int Columns, int Rows) GetEffectiveGridDimensions(float aspectWidth = 16f, float aspectHeight = 9f)
        {
            if (_cachedGridDimensions.HasValue && 
                _cachedGridDimensions.Value.AspectWidth == aspectWidth &&
                _cachedGridDimensions.Value.AspectHeight == aspectHeight)
            {
                return (_cachedGridDimensions.Value.Columns, _cachedGridDimensions.Value.Rows);
            }
            
            var settings = _workingGridSettings ?? new LayoutGridSettings();
            var columns = settings.GetEffectiveColumns(aspectWidth, aspectHeight);
            var rows = settings.GetEffectiveRows(aspectWidth, aspectHeight);
            
            _cachedGridDimensions = (columns, rows, aspectWidth, aspectHeight);
            return (columns, rows);
        }
        
        private void InvalidateGridCache()
        {
            _cachedGridDimensions = null;
        }
        
        #endregion
        
        #region Statistics
        
        /// <summary>Number of layout saves performed.</summary>
        public long SaveCount => Interlocked.Read(ref _saveCount);
        
        /// <summary>Number of times changes were discarded.</summary>
        public long DiscardCount => Interlocked.Read(ref _discardCount);
        
        /// <summary>Number of dirty snapshots written to disk.</summary>
        public long SnapshotWriteCount => Interlocked.Read(ref _snapshotWriteCount);
        
        /// <summary>Number of snapshot writes skipped due to debouncing.</summary>
        public long SnapshotSkippedCount => Interlocked.Read(ref _snapshotSkippedCount);
        
        /// <summary>Number of times MarkDirty was called.</summary>
        public long DirtyMarkCount => Interlocked.Read(ref _dirtyMarkCount);
        
        /// <summary>Last time the layout was saved.</summary>
        public DateTime? LastSaveTime => _lastSaveTime;
        
        /// <summary>Last time a dirty snapshot was written.</summary>
        public DateTime? LastSnapshotTime => _lastSnapshotTime;
        
        /// <summary>Number of tools in the current working layout.</summary>
        public int ToolCount => _workingLayout?.Count ?? 0;
        
        /// <summary>
        /// Resets all statistics counters.
        /// </summary>
        public void ResetStatistics()
        {
            Interlocked.Exchange(ref _saveCount, 0);
            Interlocked.Exchange(ref _discardCount, 0);
            Interlocked.Exchange(ref _snapshotWriteCount, 0);
            Interlocked.Exchange(ref _snapshotSkippedCount, 0);
            Interlocked.Exchange(ref _dirtyMarkCount, 0);
            _lastSaveTime = null;
            _lastSnapshotTime = null;
        }
        
        /// <summary>
        /// Gets a summary of layout editing statistics.
        /// </summary>
        public LayoutEditingStatistics GetStatistics()
        {
            return new LayoutEditingStatistics
            {
                CurrentLayoutName = _currentLayoutName,
                CurrentLayoutType = _currentLayoutType,
                IsDirty = _isDirty,
                ToolCount = ToolCount,
                SaveCount = SaveCount,
                DiscardCount = DiscardCount,
                SnapshotWriteCount = SnapshotWriteCount,
                SnapshotSkippedCount = SnapshotSkippedCount,
                DirtyMarkCount = DirtyMarkCount,
                LastSaveTime = _lastSaveTime,
                LastSnapshotTime = _lastSnapshotTime,
                SnapshotSavingsPercent = SnapshotSkippedCount > 0 
                    ? (SnapshotSkippedCount - SnapshotWriteCount) * 100.0 / SnapshotSkippedCount 
                    : 0
            };
        }
        
        #endregion
        
        #region IDisposable
        
        /// <summary>
        /// Clears event handlers and flushes pending snapshots.
        /// </summary>
        public void Dispose()
        {
            // Flush any pending snapshot
            lock (_snapshotLock)
            {
                _snapshotDebounceTimer?.Stop();
                _snapshotDebounceTimer?.Dispose();
                _snapshotDebounceTimer = null;
                
                if (_isDirty)
                {
                    SaveDirtySnapshotInternal();
                }
            }
            
            OnDirtyStateChanged = null;
            OnShowUnsavedChangesDialog = null;
            OnLayoutReverted = null;
            _log.Debug("LayoutEditingService disposed");
        }
        
        #endregion
    }
    
/// <summary>
/// Statistics for layout editing operations.
/// </summary>
public record LayoutEditingStatistics
{
    public string CurrentLayoutName { get; init; } = string.Empty;
    public LayoutType CurrentLayoutType { get; init; }
    public bool IsDirty { get; init; }
    public int ToolCount { get; init; }
    public long SaveCount { get; init; }
    public long DiscardCount { get; init; }
    public long SnapshotWriteCount { get; init; }
    public long SnapshotSkippedCount { get; init; }
    public long DirtyMarkCount { get; init; }
    public DateTime? LastSaveTime { get; init; }
    public DateTime? LastSnapshotTime { get; init; }
    public double SnapshotSavingsPercent { get; init; }
}

/// <summary>
/// User's choice in the unsaved changes dialog.
/// </summary>
public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}
