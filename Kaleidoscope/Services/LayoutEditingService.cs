using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.MainWindow;
using Newtonsoft.Json;

namespace Kaleidoscope.Services;

/// <summary>
/// Pending action blocked due to unsaved layout changes.
/// </summary>
public class PendingLayoutAction
{
    public string Description { get; set; } = string.Empty;
    public Action? ContinueAction { get; set; }
    public Action? CancelAction { get; set; }
}

/// <summary>
/// Manages layout editing with explicit save semantics (like a file editor).
/// Changes are applied immediately but only persisted on explicit Save.
/// </summary>
public class LayoutEditingService
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

    private string DirtySnapshotPath => Path.Combine(_filenameService.ConfigDirectory, "layout_dirty_snapshot.json");

    public event Action<bool>? OnDirtyStateChanged;
    public event Action? OnShowUnsavedChangesDialog;

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

        TryRestoreDirtySnapshot();
        _log.Debug("LayoutEditingService initialized");
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
    /// </summary>
    public void MarkDirty(List<ToolLayoutState>? currentTools, LayoutGridSettings? currentGridSettings)
    {
        _workingLayout = currentTools != null ? CloneToolList(currentTools) : _workingLayout;
        _workingGridSettings = currentGridSettings?.Clone() ?? _workingGridSettings;

        if (!_isDirty)
        {
            _isDirty = true;
            _log.Debug($"Layout marked dirty for '{_currentLayoutName}'");
            OnDirtyStateChanged?.Invoke(true);
        }

        SaveDirtySnapshot();
    }

    /// <summary>
    /// Updates the working layout without marking dirty.
    /// </summary>
    public void UpdateWorkingLayout(List<ToolLayoutState>? tools, LayoutGridSettings? gridSettings)
    {
        _workingLayout = tools != null ? CloneToolList(tools) : _workingLayout;
        _workingGridSettings = gridSettings?.Clone() ?? _workingGridSettings;

        if (_isDirty)
            SaveDirtySnapshot();
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
            }
            catch (Exception ex)
            {
                _log.Error($"LayoutEditingService: Failed to discard changes: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Attempts to perform an action that would discard unsaved changes.
        /// If dirty, blocks the action and shows the unsaved changes dialog.
        /// Returns true if the action can proceed immediately (not dirty).
        /// </summary>
        public bool TryPerformDestructiveAction(string description, Action continueAction, Action? cancelAction = null)
        {
            if (!_isDirty)
            {
                // Not dirty, action can proceed immediately
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
        
        private void SaveDirtySnapshot()
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
