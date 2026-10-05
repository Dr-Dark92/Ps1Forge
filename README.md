# Ps1Forge

Ps1Forge is a deliberately simple Windows desktop tool for preparing a PlayStation 1 disc dump for a PS4 package workflow.

## UX

1. Select a PS1 disc image (.iso, .img, .cue, .bin)
2. Select a start-screen image (.png, .jpg, .jpeg)
3. Click Convert

Ps1Forge validates the input, resolves CUE track files, extracts the PS1 boot serial when possible, prepares artwork, stages package content, and hands the staged content to a packaging backend.

> Ps1Forge does not ship Sony emulator/runtime files or proprietary publishing tools. Users must supply any runtime they are legally entitled to use.

## Build

Requires .NET 8 SDK on Windows.

```powershell
dotnet build .\src\Ps1Forge.App\Ps1Forge.App.csproj -c Release
```

## Current status

v0.1 foundation:
- WinForms GUI
- ISO / IMG / BIN / CUE selection
- CUE parsing and referenced-track validation
- PS1 serial detection from SYSTEM.CNF data patterns
- start-image preview and PNG conversion
- deterministic staging directory
- packaging backend interface
- dry-run manifest backend for pipeline testing

The final PS4 PKG writer/backend is intentionally isolated behind `IPackageBackend` so it can be implemented and tested independently.
