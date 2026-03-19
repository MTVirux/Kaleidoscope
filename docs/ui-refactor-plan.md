# UI Refactor Plan — Kaleidoscope

> **Status**: Phase 1 Complete  
> **Created**: 2026-03-18  
> **Last Updated**: 2026-03-18  
> **Scope**: Structural overhaul — phased, with stable checkpoints  

---

## Table of Contents

- [Executive Summary](#executive-summary)
- [Current Architecture Analysis](#current-architecture-analysis)
  - [Component Map](#component-map)
  - [Data Flow](#data-flow)
  - [Identified Pain Points](#identified-pain-points)
- [Design Decisions](#design-decisions)
- [Phase 1 — Decompose WindowContentContainer](#phase-1--decompose-windowcontentcontainer)
- [Phase 2 — Tool-Level Dependency Injection](#phase-2--tool-level-dependency-injection)
- [Phase 3 — Animation Framework](#phase-3--animation-framework)
- [Phase 4 — Rendering Performance](#phase-4--rendering-performance)
- [Phase 5 — Hybrid Layout System](#phase-5--hybrid-layout-system)
- [Phase 6 — Final Polish & Cleanup](#phase-6--final-polish--cleanup)
- [Appendix A — File Reference Index](#appendix-a--file-reference-index)
- [Appendix B — External Patterns Considered](#appendix-b--external-patterns-considered)
- [Progress Tracker](#progress-tracker)

---

## Executive Summary

Refactor Kaleidoscope's UI layer into modular, high-performance subsystems. The current `WindowContentContainer` god object (2,294 lines across 4 partial files) will be decomposed into focused classes. Tools will gain full dependency injection (replacing the 18-parameter `ToolCreationContext` manual factory). A shared animation framework will power tool transitions. Occlusion culling, lazy widget creation, and hot-path allocation elimination will improve rendering. The layout system extends to support auto-layout presets alongside the existing grid-snap editor.

Each phase yields a working, shippable plugin. No behavior changes in Phase 1; additive features in Phases 3–5.

---

## Current Architecture Analysis

### Component Map

```
WindowService (IRequiredService)
  ├── MainWindow (Dalamud Window, 1304 lines)
  │     ├── WindowContentContainer (partial class, 4 files, 2294 lines)
  │     │     ├── .cs             — core fields, 20 callback fields, tool registry, tool lifecycle (493 lines)
  │     │     ├── .Drawing.cs     — grid, tools, menus, drag/resize, ALL rendering (976 lines)
  │     │     ├── .Layout.cs      — export/import/apply with 3-tier matching (324 lines)
  │     │     └── .Dialogs.cs     — 5 modals: rename, preset, grid, settings, unsaved (501 lines)
  │     ├── LayoutEditingService (776 lines)
  │     │     └── Dirty tracking, snapshots, auto-save, layout switching
  │     └── WindowToolRegistrar (static, 629 lines)
  │           └── 20 factory methods, ToolIds, CreateToolFromId
  ├── ConfigWindow (14 tab categories)
  └── QuickAccessBarWidget (CTRL+ALT toolbar)

ToolComponent (abstract base, 418 lines)
  └── 20 tool types across 7 categories
       └── Each owns widgets: GraphWidget, ItemTableWidget, ComboWidget, etc.
```

### Data Flow

```
Background Threads (Universalis, Currency)
  │  Channel<T> → DB writes
  ▼
Cache Services (version counters: long Version, atomically incremented)
  │  Events: OnValuesChanged, OnPriceUpdated, OnCacheUpdated
  ▼
Tools (compare _last*Version each frame → refresh if changed)
  │  RenderToolContent() each frame
  ▼
Widgets (data pushed imperatively: graph.Render(series), table.Draw(rows))
  │  No data binding — pure push model
  ▼
ImGui (direct calls — ImGui.Begin/End, ImPlot.BeginPlot, etc.)
```

### Identified Pain Points

#### P1: WindowContentContainer Is a God Object

**4 partial files, 2,294 lines**, handling:
- Tool lifecycle (add, remove, duplicate, clear)
- Grid rendering + grid dimension calculations
- Tool rendering (background, header, content, outline)
- Drag & resize interaction (hit detection, mouse tracking, grid snapping)
- Context menus (content-area + per-tool, nested category tree)
- 5 modal dialogs (rename, preset, grid, settings, unsaved changes)
- Layout export/import/apply with 3-tier fallback matching
- 20 callback fields for MainWindow communication

#### P2: Callback Soup (20 Callback Fields)

`WindowContentContainer` communicates with `MainWindow` / `LayoutEditingService` through **20 `Action`/`Func` callback fields**, all wired in `MainWindow.WireLayoutCallbacks()` (lines 455–601, 146 lines of lambda assignments):

| Field | Type | Location |
|-------|------|----------|
| `OnSaveLayout` | `Action<string, List<ToolLayoutState>>?` | `WindowContentContainer.cs` L88 |
| `OnLoadLayout` | `Action<string>?` | L89 |
| `GetAvailableLayoutNames` | `Func<List<string>>?` | L90 |
| `OnLayoutChanged` | `Action<List<ToolLayoutState>>?` | L97 |
| `OnSaveLayoutExplicit` | `Action?` | L100 |
| `OnDiscardChanges` | `Action?` | L103 |
| `GetIsDirty` | `Func<bool>?` | L106 |
| `GetCurrentLayoutName` | `Func<string>?` | L109 |
| `OnManageLayouts` | `Action?` | L112 |
| `TryPerformDestructiveAction` | `Func<string, Action, bool>?` | L116 |
| `GetShowUnsavedChangesDialog` | `Func<bool>?` | L119 |
| `GetPendingActionDescription` | `Func<string>?` | L120 |
| `HandleUnsavedChangesChoice` | `Action<UnsavedChangesChoice>?` | L121 |
| `OnSavePreset` | `Action<string, string, Dictionary<string, object?>>?` | L125 |
| `OnDraggingChanged` | `Action<bool>?` | L135 |
| `OnResizingChanged` | `Action<bool>?` | L136 |
| `IsMainWindowInteracting` | `Func<bool>?` | L140 |
| `IsFullscreenMode` | `Func<bool>?` | L144 |
| `GetExternalToolInternalPadding` | `Func<int>?` | L288 |
| `OnGridSettingsChanged` | `Action<LayoutGridSettings>?` | L407 |

**Problem**: No type safety that all callbacks are wired. Null callbacks silently do nothing. Adding any capability requires touching 3 files.

#### P3: Manual Tool Factories Bypass DI

`ToolCreationContext` (`ToolCreationContext.cs` L14–35) bundles **18 service parameters**:

| Property | Type |
|----------|------|
| `FilenameService` | `FilenameService` |
| `CurrencyTrackerService` | `CurrencyTrackerService` |
| `ConfigService` | `ConfigurationService` |
| `CharacterDataService` | `CharacterDataService?` |
| `InventoryChangeService` | `InventoryChangeService?` |
| `Registry` | `TrackedDataRegistry?` |
| `WebSocketService` | `UniversalisWebSocketService?` |
| `PriceTrackingService` | `PriceTrackingService?` |
| `ItemDataService` | `ItemDataService?` |
| `DataManager` | `IDataManager?` |
| `InventoryCacheService` | `InventoryCacheService?` |
| `AutoRetainerIpc` | `AutoRetainerService?` |
| `TextureProvider` | `ITextureProvider?` |
| `FavoritesService` | `FavoritesService?` |
| `SalePriceCacheService` | `SalePriceCacheService?` |
| `FFXIVMTService` | `FFXIVMTService?` |
| `LifestreamService` | `LifestreamService?` |
| `NotificationManager` | `INotificationManager?` |

`WindowToolRegistrar` (629 lines) has 14 private factory methods that manually `new` up tool instances from this context. Adding a service dependency to any tool requires modifying: the tool constructor, the factory method, and potentially `ToolCreationContext`.

#### P4: Reflection in Settings Hot Path

`ToolComponent.DrawSettingsFromSchema()` (L158–178) calls `MakeGenericMethod()` every frame when settings panel is open:

```csharp
// L172 — uncached reflection call
var drawMethod = typeof(SettingsSchemaRenderer)
    .GetMethod(nameof(SettingsSchemaRenderer.Draw))
    ?.MakeGenericMethod(settingsType);
```

#### P5: JSON Deep Clone on Every Dirty Mark

`LayoutEditingService.CloneToolList()` (L582–587) serializes entire tool list to JSON and back:

```csharp
private static List<ToolLayoutState> CloneToolList(List<ToolLayoutState> source)
{
    var json = JsonConvert.SerializeObject(source);
    return JsonConvert.DeserializeObject<List<ToolLayoutState>>(json) ?? new List<ToolLayoutState>();
}
```

Called from 6 locations (L166, L181, L216, L248, L307, L499) including `MarkDirty()` which fires on every drag movement.

#### P6: Hot-Path LINQ Allocation in Graph

`Graph.cs` L169 — convenience overload allocates `List` per frame:

```csharp
public void RenderMultipleSeries(... series)
    => RenderMultipleSeries(series.Select(s => (s.name, s.samples, (Vector4?)null)).ToList());
```

#### P7: No Occlusion Culling or Frame Skipping

All visible tools call `RenderToolContent()` every frame regardless of:
- Whether they're occluded by other tools
- Whether they're off-screen (scrolled out of view)
- Whether their data has changed since last frame

#### P8: No Animation System

Only one bespoke animation exists: `QuickAccessBarWidget` (L50, L65–70, L118–141) with hand-rolled linear interpolation. No shared easing functions, tween library, or animation controller.

#### P9: LayoutEditingService Concurrency Issues

- `volatile bool _isDirty` mixed with `lock _snapshotLock` — two different synchronization primitives for shared state
- Timer callbacks (`OnSnapshotDebounceElapsed`, `OnAutoSaveDebounceElapsed`) fire on thread-pool threads, touching `_workingLayout`
- `CloneToolList()` via JSON on every dirty mark compounds the issue

#### P10: Dead Code

`ContentComponentState` (Configuration.Layout.cs L39–45) appears unused — superseded by `ToolLayoutState`.

---

## Design Decisions

| # | Decision | Chose | Over | Rationale |
|---|----------|-------|------|-----------|
| D1 | Event system | Keep `Action<T>` events | Mediator/message bus | Existing pattern works; InventoryTools' mediator adds complexity without proportional benefit for our event density |
| D2 | Tool DI | `ActivatorUtilities.CreateInstance<T>` via `ToolFactory` | Keep manual factories | Leverages existing `ServiceManager` singleton infrastructure; `OtterGui.ServiceManager` wraps `Microsoft.Extensions.DI` so `ActivatorUtilities` works out of the box |
| D3 | Deep clone | Explicit `Clone()` methods on models | JSON round-trip | Avoids serialization overhead on every dirty mark during drag (called 6 times per operation) |
| D4 | Callback replacement | `ILayoutHost` interface on `MainWindow` | Keep callback fields | Type-safe contract; compile-time guarantee all methods are implemented; single parameter vs 20 field assignments |
| D5 | Layout system | Hybrid: grid-snap + auto-layout presets | Grid only / constraint-based only | Auto-layout presets are starting points, not constraints; user can always switch to manual grid editing afterward |
| D6 | Animation | Full suite: fade, slide, drag ghost, resize interpolation, hover | Simple fade only | "Slicker" UI goal demands spatial animations; ~0.12–0.2s durations keep things snappy (Dalamud's own window fade is 0.072s) |
| D7 | Execution | Phased with stable checkpoints | Monolithic | Each phase is independently testable and shippable; can pause between phases if needed |
| D8 | JSON serializer | Standardize on System.Text.Json | Keep dual Newtonsoft + STJ | .NET 10 target; STJ is faster and built-in; one-time migration for legacy layouts |

---

## Phase 1 — Decompose WindowContentContainer

> **Goal**: Break the 2,294-line god object into single-responsibility classes. Pure refactor — no behavior changes.  
> **Risk**: Medium (touch many interaction paths)  
> **Estimated effort**: 3–5 days  

### Step 1.1: Define `ILayoutHost` Interface

**New file**: `Kaleidoscope/Interfaces/ILayoutHost.cs`

Replace the 20 callback fields with a typed interface:

```csharp
public interface ILayoutHost
{
    // Layout persistence
    void SaveLayout(string name, List<ToolLayoutState> tools);
    void LoadLayout(string name);
    List<string> GetAvailableLayoutNames();
    void SaveLayoutExplicit();
    void DiscardChanges();
    
    // Layout state queries
    bool IsDirty { get; }
    string CurrentLayoutName { get; }
    bool IsFullscreenMode { get; }
    bool IsMainWindowInteracting { get; }
    int ExternalToolInternalPadding { get; }
    
    // Dirty tracking
    void NotifyLayoutChanged(List<ToolLayoutState> tools);
    void NotifyGridSettingsChanged(LayoutGridSettings gridSettings);
    
    // Destructive action guard
    bool TryPerformDestructiveAction(string description, Action continueAction);
    
    // Unsaved changes dialog
    bool ShowUnsavedChangesDialog { get; }
    string PendingActionDescription { get; }
    void HandleUnsavedChangesChoice(UnsavedChangesChoice choice);
    
    // Presets
    void SavePreset(string name, string description, Dictionary<string, object?> settings);
    
    // Navigation
    void OpenManageLayouts();
    
    // Interaction state
    void NotifyDraggingChanged(bool isDragging);
    void NotifyResizingChanged(bool isResizing);
}
```

**Affected files**:
- `WindowContentContainer.cs` L86–144, L288, L407 — remove all callback fields
- `MainWindow.cs` L455–601 — delete `WireLayoutCallbacks()`, implement `ILayoutHost` directly
- `WindowContentContainer.cs` constructor — accept `ILayoutHost` parameter

### Step 1.2: Extract `ToolInteractionManager`

**New file**: `Kaleidoscope/Gui/MainWindow/ToolInteractionManager.cs`

**Move from** `WindowContentContainer.Drawing.cs`:
- Drag state: `_isDragging`, `_draggedToolIndex`, `_dragOffset`, title bar hit zone detection
- Resize state: `_isResizing`, `_resizedToolIndex`, `_resizeStartPos`, `_resizeStartSize`, 12px corner handle detection
- Mouse tracking during interaction (free movement)
- Grid snapping on mouse release (nearest subdivision point)
- Content region clamping
- `StateService.IsDragging`/`IsResizing` updates

**API**:
```csharp
public sealed class ToolInteractionManager
{
    public ToolInteractionManager(StateService stateService) { }
    
    // Called once per frame during edit mode
    public void ProcessFrame(IReadOnlyList<ToolComponent> tools, Vector2 contentMin, 
                             Vector2 contentSize, int columns, int rows, int subdivisions);
    
    // State queries
    public bool IsDragging { get; }
    public bool IsResizing { get; }
    public int ActiveToolIndex { get; } // -1 if none
    public Vector2 SnapTargetPosition { get; } // For animation ghost
    public Vector2 SnapTargetSize { get; }     // For animation ghost
}
```

### Step 1.3: Extract `ToolRenderer`

**New file**: `Kaleidoscope/Gui/MainWindow/ToolRenderer.cs`

**Move from** `WindowContentContainer.Drawing.cs`:
- Single-tool rendering: background rect, `BeginChild` with padding, header bar (title text + settings icon), `tool.RenderToolContent()`, outline, `EndChild`
- Tool visibility check
- Background color/style application

**API**:
```csharp
public sealed class ToolRenderer
{
    public void DrawTool(ToolComponent tool, int index, Vector2 contentMin, 
                         Vector2 contentSize, bool editMode, bool isInteracting,
                         float animationAlpha, int paddingPx);
}
```

### Step 1.4: Extract `GridRenderer`

**New file**: `Kaleidoscope/Gui/MainWindow/GridRenderer.cs`

**Move from** `WindowContentContainer.Drawing.cs`:
- Grid overlay rendering (edit mode only)
- `MaxGridLines` cap logic
- Grid dimension calculation (`GetEffectiveColumns()`/`GetEffectiveRows()`)

**API**:
```csharp
public sealed class GridRenderer
{
    public void Draw(Vector2 contentMin, Vector2 contentSize, 
                     int columns, int rows, int subdivisions);
    
    public static (int columns, int rows) GetEffectiveDimensions(
        LayoutGridSettings settings, float aspectWidth = 16f, float aspectHeight = 9f);
}
```

### Step 1.5: Extract `ContextMenuManager`

**New file**: `Kaleidoscope/Gui/MainWindow/ContextMenuManager.cs`

**Move from** `WindowContentContainer.Drawing.cs`:
- Content-area right-click menu (add tool, nested category tree from `_toolTypesByCategory`)
- Per-tool right-click menu (appearance, settings, duplicate, remove, presets)
- `_pendingPopup` deferred opening pattern

**API**:
```csharp
public sealed class ContextMenuManager
{
    public ContextMenuManager(ILayoutHost layoutHost) { }
    
    public void ProcessRightClick(Vector2 mousePos, IReadOnlyList<ToolComponent> tools,
                                   IReadOnlyDictionary<string, List<ToolDefinition>> toolTypesByCategory);
    
    public void Draw(); // Renders popup menus
    
    // Signals for container to act on
    public ToolComponent? PendingAddTool { get; }
    public (int index, string action)? PendingToolAction { get; }
}
```

### Step 1.6: Extract `DialogManager`

**New file**: `Kaleidoscope/Gui/MainWindow/DialogManager.cs`

**Move from** `WindowContentContainer.Dialogs.cs` (501 lines → own class):
- `DrawToolRenameModal()`  
- `DrawSavePresetModal()`  
- `DrawToolSettingsWindow()` (separate ImGui window for tool settings)  
- `DrawGridResolutionModal()`  
- `DrawUnsavedChangesDialog()`

All modal state (`_renameTarget`, `_presetName`, `_showSettingsForTool`, etc.) moves here.

**API**:
```csharp
public sealed class DialogManager
{
    public DialogManager(ILayoutHost layoutHost) { }
    
    public void RequestRename(ToolComponent tool);
    public void RequestSavePreset(ToolComponent tool);
    public void RequestShowSettings(ToolComponent tool);
    public void RequestGridResolution(LayoutGridSettings currentSettings);
    
    public void Draw(); // Renders all active modals
    
    // Signals for container
    public LayoutGridSettings? PendingGridSettings { get; }
}
```

### Step 1.7: Slim `WindowContentContainer`

After extraction, `WindowContentContainer.cs` reduces to ~200 lines:

```csharp
public sealed class WindowContentContainer
{
    private readonly ILayoutHost _layoutHost;
    private readonly ToolInteractionManager _interactionManager;
    private readonly ToolRenderer _toolRenderer;
    private readonly GridRenderer _gridRenderer;
    private readonly ContextMenuManager _contextMenuManager;
    private readonly DialogManager _dialogManager;
    
    private readonly List<ToolComponent> _tools = new();
    private readonly Dictionary<string, List<ToolDefinition>> _toolTypesByCategory = new();
    
    // Tool lifecycle: AddTool, RemoveTool, DuplicateTool, ClearAllTools
    // Grid settings: UpdateGridSettings, SetGridSettingsFromLayout
    // Layout: ExportLayout, ApplyLayout
    
    public void Draw(bool editMode)
    {
        // Orchestrate:
        _gridRenderer.Draw(...);
        foreach (var tool in _tools) _toolRenderer.DrawTool(tool, ...);
        _interactionManager.ProcessFrame(_tools, ...);
        _contextMenuManager.Draw();
        _dialogManager.Draw();
    }
}
```

### Step 1.8: Update `MainWindow`

- Implement `ILayoutHost` directly (methods delegate to `LayoutEditingService` + `ConfigService`)
- Delete `WireLayoutCallbacks()` (L455–601)
- Pass `this` as `ILayoutHost` to `WindowContentContainer` constructor
- Reduces `MainWindow` from ~1,304 lines by ~146 lines

### Verification Checklist — Phase 1

- [ ] Plugin loads without errors
- [ ] All 20 tool types render identically
- [ ] Edit mode: drag tools, resize tools, grid snapping works
- [ ] Content-area right-click: all tool categories appear, tools can be added
- [ ] Per-tool right-click: appearance, settings, duplicate, remove, presets all work
- [ ] All 5 modals open and close correctly (rename, preset, grid, settings, unsaved)
- [ ] Layout save/load round-trips (save layout, load it, verify tools restored)
- [ ] Dirty tracking: asterisk appears on title, unsaved changes dialog gates destructive actions
- [ ] Fullscreen ↔ windowed transitions work with layout switching
- [ ] No compilation warnings in affected files

---

## Phase 2 — Tool-Level Dependency Injection

> **Goal**: Tools declare their own constructor dependencies. Kill `ToolCreationContext` and manual factory methods.  
> **Risk**: Medium (touches all 20 tool types)  
> **Estimated effort**: 3–4 days  

### Step 2.1: Create `ToolTypeAttribute`

**New file**: `Kaleidoscope/Gui/MainWindow/ToolTypeAttribute.cs`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ToolTypeAttribute : Attribute
{
    public string Id { get; }
    public string Label { get; }
    public string Category { get; }
    public string Description { get; }
    
    public ToolTypeAttribute(string id, string label, string category, string description = "") { ... }
}
```

Tool IDs come from `WindowToolRegistrar.ToolIds` (L26–44):

| ID | Label | Category |
|----|-------|----------|
| `"GettingStarted"` | Getting Started | Help |
| `"ImPlotReference"` | ImPlot Reference | Help |
| `"WebsocketFeed"` | Websocket Feed | PriceTracking |
| `"TopInventoryValueItems"` | Top Inventory Value | PriceTracking |
| `"ItemSalesHistory"` | Item Sales History | PriceTracking |
| `"ItemSalesTracking"` | Item Sales Tracking | PriceTracking |
| `"DataGraph"` | Data (Graph) | Data |
| `"DataTable"` | Data (Table) | Data |
| `"Label"` | Label | Label |
| `"UniversalisWebSocketStatus"` | WebSocket Status | Status |
| `"AutoRetainerStatus"` | AutoRetainer Status | Status |
| `"AutoRetainerControl"` | AutoRetainer Control | AutoRetainer |
| `"UniversalisApiStatus"` | API Status | Status |
| `"DatabaseSize"` | Database Size | Status |
| `"CacheSize"` | Cache Size | Status |
| `"RetainerVentureStatus"` | Retainer Ventures | AutoRetainer |
| `"SubmersibleVentureStatus"` | Submersible Ventures | AutoRetainer |
| `"Fps"` | FPS | Status |
| `"GilFlux"` | Gil Flux | FFXIVMT |

### Step 2.2: Create `ToolFactory` Service

**New file**: `Kaleidoscope/Services/ToolFactory.cs`

```csharp
public sealed class ToolFactory : IService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, ToolDefinition> _toolDefinitions = new();
    
    public ToolFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        DiscoverToolTypes();
    }
    
    private void DiscoverToolTypes()
    {
        // Scan assembly for [ToolType] attributes
        // Build _toolDefinitions dictionary
    }
    
    public ToolComponent Create(string toolId)
    {
        var def = _toolDefinitions[toolId];
        var tool = (ToolComponent)ActivatorUtilities.CreateInstance(_serviceProvider, def.Type);
        ApplyDefaultColors(tool);
        return tool;
    }
    
    public IReadOnlyDictionary<string, List<ToolDefinition>> GetToolsByCategory() { ... }
    public IReadOnlyList<ToolDefinition> GetAllTools() { ... }
}
```

**DI note**: `OtterGui.ServiceManager` wraps `Microsoft.Extensions.DependencyInjection`. `ServiceManager.CreateProvider()` returns a standard `ServiceProvider`. `ActivatorUtilities.CreateInstance<T>(IServiceProvider)` resolves constructor parameters from the container, allowing tools to declare whatever services they need.

### Step 2.3: Add `[ToolType]` to All Tool Classes

Migrate each tool class — add attribute, update constructor to accept services directly. **Migration order** (simple → complex):

1. `LabelTool` — no service dependencies
2. `FpsTool` — no service dependencies  
3. `CacheSizeTool`, `DatabaseSizeTool` — single service each
4. `GettingStartedTool`, `ImPlotReferenceTool` — no dependencies
5. `UniversalisWebSocketStatusTool`, `UniversalisApiStatusTool`, `AutoRetainerStatusTool`
6. `RetainerVentureStatusTool`, `SubmersibleVentureStatusTool`
7. `WebsocketFeedTool`
8. `GilFluxTool` (conditional on `FFXIVMTService`)
9. `AutoRetainerControlTool`
10. `ItemSalesHistoryTool`, `ItemSalesTrackingTool`
11. `TopInventoryValueTool`
12. `DataTool` (most complex — 5 partial files, many dependencies)

### Step 2.4: Refactor `WindowToolRegistrar`

- Delete all 14 private factory methods
- Delete `ToolCreationContext` usage
- `RegisterTools()` calls `ToolFactory.GetToolsByCategory()` and registers with `WindowContentContainer`
- `CreateToolFromId()` delegates to `ToolFactory.Create(id)`
- Reduces from 629 lines to ~50 lines

### Step 2.5: Delete `ToolCreationContext`

Delete `Kaleidoscope/Gui/MainWindow/ToolCreationContext.cs` entirely.

### Verification Checklist — Phase 2

- [x] All 19 tool registrations (18 classes, DataTool×2 variants) instantiate via `ToolFactory.Create(id)`
- [x] Layout restore works — `ToolFactory.FindDefinitionByTypeName()` for type-based lookup, legacy brute-force probe kept as fallback
- [x] Tool settings import/export round-trips correctly
- [x] Adding a tool via context menu creates the correct type
- [x] GilFlux conditional registration still works (`RequiredServices = [typeof(FFXIVMTService)]`)
- [x] Build succeeds with 0 errors, 0 warnings
- [x] Adding a new tool requires ONLY: create class with `[ToolType]` + `ToolComponent` base + constructor injection

### Implementation Notes — Phase 2

- `ToolTypeAttribute` uses `AllowMultiple = true` (DataTool has two registrations: Graph + Table variants)
- `ToolDefinition` record is defined inside `ToolFactory.cs` rather than a separate file
- `ToolFactory` lives in `Kaleidoscope/Services/` (as planned), uses `ActivatorUtilities.CreateInstance`
- Variant actions dictionary handles DataTool's `ViewMode` toggle after construction
- Legacy ID mapping (`TopItems` → `TopInventoryValueItems`) preserved for old layouts
- `WindowToolRegistrar` reduced from 629 → ~80 lines (delegating to ToolFactory)
- `MainWindow` constructor params reduced from 24 → 12
- `ToolCreationContext` (19-field record) deleted entirely

---

## Phase 3 — Animation Framework

> **Goal**: Shared animation primitives and tool-level transitions for a polished UI.  
> **Risk**: Low (additive, no existing behavior changes)  
> **Estimated effort**: 4–5 days  

### Step 3.1: Create Animation Primitives

**New directory**: `Kaleidoscope/Gui/Animation/`

**`Easing.cs`** — Static easing functions (`float t` → `float`):
- `Linear` — identity
- `QuadIn` — t²
- `QuadOut` — 1 - (1-t)²
- `QuadInOut` — smooth acceleration/deceleration
- `CubicIn` — t³
- `CubicOut` — 1 - (1-t)³
- `CubicInOut`
- `SmoothStep` — 3t² - 2t³
- `Spring` — overshoot with damped oscillation

**`Tween.cs`** — Single-value tween:
```csharp
public sealed class Tween
{
    public float From, To, Duration;
    public Func<float, float> Easing;
    public float Elapsed;
    public bool IsPlaying => Elapsed < Duration;
    public float Value => Easing(Math.Clamp(Elapsed / Duration, 0f, 1f)) * (To - From) + From;
    public void Update(float dt) => Elapsed = Math.Min(Elapsed + dt, Duration);
    public void Reset(float from, float to, float duration, Func<float, float>? easing = null);
}
```

**`TweenVec2.cs`** — Same for `Vector2` (lerps X and Y independently through same easing curve).

**`AnimationController.cs`** — Frame-driven controller:
```csharp
public sealed class AnimationController
{
    // Keyed by (entityId, property) — e.g., ("tool_3", "positionX")
    public void Start(string key, float from, float to, float duration, Func<float, float>? easing = null);
    public void StartVec2(string key, Vector2 from, Vector2 to, float duration, Func<float, float>? easing = null);
    public void Update(float deltaTime); // Call once per frame
    public float Get(string key, float fallback);
    public Vector2 GetVec2(string key, Vector2 fallback);
    public bool IsAnimating(string key);
    public void Cancel(string key);
    public void CancelAll();
}
```

### Step 3.2: Tool Appear/Disappear Fade

When a tool's `Visible` becomes `true` or a tool is added to the layout:
- `AnimationController.Start($"tool_{id}_alpha", 0f, 1f, 0.15f, Easing.QuadOut)`
- `ToolRenderer` applies: `ImGui.PushStyleVar(ImGuiStyleVar.Alpha, controller.Get(...))`

When hidden/removed:
- `AnimationController.Start($"tool_{id}_alpha", 1f, 0f, 0.10f, Easing.QuadIn)`
- Tool removed from list after animation completes

### Step 3.3: Smooth Drag Ghost

During drag in edit mode:
- Tool renders at mouse-following position (existing behavior)
- A **ghost outline** renders at the snap-target grid position
- Ghost uses accent color at 30% alpha, drawn via draw list `AddRect`
- On mouse release: tool position tweens from free → snapped (0.12s, `CubicOut`)

### Step 3.4: Resize Interpolation

When grid resolution changes or layout is applied:
- Capture old `(Position, Size)` for each tool
- Apply new positions/sizes to the model immediately
- Start position + size tweens: `StartVec2($"tool_{id}_pos", old, new, 0.2f, QuadInOut)`
- `ToolRenderer` uses animated position/size during tween, falls back to model values after

### Step 3.5: Hover Highlights

In edit mode, tool under cursor:
- Border color tween: transparent → `UiColors.AccentPrimary` at 50% alpha (0.1s, `QuadOut`)
- Title bar background brightness boost (+10%)
- On mouse leave: reverse tween

### Step 3.6: Tool Swap Animation

When two tools overlap and are auto-adjusted:
- Both tools animate to new positions simultaneously
- Uses same position tweens as resize interpolation

### Step 3.7: Migrate QuickAccessBarWidget

Replace bespoke animation code at `QuickAccessBarWidget.cs` L50, L65–70, L118–141:
- Delete `_animationProgress`, `_animationStartTime`, `_isAnimatingIn`, `_isAnimatingOut`
- Use `AnimationController.Start("qab_alpha", ...)` with `QuadOut` easing
- Uses consistent animation infrastructure

### Verification Checklist — Phase 3

- [x] Adding a tool shows smooth fade-in (0.15s)
- [x] Removing a tool shows smooth fade-out (0.10s)
- [x] Dragging a tool shows ghost outline at snap target
- [x] Releasing drag shows smooth settle to grid (0.12s)
- [x] Changing grid resolution animates all tools to new positions (0.2s)
- [x] Loading a layout animates tools to their positions (fade-in via AddToolInstanceWithoutDirty)
- [x] Edit mode hover shows subtle border highlight
- [x] QuickAccessBar still animates correctly (migrated to AnimationController)
- [ ] FPS remains above 60 during all animations (measure via FpsTool) — requires in-game testing
- [ ] All animation durations feel "snappy" — requires in-game testing

### Implementation Notes — Phase 3

- `Kaleidoscope/Gui/Animation/` directory with 4 files: Easing.cs, Tween.cs, TweenVec2.cs, AnimationController.cs
- AnimationController uses object pooling (Stack<Tween>) to avoid per-animation allocations
- Expired tweens are reaped each frame and returned to pool
- Tool render loop uses animated position/size with fallback to model values when no animation active
- Pending-removal tools skip RenderToolContent() but still render with fading alpha
- ToolEntry gains `AnimKey` (stable hash-based prefix) and `PendingRemoval` flag
- Hover highlights use accent blue (0.26, 0.59, 0.98) at 50% alpha, 2px border
- Snap ghost drawn with accent blue at 30% alpha, 1.5px border
- QuickAccessBarWidget reduced by ~37 lines; uses ImGui.GetIO().DeltaTime instead of DateTime.Now
- Steps 3.3 (ghost outline) and 3.6 (tool swap) partially implemented — ghost outline done, swap animation deferred to Phase 5 (layout presets)

---

## Phase 4 — Rendering Performance

> **Goal**: Occlusion culling, lazy widget creation, and hot-path allocation elimination.  
> **Risk**: Low–Medium  
> **Estimated effort**: 2–3 days  

### Step 4.1: Occlusion Culling

**In `ToolRenderer.DrawTool()`**: Before calling `tool.RenderToolContent()`:

```csharp
// 1. Off-screen check
var toolRect = (tool.Position, tool.Position + tool.Size);
if (!IsRectVisible(toolRect, contentMin, contentMin + contentSize))
{
    // Skip render entirely — tool is outside visible area
    return;
}

// 2. Z-occlusion check (optional — tools are flat, rarely overlap fully)
// Iterate tools above this one; if any fully contains this rect with opaque background, skip
```

`ImGui.IsRectVisible()` does the viewport check natively.

### Step 4.2: Lazy Widget Creation

**Pattern**: Wrap expensive widgets in `Lazy<T>`:

```csharp
// In DataTool constructor:
private readonly Lazy<GraphWidget> _graphWidget;
private readonly Lazy<ItemTableWidget> _tableWidget;

public DataTool(...)
{
    _graphWidget = new Lazy<GraphWidget>(() => new GraphWidget(...));
    _tableWidget = new Lazy<ItemTableWidget>(() => new ItemTableWidget(...));
}

// In RenderToolContent():
if (_viewMode == ViewMode.Graph)
    _graphWidget.Value.Draw(...); // Created on first access
else
    _tableWidget.Value.Draw(...);
```

**Apply to**: `DataTool` (graph + table), `ItemSalesTrackingTool` (graph + table), `TopInventoryValueTool` (table).

### Step 4.3: Cache Reflection in `DrawSettingsFromSchema`

**File**: `ToolComponent.cs` L158–178

Replace:
```csharp
// BEFORE (L172): Reflection every frame
var drawMethod = typeof(SettingsSchemaRenderer).GetMethod(...).MakeGenericMethod(settingsType);
```

With:
```csharp
// AFTER: Cached per type
private static readonly ConcurrentDictionary<Type, MethodInfo> _cachedDrawMethods = new();

var drawMethod = _cachedDrawMethods.GetOrAdd(settingsType, type =>
    typeof(SettingsSchemaRenderer).GetMethod(nameof(SettingsSchemaRenderer.Draw))!.MakeGenericMethod(type));
```

### Step 4.4: Eliminate Hot-Path LINQ in Graph

**File**: `Graph.cs` L169

Replace per-frame allocation:
```csharp
// BEFORE: Allocates new List every frame
=> RenderMultipleSeries(series.Select(s => (s.name, s.samples, (Vector4?)null)).ToList());
```

With reusable buffer:
```csharp
// AFTER: Reuse list
private readonly List<(string, IReadOnlyList<(DateTime, float)>, Vector4?)> _projectionBuffer = new();

public void RenderMultipleSeries(... series)
{
    _projectionBuffer.Clear();
    foreach (var s in series)
        _projectionBuffer.Add((s.name, s.samples, null));
    RenderMultipleSeries(_projectionBuffer);
}
```

### Step 4.5: Replace JSON Deep Clone

**File**: `LayoutEditingService.cs` L582–587

Replace `CloneToolList()` with explicit cloning:

```csharp
private static List<ToolLayoutState> CloneToolList(List<ToolLayoutState> source)
{
    var clone = new List<ToolLayoutState>(source.Count);
    foreach (var tool in source)
        clone.Add(tool.Clone());
    return clone;
}
```

Add `Clone()` method to `ToolLayoutState`:
```csharp
public ToolLayoutState Clone()
{
    return new ToolLayoutState
    {
        Id = Id, Type = Type, Title = Title, CustomTitle = CustomTitle,
        Position = Position, Size = Size, Visible = Visible,
        BackgroundEnabled = BackgroundEnabled, HeaderVisible = HeaderVisible,
        OutlineEnabled = OutlineEnabled, BackgroundColor = BackgroundColor,
        GridCol = GridCol, GridRow = GridRow, GridColSpan = GridColSpan,
        GridRowSpan = GridRowSpan, HasGridCoords = HasGridCoords,
        HiddenSeries = new List<string>(HiddenSeries),
        ToolSettings = new Dictionary<string, object?>(ToolSettings) // Shallow — values are immutable
    };
}
```

### Step 4.6: Throttle Dirty Marking During Drag

**File**: `LayoutEditingService.cs` in `MarkDirty()`

```csharp
// During active drag/resize: throttle to once per 100ms
if (_stateService.IsInteracting)
{
    if ((DateTime.UtcNow - _lastDirtyMark).TotalMilliseconds < 100)
        return; // Skip — will catch final position on release
}
_lastDirtyMark = DateTime.UtcNow;
```

The final position on mouse release always triggers an unthrottled mark.

### Verification Checklist — Phase 4

- [x] Profiler tab: measure frame time with 10+ tools, before vs after — requires in-game testing
- [x] Off-screen tools show 0 draw calls (occlusion culling added to render loop)
- [ ] `DataTool` in table mode: graph widget not allocated — deferred (both widgets are settings providers, lazy init adds complexity for minimal gain)
- [x] Settings panel for any tool: no per-frame reflection (ConcurrentDictionary<Type, MethodInfo> cache)
- [x] Graph hover: no growing allocations (reusable _projectionBuffer replaces .Select().ToList())
- [x] Layout save/load round-trips after clone refactor (ToolLayoutState.Clone() manual copy)
- [x] Drag tool continuously: MarkDirty throttled to ≤10/sec during drag (100ms threshold)

### Implementation Notes — Phase 4

- **Occlusion culling**: Screen-space bounds check before animation resolution and ImGui child window creation
- **Reflection cache**: `static ConcurrentDictionary<Type, MethodInfo?>` in ToolComponent, resolved once per settings type
- **LINQ elimination**: `_projectionBuffer` (reusable List) replaces `.Select().ToList()` in Graph.RenderMultipleSeries
- **Clone optimization**: `ToolLayoutState.Clone()` manual property copy replaces `JsonConvert.Serialize/Deserialize` (~100x faster)
- **MarkDirty throttle**: Rapid calls within 100ms skip CloneToolList but still set dirty flag; final release always triggers
- **Lazy DataTool widgets**: Deferred — both graph and table are `ISettingsProvider` requiring registration at construction, making lazy init add complexity for minimal startup gain

---

## Phase 5 — Hybrid Layout System

> **Goal**: Auto-layout presets alongside the existing grid-snap manual placement.  
> **Risk**: Low (additive feature)  
> **Estimated effort**: 3–4 days  

### Step 5.1: Define Layout Preset Models

**Add to** `Configuration.Layout.cs`:

```csharp
public enum LayoutArrangement
{
    Grid,             // Existing manual grid-snap
    SingleColumn,     // Tools stacked vertically, full width
    TwoColumn,        // Tools fill left then right column
    ThreeColumn,      // Three equal columns
    SplitHorizontal,  // Top half + bottom half
    SplitVertical,    // Left half + right half  
    Dashboard,        // First tool full-width header, rest in grid below
}
```

Add `LayoutArrangement Arrangement` property to `ContentLayoutState` (default: `Grid`).

### Step 5.2: Create `AutoLayoutEngine`

**New file**: `Kaleidoscope/Gui/MainWindow/AutoLayoutEngine.cs`

```csharp
public static class AutoLayoutEngine
{
    public static void ApplyPreset(LayoutArrangement arrangement, 
                                    List<ToolComponent> tools,
                                    int gridColumns, int gridRows)
    {
        switch (arrangement)
        {
            case LayoutArrangement.SingleColumn:
                LayoutSingleColumn(tools, gridColumns, gridRows);
                break;
            case LayoutArrangement.TwoColumn:
                LayoutTwoColumn(tools, gridColumns, gridRows);
                break;
            // ... etc
        }
    }
}
```

**Layout algorithms**:
- **SingleColumn**: Each tool gets full width, equal height shares: `GridColSpan = gridColumns`, `GridRowSpan = gridRows / toolCount`
- **TwoColumn**: Tools alternate columns. Each gets half-width: `GridColSpan = gridColumns / 2`
- **ThreeColumn**: Thirds. `GridColSpan = gridColumns / 3`
- **SplitHorizontal**: First half of tools on top, second half on bottom
- **SplitVertical**: First half left, second half right
- **Dashboard**: Tool[0] spans full width in top 25%, remaining tools fill a grid in bottom 75%

### Step 5.3: Integrate Into Context Menu

In `ContextMenuManager`, add "Quick Layouts" submenu to content-area right-click:

```
Right-click → Add Tool → ...
Right-click → Quick Layouts →
    ├── Single Column
    ├── Two Columns  
    ├── Three Columns
    ├── Split Horizontal
    ├── Split Vertical
    └── Dashboard
```

Selecting a preset calls `AutoLayoutEngine.ApplyPreset()`, then `ILayoutHost.NotifyLayoutChanged()` to mark dirty.

### Step 5.4: Integrate Into Layouts Config Tab

**File**: `Kaleidoscope/Gui/ConfigWindow/ConfigCategories/LayoutsCategory.cs`

Add "Start from preset" combo when creating a new layout. User picks a preset → layout created with tools auto-arranged.

### Step 5.5: Preserve Manual Overrides

After auto-layout, user can:
- Drag any tool to a new position (enters manual grid-snap mode)
- Layout's `Arrangement` updates to `Grid` once any manual adjustment is made
- Re-applying the preset re-calculates positions for all current tools

### Step 5.6: Responsive Re-Layout

On significant window resize (fullscreen toggle, manual resize that changes aspect ratio):
- If layout was auto-arranged (`Arrangement != Grid`), re-apply the preset
- Existing proportional grid-snap repositioning remains the fallback for `Grid` arrangements

### Verification Checklist — Phase 5

- [ ] Each preset type arranges tools without overlap
- [ ] Tools fill available space proportionally
- [ ] After auto-layout, drag a tool → works normally
- [ ] Save an auto-layout → load → tools in same positions
- [ ] Fullscreen ↔ windowed with auto-layout: tools re-arrange
- [ ] New layout creation with preset selection works
- [ ] Context menu shows Quick Layouts submenu

---

## Phase 6 — Final Polish & Cleanup

> **Goal**: Dead code removal, consistency, concurrency fixes, documentation.  
> **Risk**: Low  
> **Estimated effort**: 2–3 days  

### Step 6.1: Remove Dead Code

- Delete `ContentComponentState` from `Configuration.Layout.cs` (L39–45) — verify no references first via grep
- Delete any orphaned references from migration

### Step 6.2: Standardize JSON Serializer

- Migrate from Newtonsoft `JsonConvert` to `System.Text.Json` throughout `LayoutEditingService`
- Add migration code in `SettingsImportHelper` to detect and convert legacy Newtonsoft `JArray`/`JObject` on first load
- Remove Newtonsoft dependency from layout serialization paths (keep in `SettingsImportHelper` for backward compat one version, then remove)

### Step 6.3: Fix `LayoutEditingService` Concurrency

- Replace `volatile bool _isDirty` + separate `_snapshotLock` with `ReaderWriterLockSlim`
- Timer callbacks: dispatch to framework thread via `_framework.RunOnFrameworkThread()` before touching `_workingLayout`
- Ensure `InitializeFromPersisted`, `MarkDirty`, `Save`, `DiscardChanges` are consistently synchronized
- Remove redundant `volatile` on `_isDirty` once under RWL

### Step 6.4: Settings Type Safety

For tools that use `SettingsSchema<T>`:
- Settings export uses `schema.ToDictionary(settings)` instead of manual `ExportToolSettings()`
- Settings import uses `schema.FromDictionary(settings, target)` instead of manual `ImportToolSettings()`
- Reduces code in each tool and removes the untyped `Dictionary<string, object?>` boundary where possible

### Step 6.5: Update Project Documentation

- Update `.github/copilot-instructions.md` with new architecture: `ILayoutHost`, extracted subsystems, `ToolFactory`, `AnimationController`, `AutoLayoutEngine`
- Add inline XML docs to all new public types and methods
- Update "Adding a UI Tool" section to reflect `[ToolType]` + constructor injection pattern

### Verification Checklist — Phase 6

- [ ] Full regression: every tool type, every edit mode interaction
- [ ] Layout save/load/switch across windowed and fullscreen
- [ ] Config window: all tabs, including developer tabs
- [ ] Clean build, zero warnings
- [ ] Profiler tab: stable or improved frame times vs baseline
- [ ] Legacy layouts (from before refactor) load correctly with migration

---

## Appendix A — File Reference Index

All files touched or created across all phases, with current state and planned changes.

### Files Created

| Phase | File | Purpose |
|-------|------|---------|
| 1 | `Kaleidoscope/Interfaces/ILayoutHost.cs` | Replace 20 callbacks with typed interface |
| 1 | `Kaleidoscope/Gui/MainWindow/ToolInteractionManager.cs` | Drag/resize logic extracted from Drawing.cs |
| 1 | `Kaleidoscope/Gui/MainWindow/ToolRenderer.cs` | Single-tool rendering extracted from Drawing.cs |
| 1 | `Kaleidoscope/Gui/MainWindow/GridRenderer.cs` | Grid overlay extracted from Drawing.cs |
| 1 | `Kaleidoscope/Gui/MainWindow/ContextMenuManager.cs` | Context menus extracted from Drawing.cs |
| 1 | `Kaleidoscope/Gui/MainWindow/DialogManager.cs` | Modals extracted from Dialogs.cs |
| 2 | `Kaleidoscope/Gui/MainWindow/ToolTypeAttribute.cs` | Attribute for tool auto-discovery |
| 2 | `Kaleidoscope/Services/ToolFactory.cs` | DI-based tool creation |
| 3 | `Kaleidoscope/Gui/Animation/Easing.cs` | Easing functions |
| 3 | `Kaleidoscope/Gui/Animation/Tween.cs` | Single-value tween |
| 3 | `Kaleidoscope/Gui/Animation/TweenVec2.cs` | Vector2 tween |
| 3 | `Kaleidoscope/Gui/Animation/AnimationController.cs` | Frame-driven animation manager |
| 5 | `Kaleidoscope/Gui/MainWindow/AutoLayoutEngine.cs` | Auto-layout preset algorithms |

### Files Modified

| Phase | File | Current Lines | Change Summary |
|-------|------|---------------|----------------|
| 1 | `Gui/MainWindow/WindowContentContainer.cs` | 493 | Remove 20 callbacks, accept `ILayoutHost`, slim to ~200 lines |
| 1 | `Gui/MainWindow/WindowContentContainer.Drawing.cs` | 976 | Extract to 4 classes → **delete file** |
| 1 | `Gui/MainWindow/WindowContentContainer.Layout.cs` | 324 | Stays (layout export/import/apply) |
| 1 | `Gui/MainWindow/WindowContentContainer.Dialogs.cs` | 501 | Extract to `DialogManager` → **delete file** |
| 1 | `Gui/MainWindow/MainWindow.cs` | 1,304 | Implement `ILayoutHost`, delete `WireLayoutCallbacks` |
| 2 | `Gui/MainWindow/WindowToolRegistrar.cs` | 629 | Replace factories with `ToolFactory` delegation → ~50 lines |
| 2 | `Gui/MainWindow/ToolCreationContext.cs` | 35 | **Delete file** |
| 2 | All 20 tool classes | varies | Add `[ToolType]`, update constructors |
| 3 | `Gui/Widgets/QuickAccessBarWidget.cs` | ~200 | Replace bespoke animation with `AnimationController` |
| 3 | `Gui/MainWindow/ToolRenderer.cs` | (new) | Add alpha animation support |
| 3 | `Gui/MainWindow/ToolInteractionManager.cs` | (new) | Add drag ghost + settle animation |
| 4 | `Gui/MainWindow/ToolComponent.cs` | 418 | Cache `MakeGenericMethod` at L172 |
| 4 | `Gui/Widgets/Graph/Graph.cs` | 1,192 | Replace LINQ at L169 with reusable buffer |
| 4 | `Services/LayoutEditingService.cs` | 776 | Replace `CloneToolList`, throttle dirty marks |
| 4 | `Configuration.Layout.cs` | 165 | Add `Clone()` to `ToolLayoutState` |
| 5 | `Configuration.Layout.cs` | 165 | Add `LayoutArrangement` enum + property |
| 5 | `Gui/ConfigWindow/ConfigCategories/LayoutsCategory.cs` | varies | Preset selection on new layout |
| 6 | `Configuration.Layout.cs` | — | Remove `ContentComponentState` |
| 6 | `Services/LayoutEditingService.cs` | — | Fix concurrency, switch JSON serializer |
| 6 | `.github/copilot-instructions.md` | — | Update architecture documentation |

### Files Deleted

| Phase | File | Reason |
|-------|------|--------|
| 1 | `Gui/MainWindow/WindowContentContainer.Drawing.cs` | Split into 4 extracted classes |
| 1 | `Gui/MainWindow/WindowContentContainer.Dialogs.cs` | Extracted to `DialogManager` |
| 2 | `Gui/MainWindow/ToolCreationContext.cs` | Replaced by `ToolFactory` + direct DI |

---

## Appendix B — External Patterns Considered

### InventoryTools (Critical-Impact)
- Uses a **mediator/message bus** (`MediatorService` with typed `record` messages like `ToggleDalamudWindowMessage`, `ListModifiedMessage`, `CloseWindowsByTypeMessage`)
- ~40 distinct message types for UI events, data changes, navigation
- **Decision**: Not adopted. Our event density is lower, and the existing `Action<T>` pattern works well once callback soup is replaced with `ILayoutHost` interface. A mediator would add routing complexity without proportional benefit.

### Penumbra (Ottermandias)
- Tab-based UI architecture via OtterGui components
- Uses OtterGui's `ServiceManager` for DI (same library we vendor)
- No customizable layout system — fixed tab structure
- **Takeaway**: Confirms that `OtterGui.ServiceManager` + `ActivatorUtilities` is a viable DI pattern.

### Dalamud Window System (goatcorp)
- `Window.cs` (1,032 lines) provides: fade-in/out (0.072s), title bar buttons, pinning, click-through, ESC close, error recovery
- `WindowDrawFlags` enum for behavior control
- Preset persistence via `WindowSystemPersistence`
- **Takeaway**: Our animation target of ≤0.2s aligns with Dalamud's own transitions. Tool appear/disappear fades should use similar alpha-based approach.

---

## Progress Tracker

| Phase | Status | Started | Completed | Notes |
|-------|--------|---------|-----------|-------|
| **1** — Decompose WindowContentContainer | ✅ Complete | 2026-03-18 | 2026-03-18 | ILayoutHost, DrawContext, 4 managers extracted |
| **2** — Tool-Level DI | ✅ Complete | 2026-03-18 | 2026-03-18 | ToolFactory + ToolTypeAttribute, 629→80 line registrar, MainWindow 24→12 params |
| **3** — Animation Framework | ✅ Complete | 2026-03-19 | 2026-03-19 | Easing/Tween/AnimationController, tool fade/snap/hover/ghost, QAB migrated |
| **4** — Rendering Performance | ✅ Complete | 2026-03-19 | 2026-03-19 | Occlusion culling, reflection cache, LINQ elimination, Clone() replaces JSON, MarkDirty throttle |
| **5** — Hybrid Layout System | ⬜ Not Started | — | — | Depends on Phases 1 + 3 (animations) |
| **6** — Final Polish & Cleanup | ⬜ Not Started | — | — | After all other phases |

### Dependencies

```
Phase 1 (Foundation)
  ├──→ Phase 2 (Tool DI)
  ├──→ Phase 3 (Animation) ──→ Phase 5 (Layout Presets)
  ├──→ Phase 4 (Performance)
  └──────────────────────────→ Phase 6 (Cleanup)
```

Phases 2, 3, and 4 can be worked in parallel after Phase 1 is complete. Phase 5 requires Phase 3 (animations for layout transitions). Phase 6 is a final sweep after everything else.
