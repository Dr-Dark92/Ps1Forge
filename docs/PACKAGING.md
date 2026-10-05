# PS4 packaging backend

Ps1Forge's PS4 backend follows the documented PKGForge/LibOrbis-style pipeline:

1. Resolve the selected PS1 disc.
2. Normalize it to a package-local disc image.
3. Generate PS1HD runtime configuration.
4. Add user-supplied PS1HD runtime files.
5. Generate PS4 metadata/artwork.
6. Build inner PFS.
7. Wrap inner PFS in PFSC.
8. Build signed/encrypted outer PFS.
9. Assemble the debug/fake PKG.

## Runtime assets

Ps1Forge does not redistribute Sony runtime files.

Place user-supplied runtime files under:

```
runtime/ps1hd/
  eboot.bin
  sce_module/
    libc.prx
    libSceFios2.prx
    libSceNpToolkit2.prx
```

The application validates these before package construction.

The native PKG/PFS writer is being implemented independently from the UI so the
front-end remains the three-action workflow: game, image, convert.
