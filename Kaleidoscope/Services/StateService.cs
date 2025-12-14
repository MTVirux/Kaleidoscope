using System;
using Dalamud.Plugin.Services;
using Kaleidoscope.Interfaces;

namespace Kaleidoscope.Services
{
    /// <summary>
    /// Centralized service for tracking UI mode states across the plugin.
    /// Provides a single source of truth for fullscreen, edit, locked, and drag states.
    /// </summary>
    public class StateService : IStateService
    {
        private readonly IPluginLog _log;
        private readonly ConfigurationService _configService;

        private bool _isFullscreen;
        private bool _isEditMode;
        private bool _isLocked;
        private bool _isDragging;
        private bool _isResizing;
        private bool _isMainWindowMoving;
        private bool _isMainWindowResizing;

        public StateService(IPluginLog log, ConfigurationService configService)
        {
            _log = log;
            _configService = configService;

            // Initialize from configuration
            _isEditMode = configService.Config.EditMode;
            _isLocked = configService.Config.PinMainWindow;
            _isFullscreen = configService.Config.ExclusiveFullscreen;

            _log.Debug("StateService initialized");
        }

        private Configuration Config => _configService.Config;

        /// <inheritdoc />
        public bool IsFullscreen
        {
            get => _isFullscreen;
            set
            {
                if (_isFullscreen == value) return;
                _isFullscreen = value;
                _log.Debug($"StateService: IsFullscreen changed to {value}");
                OnFullscreenChanged?.Invoke(value);
            }
        }

        /// <inheritdoc />
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode == value) return;
                _isEditMode = value;
                Config.EditMode = value;
                _configService.Save();
                _log.Debug($"StateService: IsEditMode changed to {value}");
                OnEditModeChanged?.Invoke(value);
            }
        }

        /// <inheritdoc />
        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value) return;
                _isLocked = value;
                Config.PinMainWindow = value;
                _configService.Save();
                _log.Debug($"StateService: IsLocked changed to {value}");
                OnLockedChanged?.Invoke(value);
            }
        }

        /// <inheritdoc />
        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging == value) return;
                _isDragging = value;
                _log.Verbose($"StateService: IsDragging changed to {value}");
                OnDraggingChanged?.Invoke(value);
            }
        }

        /// <inheritdoc />
        public bool IsResizing
        {
            get => _isResizing;
            set
            {
                if (_isResizing == value) return;
                _isResizing = value;
                _log.Verbose($"StateService: IsResizing changed to {value}");
                OnResizingChanged?.Invoke(value);
            }
        }

        /// <inheritdoc />
        public bool IsMainWindowMoving
        {
            get => _isMainWindowMoving;
            set
            {
                if (_isMainWindowMoving == value) return;
                _isMainWindowMoving = value;
                _log.Verbose($"StateService: IsMainWindowMoving changed to {value}");
            }
        }

        /// <inheritdoc />
        public bool IsMainWindowResizing
        {
            get => _isMainWindowResizing;
            set
            {
                if (_isMainWindowResizing == value) return;
                _isMainWindowResizing = value;
                _log.Verbose($"StateService: IsMainWindowResizing changed to {value}");
            }
        }

        /// <inheritdoc />
        public bool IsMainWindowInteracting => _isMainWindowMoving || _isMainWindowResizing;

        /// <inheritdoc />
        public bool IsInteracting => _isDragging || _isResizing;

        /// <inheritdoc />
        public bool CanEditLayout => _isEditMode;

        /// <inheritdoc />
        public event Action<bool>? OnFullscreenChanged;

        /// <inheritdoc />
        public event Action<bool>? OnEditModeChanged;

        /// <inheritdoc />
        public event Action<bool>? OnLockedChanged;

        /// <inheritdoc />
        public event Action<bool>? OnDraggingChanged;

        /// <inheritdoc />
        public event Action<bool>? OnResizingChanged;

        /// <inheritdoc />
        public void ToggleEditMode()
        {
            var newState = !IsEditMode;
            IsEditMode = newState;
            _log.Debug($"StateService: Edit mode toggled to {newState}");
        }

        /// <inheritdoc />
        public void ToggleLocked()
        {
            IsLocked = !IsLocked;
            _log.Debug($"StateService: Locked toggled to {IsLocked}");
        }

        /// <inheritdoc />
        public void EnterFullscreen()
        {
            if (_isFullscreen) return;
            IsFullscreen = true;
            _log.Debug("StateService: Entered fullscreen");
        }

        /// <inheritdoc />
        public void ExitFullscreen()
        {
            if (!_isFullscreen) return;
            IsFullscreen = false;
            _log.Debug("StateService: Exited fullscreen");
        }
    }
}
