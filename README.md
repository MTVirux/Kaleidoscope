# Kaleidoscope

Kaleidoscope is a Dalamud plugin for Final Fantasy XIV that provides an overlay for tracking game information across multiple characters.

## Repository structure

- `Kaleidoscope/` - plugin source
  - `Core/KaleidoscopePlugin.cs` - plugin entry point
  - `Gui/` - windows and widgets
  - `Services/` - services, including the SQLite layer in `Services/Database/`
  - `Models/`, `Interfaces/`, `Config/`
- `Kaleidoscope.Tests/` - unit tests
- `OtterGui/`, `FFXIVClientStructs/` - git submodules
- `scripts/build/` - debug and release build scripts
- `scripts/publish/` - testing and release publish scripts

## Building

Clone with submodules, then build with the scripts rather than calling `dotnet build` directly:

```pwsh
git submodule update --init --recursive
pwsh .\scripts\build\debug.ps1     # debug build, reloads the plugin if the game is running
pwsh .\scripts\build\release.ps1   # release build
```

The build scripts hold a repo-wide lock so debug and release builds can't run at the same time.

## Publishing

- `pwsh .\scripts\publish\testing.ps1` - publish a testing build
- `pwsh .\scripts\publish\release.ps1` - publish a release (must be on `main`)

Both require the local branch to be up to date with the remote.

## Conventions

- Don't modify the submodules here.
- Register services in `Kaleidoscope/Services/StaticServiceManager.cs` using `OtterGui.Services.ServiceManager`, and prefer constructor injection.
- Register new windows in `Kaleidoscope/Services/WindowService.cs`.
- Use `IPluginLog` for logging and `IChatGui.PrintError` for user-facing errors.
- Configuration lives in `Kaleidoscope/Configuration*.cs` and `Kaleidoscope/ConfigStatic.cs`.
