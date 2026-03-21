# Copilot Instructions — Kaleidoscope

## Project Overview

Kaleidoscope is a **Dalamud plugin** (FFXIV) for tracking game data across multiple characters. It targets `net10.0-windows` using `Dalamud.NET.Sdk/14.0.1`. The solution has two projects: **Kaleidoscope** (the plugin) and **OtterGui** (DI + UI utilities). `OtterGui` and `FFXIVClientStructs` are vendored submodules — **do not modify them**. ImGui/ImPlot widgets (graph, table, combo, tree, date picker) live directly in `Kaleidoscope/Gui/Widgets/`.

---

## Architecture

### Plugin Lifecycle (`Kaleidoscope/Core/KaleidoscopePlugin.cs`)
1. Constructor receives `IDalamudPluginInterface` from Dalamud.
2. `StaticServiceManager.CreateProvider()` builds the DI container (assembly-scanned).
3. Static services initialized manually: `LogService.Initialize()` → `LogService.InitializeServices()` → `GameStateService.Initialize()`.
4. `ServiceManager.GetService<T>()` eagerly resolves all `IRequiredService` types.
5. Dispose: shuts down `GameStateService`, `LogService`, then disposes the entire `ServiceProvider`.
6. Constructor wraps in try/catch — disposes on failure before rethrowing.

### Dependency Injection
- Uses **OtterGui's `ServiceManager`**. Services are auto-discovered by marker interface — never registered explicitly.
- `IService` → lazy singleton (resolved on first request). `IRequiredService` → eager singleton (instantiated at startup).
- Dalamud-provided interfaces (`ICommandManager`, `IGameGui`, `IClientState`, etc.) are registered explicitly in `StaticServiceManager` via `AddDalamudServices()`.
- `ToolFactory` discovers tools via `[ToolType]` attributes and instantiates them with `ActivatorUtilities.CreateInstance` (full constructor DI).

### Static Escape Hatches
Three `static class` services provide DI-free access for unsafe/static contexts:
- **`LogService`** — logging facade with `Initialize()` / `InitializeServices()` / `Cleanup()` lifecycle
- **`GameStateService`** — unsafe game memory reads with `Initialize()` / `Cleanup()` lifecycle
- **`ConfigStatic`** — compile-time `const` / `static readonly` values only (no lifecycle)
- Prefer **constructor injection everywhere else**. These exist only for contexts where DI is unavailable.

---

## Database (`KaleidoscopeDbService` — 10 partial files)

### File Split

| File | Domain |
|------|--------|
| `KaleidoscopeDbService.cs` | Core: constructor, connections, WAL, schema, migrations |
| `.Characters.cs` | Character name/display-name/color CRUD, 30s name cache |
| `.Series.cs` | Time-series CRUD (insert, query, windowed reads) |
| `.Data.cs` | Bulk ops: clear/delete ranges, CSV export, VACUUM, orphan cleanup |
| `.Export.cs` | Simple CSV export |
| `.Inventory.cs` | Inventory cache persistence (batched inserts, transactional saves) |
| `.Prices.cs` | Market prices, price history, inventory value history, stale detection |
| `.Sales.cs` | Sale records: CRUD, batch save, delete with value recalculation |
| `.Maintenance.cs` | WAL checkpoint, VACUUM with size stats |
| `.Query.cs` | Developer tools: raw SQL execution, table/schema introspection |

### Schema (9 tables)
`series` + `points` (time-series), `character_names`, `inventory_cache` + `inventory_items`, `item_prices` + `price_history`, `inventory_value_history` + `inventory_value_items`, `sale_records`. All have extensive indexes (20+).

### Connection Model
- **Write connection** (`_connection`): `ReadWriteCreate` mode with `journal_mode=WAL`, `foreign_keys=ON`, `synchronous=NORMAL`.
- **Read connection** (`_readConnection`): `ReadOnly` mode, opened after schema setup. WAL allows concurrent reads during writes.
- **Thread safety**: Two `object` locks — `_writeLock` for writes on `_connection`, `_readLock` for reads on `_readConnection` (or fallback to `_connection`).
- **Dispose**: Checkpoints WAL (TRUNCATE), then closes both connections.

### Migrations
Idempotent — each checks via `PRAGMA table_info()` (columns) or `sqlite_master` (tables) before applying. No version table. Run in `RunMigrations()` at startup. Pattern:
```csharp
private void MigrateAddXyzColumn() {
    // Check: PRAGMA table_info(target_table) → scan for column name
    // If missing: ALTER TABLE target_table ADD COLUMN xyz TYPE NOT NULL DEFAULT val
}
```

### Caching
- **Character name cache**: In-memory dictionary, 30s TTL, invalidated on writes.
- **Inventory value stats cache**: Record count + max timestamp, invalidated after writes, refreshed lazily.
- External caches exist as separate services: `MarketDataCacheService`, `TimeSeriesCacheService`, `SalePriceCacheService`, `InventoryCacheService`, `CharacterDataCacheService`.

---

## Threading Model

### Main Thread (Framework/UI)
- All `Framework.Update` callbacks — game state polling, value change detection, periodic task scheduling.
- All ImGui rendering (`WindowService` → `Draw()`).
- All direct game memory reads (unsafe `InventoryManager*`, `PlayerState*`, etc.).
- Dalamud event handlers (`Login`, `Logout`, `TerritoryChanged`).

### Background Threads
- **`Channel<T>` producer-consumer workers**: `UniversalisService` (price writes, 1k bounded DropOldest) and `CurrencyTrackerService` (sample writes, 10k bounded DropOldest) use `Channel<T>` with dedicated background `Task` consumers for SQLite writes.
- **Fire-and-forget `Task.Run`**: Value snapshots, cleanup, world data refresh, inventory cache flush.
- **WebSocket receive loop**: `UniversalisService` runs `ReceiveLoopAsync` as a long-running task.
- **HTTP requests**: Universalis REST API calls run on thread pool.

### Critical Rule: Game Data Access
**Never** access game memory from background threads. Pattern:
1. Read game data on main thread (Framework tick), capture into DTOs/plain values.
2. Enqueue captured data to `Channel<T>` or pass to `Task.Run`.
3. If background code *must* read game data, use `Framework.RunOnFrameworkThread()` (rare — only 2 usages exist).

### Concurrency Primitives

| Primitive | Where |
|-----------|-------|
| `Channel<T>` (bounded) | `UniversalisService` (1k), `CurrencyTrackerService` (10k) — DB write queues |
| `SemaphoreSlim` | HTTP rate limiting, cache initialization guards |
| `ConcurrentDictionary` | Price caches, inventory caches, log writers, sale price caches |
| `ConcurrentQueue` | WebSocket live feed buffer |
| `lock` (object) | DB read/write locks, config save, file I/O, rate limit queues |
| `volatile bool` | Disposed flags, pending recalc, cache population flags |
| `ThreadLocal<T>` | Per-thread character context for log file routing |

---

## Configuration & Persistence

### Hybrid Model (`ConfigService`)
- **Main config** (`Configuration`): Dalamud's `IPluginConfiguration.Save()` for top-level settings.
- **Sub-configs** (JSON files via `ConfigService`):
  - `general.json` → `GeneralSettings` (ShowOnStart, ExclusiveFullscreen, grid, EditMode)
  - `currencytracker.json` → `CurrencyTrackerSettings` (TrackingEnabled, IntervalMs, CacheSizeMb)
  - `windows.json` → `WindowSettings` (pin state, positions, sizes)
  - `layouts.json` → `LayoutSettings` (all saved layouts)
- `Configuration` partial class split: `.cs` (top-level), `.Enums.cs`, `.Layout.cs`, `.Settings.cs`.

### Save Mechanics
- **500ms debounce**: `RequestSave()` sets dirty flag and restarts timer. Timer elapsed → `PerformSave()`.
- **Flush**: `Flush()` saves immediately. `FlushIfDirty()` skips if clean.
- **Lock**: All saves acquire an `object` lock (not SemaphoreSlim).
- **Events**: `OnConfigSaved`, `OnLayoutChanged`.
- **Statistics**: Tracks save count, skip count, debounce resets.

---

## Event System

Events use `Action<T>` delegates (never `EventHandler`). Pattern:
```csharp
public event Action<uint>? OnPriceUpdated;
// Raised with: OnPriceUpdated?.Invoke(itemId);
```
- Subscribers use `+=` in constructors, `-=` in `Dispose()`.
- Events from Framework tick run on main thread; WebSocket events run on thread pool.
- Key events: `OnValuesChanged`, `OnPriceReceived`, `OnWebSocketStatusChanged`, `OnFullscreenChanged`, `OnEditModeChanged`, `OnPriceUpdated`, `OnConfigSaved`, `OnLayoutChanged`, `OnCacheUpdated`.

---

## Key Conventions

### Adding a Service
1. Create a `sealed class` in `Kaleidoscope/Services/` implementing `IService` (lazy) or `IRequiredService` (eager) and optionally `IDisposable`.
2. Use constructor injection for all dependencies — the container resolves them automatically.
3. No manual registration needed; the assembly scanner picks it up.

### Adding a UI Tool
1. Create a class extending `ToolComponent` in `Kaleidoscope/Gui/MainWindow/Tools/<Category>/`.
2. Decorate with `[ToolType(id, label, category, description)]` — the `ToolFactory` discovers and registers it automatically (no manual registration).
3. Declare dependencies as **constructor parameters** — `ActivatorUtilities.CreateInstance` resolves them from DI.
4. Override `RenderToolContent()` for rendering.
5. For settings, choose one of two patterns (see **Settings Patterns** below).
6. To compose with widgets: create the widget in constructor, call `RegisterSettingsProvider(widget)` so widget settings appear in the tool's settings panel.
7. Large tools use **partial classes** split across multiple files.

#### Registration Examples
```csharp
// Simple tool
[ToolType("Label", "Label", "Utility", "A simple text label")]
public sealed class LabelTool : ToolComponent { ... }

// Multiple variants (same class, different tool IDs)
[ToolType("DataGraph", "Data Graph", "Items/Currency", "Graph view", Variant = "Graph")]
[ToolType("DataTable", "Data Table", "Items/Currency", "Table view", Variant = "Table")]
public sealed partial class DataTool : ToolComponent { ... }

// With required service gates (tool hidden if services unavailable)
[ToolType("WebsocketFeed", "Websocket Feed", "Universalis",
    RequiredServices = new[] { typeof(UniversalisWebSocketService) })]
public sealed class WebsocketFeedTool : ToolComponent { ... }
```

#### Settings Patterns
**Schema-based** — best for tools with 5+ settings, provides declarative rendering + export/import:
```csharp
private static readonly SettingsSchema<MyToolSettings> Schema = new SettingsSchema<MyToolSettings>()
    .Checkbox(s => s.ShowThing, "Show Thing", "Tooltip")
    .SliderFloat(s => s.Threshold, "Threshold", 0f, 100f, "Tooltip");

protected override bool HasToolSettings => true;
protected override object? GetToolSettingsSchema() => Schema;
protected override object? GetToolSettingsObject() => _settings;
public override Dictionary<string, object?>? ExportToolSettings() => Schema.ToDictionary(_settings)!;
public override void ImportToolSettings(Dictionary<string, object?>? settings) => Schema.FromDictionary(_settings, settings);
```

**Manual** — simpler for tools with 1–4 settings or complex types:
```csharp
protected override bool HasToolSettings => true;
protected override void DrawToolSettings() { /* ImGui calls */ }
public override Dictionary<string, object?>? ExportToolSettings() =>
    new() { ["MaxEntries"] = _maxEntries, ["ShowHqOnly"] = _showHqOnly };
public override void ImportToolSettings(Dictionary<string, object?>? settings) {
    _maxEntries = GetSetting(settings, "MaxEntries", 100);
    _showHqOnly = GetSetting(settings, "ShowHqOnly", false);
}
```

### Adding a Config Window Tab
Add in `Kaleidoscope/Gui/ConfigWindow/` following the sidebar-category pattern. Developer-only tabs are gated behind **CTRL+ALT** (temporary) or `Config.DeveloperModeEnabled` (persistent toggle in Profiler tab).

### Adding a Window
Create under `Kaleidoscope/Gui/`, extend Dalamud's `Window`, register via `WindowService`.

---

## Tool System (`Kaleidoscope/Gui/MainWindow/`)

### ToolComponent (Base Class)
Abstract base for all tools. Key members:
- **`RenderToolContent()`** — abstract, draws tool content each frame.
- **`ToolName`** — virtual, inherent type name.
- **`ExportToolSettings()` / `ImportToolSettings()`** — virtual, layout persistence.
- **`GetToolSettingsSchema()` / `GetToolSettingsObject()`** — virtual, declarative settings via `SettingsSchema<T>`.
- **`DrawToolSettings()`** — virtual, imperative settings fallback.
- **`GetContextMenuOptions()`** — virtual, custom right-click items.
- **`RegisterSettingsProvider(ISettingsProvider)`** — register widget settings providers.
- Properties: `Id`, `Title`, `CustomTitle`, `DisplayTitle`, `Position`, `Size`, `Visible`, `BackgroundEnabled`, `BackgroundColor`, `HeaderVisible`, `OutlineEnabled`, grid coordinates (`GridCol/GridRow/GridColSpan/GridRowSpan`).
- Helper methods: `GetSetting<T>()`, `ExportColor()`, `ImportColor()`, `ExportHashSet<T>()`, `ImportHashSet<T>()`, `NotifyToolSettingsChanged()`, `LogDebug()`, `LogError()`.

### ToolTypeAttribute
`[ToolType(id, label, category, description)]` — marks a `ToolComponent` subclass for auto-discovery.
- **`AllowMultiple = true`** — one class can register multiple tool IDs with different `Variant` values.
- **`RequiredServices`** — `Type[]` of services that must be available; tool hidden from menus if any are missing.
- **`Variant`** — post-creation config key (e.g., `"Graph"` vs `"Table"` for `DataTool`).
- Current categories: `"Help"`, `"Universalis"`, `"Utility"`, `"AutoRetainer"`, `"Items/Currency"`, `"FFXIVMT"`.

### ToolFactory (`IService`)
Auto-discovers all `[ToolType]`-decorated classes at construction via assembly scan. Creates tool instances with `ActivatorUtilities.CreateInstance()` (full constructor DI). Handles legacy ID resolution, required service checks, and variant actions.
- **`Create(string id, Vector2? position)`** — instantiate a tool by registered ID.
- **`GetAvailableDefinitionsByCategory()`** — for building "Add Tool" menus.
- Variant actions registered in `RegisterVariantActions()` (e.g., `"Graph"` → sets `ViewMode`).

### SettingsSchema<T> (`Kaleidoscope/Models/Settings/`)
Fluent builder for declarative tool settings:
- **Builder**: `.Checkbox()`, `.SliderFloat()`, `.SliderInt()`, `.Combo<TEnum>()`, `.RadioGroup<TEnum>()`, `.ColorEdit()`, `.TextInput()`, `.TextMultiline()`, `.Separator()`, `.Spacing()`, `.Header()`.
- **Persistence**: `ToDictionary(settings)` / `FromDictionary(settings, dict)` for layout export/import.
- **Rendering**: `SettingsSchemaRenderer.Draw<T>()` renders all definitions as ImGui controls.
- Uses compiled expression trees for getter/setter — no per-frame reflection.

---

## Layout System

### ILayoutHost (`Kaleidoscope/Gui/MainWindow/ILayoutHost.cs`)
Typed contract between `WindowContentContainer` and `MainWindow`. Replaces 20+ callback/delegate fields with a single interface. Used by `WindowContentContainer`, `DialogManager`, `ToolInteractionManager`, `ContextMenuManager`.
- Layout persistence: `SaveLayout()`, `LoadLayout()`, `GetAvailableLayoutNames()`, `GetCurrentLayoutName()`.
- Dirty state: `IsDirty`, `SaveLayoutExplicit()`, `DiscardChanges()`, `MarkLayoutDirty()`.
- Unsaved changes dialog: `ShowUnsavedChangesDialog`, `HandleUnsavedChangesChoice()`.
- Presets: `SavePreset()`, `CanSavePresets`.
- Interaction: `IsMainWindowInteracting`, `IsFullscreenMode`, `NotifyDraggingChanged()`, `NotifyResizingChanged()`.

### LayoutEditingService (`Kaleidoscope/Services/`)
Manages layout editing with **explicit save semantics** (like a file editor). Changes applied in memory immediately, persisted only on explicit Save.
- **Dirty tracking**: `volatile bool _isDirty`, `MarkDirty()` with 100ms throttle, `OnDirtyStateChanged` event.
- **Snapshot crash recovery**: Serializes working layout to `layout_dirty_snapshot.json` with 1000ms debounce. Atomic write (temp file + rename). Auto-restores on startup.
- **Auto-save**: Optional 500ms debounce auto-save, marshals to framework thread.
- **Destructive action guards**: `TryPerformDestructiveAction()` / `TrySwitchLayout()` block if dirty, show unsaved-changes dialog.
- **Threading**: All public methods on main thread except `FlushDirtySnapshot()` and `GetStatistics()`. `_snapshotLock` protects working layout cloning during serialization.

### AutoLayoutEngine (`internal static class`)
Calculates grid coordinates for automatic layout arrangements.
- **`ApplyPreset(arrangement, tools, gridColumns, gridRows)`** — sets `GridCol/GridRow/GridColSpan/GridRowSpan` on each tool.
- Arrangement types: `Grid` (manual, no-op), `SingleColumn`, `TwoColumn`, `ThreeColumn`, `SplitHorizontal`, `SplitVertical`, `Dashboard` (first tool full-width header, rest in grid below).
- Operates in grid-coordinate space — normal grid→pixel conversion handles positioning.

---

## Animation Framework (`Kaleidoscope/Gui/Animation/`)

### AnimationController
String-keyed tween manager with object pooling. Must call `Update(deltaTime)` once per frame.
- **Float tweens**: `Start(key, from, to, duration, easing?)` / `Get(key, fallback)` / `IsAnimating(key)`.
- **Vector2 tweens**: `StartVec2(key, from, to, duration, easing?)` / `GetVec2(key, fallback)` / `IsAnimatingVec2(key)`.
- **Lifecycle**: `Cancel(key)`, `CancelAll()`, `HasActiveAnimations`.
- Instances: `WindowContentContainer` owns one for tool animations; `QuickAccessBarWidget` owns another for bar animations.

### Easing Functions (`Easing` static class)
`Linear`, `QuadIn`, `QuadOut`, `QuadInOut`, `CubicIn`, `CubicOut`, `CubicInOut`, `SmoothStep`, `Spring`.

---

## Widget Library (`Kaleidoscope/Gui/Widgets/`)

Reusable ImGui/ImPlot widgets organized into subdirectories by type. These were formerly the separate MTGui project.

### Graph (`GraphRenderer` in `Widgets/Graph/`)
```csharp
var graph = new GraphRenderer(new GraphConfig { PlotId = "myplot", GraphType = GraphType.Area });
// Each frame: graph.Render(seriesList, settings);
```
- Data is pushed each frame via `Render()` — no data-binding interface.
- Config: ~30 properties (legend position, auto-scroll, crosshair, grid lines, graph type).
- `GraphWidget` adds two-way `IGraphWidgetSettings` binding and a full settings UI.

### Table (`TableWidget<TRow>` in `Widgets/Table/`)
```csharp
var table = new TableWidget<MyRow>("tableId", "Empty message");
table.BindSettings(settings, () => Save(), "Settings Label");
// Each frame: table.Draw(columns, rows, cellRenderer, sortKeySelector, settings, height);
```
- Columns: `TableColumn` (label, width, flags). Cell rendering via delegate.
- `ItemTableWidget` (5 partial files) adds character rows, item columns, retainer breakdown, merged rows/columns, grouping.

### Combo (`ComboWidget<TItem, TId>` in `Widgets/Combo/`)
```csharp
var state = new ComboState<uint> { SortOrder = ComboSortOrder.Alphabetical };
var combo = new ComboWidget<MyItem, uint>(config, state)
    .WithFilter((item, text) => item.Name.Contains(text))
    .WithIconRenderer(DrawIcon);
combo.UpdateItems(itemList);
// Each frame: combo.Draw() or combo.DrawMultiSelect();
```
- Supports single/multi-select, favorites, hierarchical grouping (3 levels), search.
- Domain wrappers: `ItemComboDropdown`, `CurrencyComboDropdown`, `CharacterCombo`.

### Tree (`TreeNode<TKey, TData>` in `Widgets/Tree/`)
- Data model: nested `TreeNode` with key, label, icon, children, data payload.
- State: `TreeExpansionState<TKey>` tracks expand/collapse.
- Rendering: Static `TreeHelpers` methods (`DrawTree`, `DrawCollapsingSection`).

### Common Utilities (`Widgets/Common/`)
- `NumberFormatter` — K/M/B formatting, `NumberFormatConfig`, `INumberFormatSettings`.
- `FlavourText` — random flavour text pool.

### DatePicker (`Widgets/DatePicker/`)
- `DatePickerWidget` — calendar-based date picker.

### Settings Provider Pattern
Both `GraphWidget` and `ItemTableWidget` implement `ISettingsProvider`. Register via `RegisterSettingsProvider(widget)` in your tool — the base `ToolComponent` auto-renders each provider's `DrawSettings()` under a collapsible header.

---

## Naming & Style

| Element | Convention | Example |
|---------|-----------|---------|
| Services | `*Service` suffix, `sealed class` | `WindowService`, `UniversalisService` |
| Widgets | `*Widget` suffix | `GraphWidget`, `ItemComboWidget` |
| Settings POCOs | `*Settings` suffix | `GeneralSettings`, `DataToolSettings` |
| Private fields | `_camelCase` | `_connection`, `_writeLock` |
| Config partials | `Configuration.*.cs` | `.Enums.cs`, `.Layout.cs`, `.Settings.cs` |
| DB partials | `KaleidoscopeDbService.*.cs` | `.Characters.cs`, `.Prices.cs`, `.Query.cs` |
| Namespaces | Mirror folder structure | `Kaleidoscope.Services`, `Kaleidoscope.Gui.MainWindow.Tools` |
| Tool categories | Subfolder in `Tools/` | `Data/`, `PriceTracking/`, `Status/`, `Help/`, `AutoRetainer/` |

---

## Build & Versioning

- **Debug build**: `pwsh .\scripts\build\debug.ps1`
- **Release build**: `pwsh .\scripts\build\release.ps1`
- **Publish testing**: `pwsh .\scripts\publish\testing.ps1` (auto-increments `testing_*` tag)
- **Publish release**: `pwsh .\scripts\publish\release.ps1` (promotes latest testing tag)
- **dotnet CLI**: `dotnet build Kaleidoscope.sln -c Debug`
- Versions are **always `1.0.0.0` in source** — stamped from git tags at build time. The build script modifies `.csproj`/`Kaleidoscope.json`/`repo.json`, builds, then **reverts** all changes. Never manually change version numbers.
- Build scripts auto-detect `.sln`/`.csproj`/plugin JSON (excluding submodule paths from `.gitmodules`).
- Build lock file (`build.lock`) prevents concurrent builds (300s timeout).
- CI: GitHub Actions on tag push — `testing_*` tags trigger test builds, bare version tags trigger releases. Both download Dalamud staging distrib, build, zip output, create GitHub Release, and update `repo.json`.

---

## External Integrations

| Integration | Service | Mechanism |
|-------------|---------|-----------|
| **Universalis REST API** | `UniversalisService` | Async HTTP for price lookups, world data, marketable items |
| **Universalis WebSocket** | `UniversalisService` | BSON-encoded messages over `wss://universalis.app/api/ws`, auto-reconnect (5s delay), channel subscriptions |
| **AutoRetainer IPC** | `AutoRetainerService` | Dalamud IPC for cross-character retainer/submersible data |
| **FFXIVClientStructs** | `GameStateService` (static) | Direct unsafe game memory access (`InventoryManager*`, `PlayerState*`) |
| **Dalamud services** | `StaticServiceManager` | Injected via DI: `ICommandManager`, `IGameGui`, `IClientState`, `IFramework`, etc. |

---

## Logging

- **Injected**: Use `IPluginLog` for standard service logging.
- **Static**: Use `LogService.Debug(LogCategory, message)` / `.Warning()` / `.Error()` where DI is unavailable.
- **Categories**: `LogCategory` flags enum — Database, Cache, GameState, PriceTracking, Universalis, AutoRetainer, CurrencyTracker, Inventory, Character, Layout, UI, Listings, Config (13 categories).
- **File logging**: Supports main file, split-by-category, split-by-character. Rotation by file size. Configured in developer Logging tab.
- **User-facing**: `IChatGui.PrintError(...)` for in-game error messages.

---

## Testing

- **No unit test framework** — no xUnit/NUnit/MSTest projects exist.
- **In-game testing only** via the **Tests developer tab** (`Kaleidoscope/Gui/ConfigWindow/`): interactive test runner covering DB connection/read/write, AutoRetainer IPC, Universalis API/WebSocket, Cache Service, Tracked Data Registry, Config Service, and 6 phases of cache architecture tests.
- **Developer mode**: Hold **CTRL+ALT** with Config Window focused (temporary) or enable via Profiler tab toggle (persistent). Reveals 5 hidden tabs: Profiler, Tests, Caches, Logging, SQL Query.

---

## Interfaces (`Kaleidoscope/Interfaces/`)

| Interface | Purpose |
|-----------|---------|
| `ICharacterData` | Character selection data — list characters, load data, name lookups |
| `IConfigData` | Read-only config contract: sub-configs, developer mode, save events |
| `ISettingsProvider` | Widget-level settings UI: `SettingsLabel`, `HasSettings`, `DrawSettings()` |
| `ILayoutHost` | Typed contract between `WindowContentContainer` and `MainWindow` — layout persistence, dirty state, interaction, presets |
| `IWindowMode` | UI mode state: fullscreen, edit mode, locked, dragging, resizing + change events |

---

## Key Directories

| Path | Purpose |
|------|---------|
| `Kaleidoscope/Core/` | Plugin entry point, service manager |
| `Kaleidoscope/Services/` | All services (DI auto-discovered) |
| `Kaleidoscope/Gui/MainWindow/Tools/` | Tool components by category |
| `Kaleidoscope/Gui/ConfigWindow/` | Settings UI tabs (incl. hidden developer tabs) |
| `Kaleidoscope/Gui/Widgets/` | Reusable UI widgets (Graph, Table, Combo, Tree, DatePicker, Common) |
| `Kaleidoscope/Gui/Animation/` | Animation framework (AnimationController, Tween, Easing) |
| `Kaleidoscope/Gui/Common/` | Shared utilities (colors, combos, ImGui helpers) |
| `Kaleidoscope/Gui/Helpers/` | Helper classes (AutoRetainer IPC, timed cache, settings import) |
| `Kaleidoscope/Models/` | Data models |
| `Kaleidoscope/Config/` | ConfigService and sub-config POCOs |
| `Kaleidoscope/Interfaces/` | Core abstractions (ICharacterData, IConfigData, etc.) |
| `scripts/build/` | Build scripts (debug.ps1, release.ps1) |
| `scripts/publish/` | Publish scripts (testing.ps1, release.ps1) + version info |
