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

Ps1Forge now has an end-to-end native PS4 fPKG pipeline for CUE/BIN PS1 dumps:

- WinForms `Input → Process → Output` GUI
- CUE/BIN parsing, validation and multi-BIN normalization
- PS1 serial/region detection
- PS1 TOC and PS1HD configuration generation
- start-screen artwork conversion
- user-supplied PS1HD runtime and BIOS staging
- native unsigned inner PFS generation
- PFSC wrapping
- signed/encrypted outer PFS generation
- native PS4 PKG assembly and validation
- Windows CI covering the complete synthetic CUE/BIN → validated PKG path

The first compatibility target is CUE/BIN. ISO/IMG selection is visible in the UI, but conversion is intentionally rejected until sector-layout conversion is implemented and tested.

## Local runtime layout

Ps1Forge does not ship Sony runtime, BIOS, or proprietary publishing assets. Place runtime files you are legally entitled to use beside the executable:

```text
Ps1Forge/
├── Ps1Forge.exe
├── runtime/
│   ├── eboot.bin
│   ├── sce_module/
│   │   ├── libc.prx
│   │   ├── libSceFios2.prx
│   │   └── libSceNpToolkit2.prx
│   └── bios/
│       └── <runtime BIOS files>
└── keys/
    ├── pkg_public_0.bin
    ├── pkg_public_1.bin
    ├── pkg_public_3.bin
    └── fake_keyset_modulus.bin
```

`runtime/` and `keys/` are local-only and ignored by Git.
