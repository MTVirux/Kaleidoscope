using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Plugin.Services;
using Kaleidoscope.Gui.MainWindow;
using Newtonsoft.Json;

namespace Kaleidoscope.Services
{
    /// <summary>
    /// Represents a pending action that was blocked due to unsaved layout changes.
    /// The user must choose to Save, Discard, or Cancel before the action can proceed.
    /// </summary>
    public class PendingLayoutAction
    {
        /// <summary>A human-readable description of the action that was blocked.</summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>The action to perform after the user chooses Save or Discard.</summary>
        public Action? ContinueAction { get; set; }
        
        /// <summary>Optional action to perform if the user cancels.</summary>
        public Action? CancelAction { get; set; }
    }

    /// <summary>
    /// Manages layout editing with explicit file-editor semantics.
    /// 
    /// State model:
    /// - Persisted layout: saved to disk (layouts.json)
    /// - Working layout: in-memory, actively used
    /// - Dirty snapshot: temporary file for reload recovery
    /// 
    /// Rules:
    /// - User changes update working layout immediately and set IsDirty = true
    /// - Changes are applied and used instantly but NOT auto-persisted
    /// - Persisted layout updates ONLY on explicit Save action
    /// - After save, IsDirty = false and dirty snapshot is cleared
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
        
        // Pending action that was blocked due to unsaved changes
        private PendingLayoutAction? _pendingAction;
        
        // Flag to show unsaved changes dialog
        private bool _showUnsavedChangesDialog;
        
        /// <summary>
        /// Path to the dirty snapshot file for reload recovery.
        /// </summary>
        private string DirtySnapshotPath => Path.Combine(_filenameService.ConfigDirectory, "layout_dirty_snapshot.json");
        
        /// <summary>
        /// Raised when the dirty state changes.
        /// </summary>
        public event Action<bool>? OnDirtyStateChanged;
        
        /// <summary>
        /// Raised when the unsaved changes dialog should be shown.
        /// </summary>
        public event Action? OnShowUnsavedChangesDialog;
        
        /// <summary>
        /// True if the working layout has unsaved changes.
        /// </summary>
        public bool IsDirty => _isDirty;
        
        /// <summary>
        /// True if the unsaved changes dialog should be shown.
        /// </summary>
        public bool ShowUnsavedChangesDialog => _showUnsavedChangesDialog;
        
        /// <summary>
        /// The pending action that was blocked, if any.
        /// </summary>
        public PendingLayoutAction? PendingAction => _pendingAction;
        
        /// <summary>
        /// The name of the currently active layout.
        /// </summary>
        public string CurrentLayoutName => _currentLayoutName;
        
        /// <summary>
        /// The type of the currently active layout.
        /// </summary>
        public LayoutType CurrentLayoutType => _currentLayoutType;
        
        /// <summary>
        /// The current working layout (may differ from persisted if dirty).
        /// </summary>
        public List<ToolLayoutState>? WorkingLayout => _workingLayout;
        
        /// <summary>
        /// The current working grid settings (may differ from persisted if dirty).
        /// </summary>
        public LayoutGridSettings? WorkingGridSettings => _workingGridSettings;

        public LayoutEditingService(IPluginLog log, ConfigurationService configService, FilenameService filenameService)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _filenameService = filenameService ?? throw new ArgumentNullException(nameof(filenameService));
            
            // Check for dirty snapshot on startup and restore if present
            TryRestoreDirtySnapshot();
            
            _log.Debug("LayoutEditingService initialized");
        }
        
        /// <summary>
        /// Initializes the working layout from the currently active persisted layout.
        /// Call this after the layout has been loaded from config.
        /// </summary>
        public void InitializeFromPersisted(string layoutName, LayoutType layoutType, List<ToolLayoutState>? tools, LayoutGridSettings? gridSettings)
        {
            // If we already have a dirty state from a snapshot restore, don't overwrite it
            if (_isDirty && _workingLayout != null)
            {
                _log.Debug($"LayoutEditingService: Keeping restored dirty state for '{_currentLayoutName}'");
                return;
            }
            
            _currentLayoutName = layoutName;
            _currentLayoutType = layoutType;
            _workingLayout = tools != null ? CloneToolList(tools) : new List<ToolLayoutState>();
            _workingGridSettings = gridSettings?.Clone();
            _isDirty = false;
            
            _log.Debug($"LayoutEditingService: Initialized from persisted layout '{layoutName}' ({_workingLayout.Count} tools)");
        }
        
        /// <summary>
        /// Marks the working layout as changed. Call this when the user modifies the layout.
        /// </summary>
        public void MarkDirty(List<ToolLayoutState>? currentTools, LayoutGridSettings? currentGridSettings)
        {
            _workingLayout = currentTools != null ? CloneToolList(currentTools) : _workingLayout;
            _workingGridSettings = currentGridSettings?.Clone() ?? _workingGridSettings;
            
            if (!_isDirty)
            {
                _isDirty = true;
                _log.Debug($"LayoutEditingService: Layout marked dirty for '{_currentLayoutName}'");
                OnDirtyStateChanged?.Invoke(true);
            }
            
            // Persist dirty snapshot for reload recovery
            SaveDirtySnapshot();
        }
        
        /// <summary>
        /// Updates the working layout without marking dirty (used for internal sync).
        /// </summary>
        public void UpdateWorkingLayout(List<ToolLayoutState>? tools, LayoutGridSettings? gridSettings)
        {
            _workingLayout = tools != null ? CloneToolList(tools) : _workingLayout;
            _workingGridSettings = gridSettings?.Clone() ?? _workingGridSettings;
            
            if (_isDirty)
            {
                SaveDirtySnapshot();
            }
        }
        
        /// <summary>
        /// Saves the current working layout to the persisted storage.
        /// Clears the dirty state and removes the dirty snapshot.
        /// </summary>
        public void Save()
        {
            if (!_isDirty)
            {
                _log.Debug("LayoutEditingService: Save called but layout is not dirty");
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
}
