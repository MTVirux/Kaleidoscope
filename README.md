# Kaleidoscope

Kaleidoscope is a Dalamud plugin for Final Fantasy XIV that provides an overlay for tracking game information across multiple characters. This repository contains the plugin source, configuration, and helper services used while developing and packaging the plugin.

**Quick Links**

- **Source:** `Kaleidoscope/`
- **Plugin entry:** `Kaleidoscope/Core/KaleidoscopePlugin.cs`
- **Build scripts:** `scripts/build/` and `scripts/publish/`

**What this repo contains**

- The main plugin code in `Kaleidoscope/`
- Build and publish helper scripts in `scripts/`.

**Getting started (development)**

1. Open the solution in Visual Studio or use the provided PowerShell scripts.
2. Build the plugin (debug):

```pwsh
pwsh .\scripts\build\debug.ps1
```

3. The build script will produce a debug package and optionally run post-build packaging tasks. See the scripts for details.

**Packaging / Release**

- Use `pwsh .\scripts\publish\release.ps1` for release packaging.
- The build scripts may temporarily modify project files for auto-versioning; they revert changes after packaging.

**Development notes & conventions**

- Only edit files under `Kaleidoscope/`. Submodules are managed separately and should not be modified here.
- Services should be registered in `Kaleidoscope/Services/StaticServiceManager.cs` using the existing `OtterGui.Services.ServiceManager` patterns.
- Prefer constructor injection for Dalamud services (see `Kaleidoscope/Services/*`).
- Logging: use `IPluginLog` for debug messages, and `IChatGui.PrintError` for user-facing errors.
- Database persistence uses SQLite through `Kaleidoscope/Services/KaleidoscopeDbService.cs`.

**Repository structure (high-level)**

- `Kaleidoscope/` — Main plugin source and config.
- `CriticalCommonLib/`, `OtterGui/`, `ECommons/`, `FFXIVClientStructs/` — shared submodules and libraries.
- `scripts/` — build and publish scripts.

**Useful commands**

- Build debug package: `pwsh .\scripts\build\debug.ps1`
- Build (dotnet): `dotnet build Kaleidoscope.sln -c Debug`
- Package release: `pwsh .\scripts\publish\release.ps1`

**Where to edit / add things**

- Add UI windows under `Kaleidoscope/Gui/` and register them via `Kaleidoscope/Services/WindowService.cs`.
- Add new services in `Kaleidoscope/Services/` and register in `StaticServiceManager.cs`.
- Configuration is in `Kaleidoscope/Configuration.cs` and `Kaleidoscope/ConfigStatic.cs`.

If you'd like, I can also:
- Run the debug build to verify the package output.
- Add short CONTRIBUTING notes or a development checklist to this README.

---
Generated / updated by the project maintainer's local tooling.
