# Bundled FFNx for 7th Heaven Design

## Goal

Make the Blind Soldier direct-extract release self-contained for the supported 7th Heaven layout, without changing or overwriting the native Steam 2026 runtime.

## Packaging boundary

- Pin the stable FFNx Steam release at version 1.24.3.0 rather than downloading an unversioned latest build during release creation.
- Verify the official release archive by its published size and SHA-256 digest before extraction.
- Copy the FFNx runtime to `ff7/workingdir`, the location documented by FFNx for a converted Steam 2026 installation and used by 7th Heaven.
- Do not place the Steam FFNx payload at the package root. That root is the native Steam 2026 installation and contains its own runtime assets.
- Omit `FFNx.pdb`; it is a 133 MB debugging-symbol file and is not required by FFNx or by 7th Heaven's installed-driver check.
- Preserve the remaining official files, including `AF3DN.P`, `AF4DN.P`, `FFNx.toml`, `steam_api.dll`, shaders, configuration files, and the GPL license.
- Record the FFNx source, version, archive hash, and source-code URL in the prerequisite manifest and include the license in `LICENSES`.

## Runtime compatibility repair

The x86 WinMM proxy currently creates `Local\\BlindSoldier-Ready-<guid>`, while the x86 bootstrap accepts only `Local\\BlindSoldier.Ready.<guid>`. Both components will use one shared constant so the event contract cannot drift again. This repair is required for Blind Soldier to attach after FFNx starts.

## Verification

- A prerequisite-builder test will prove the pinned FFNx archive is verified, safely extracted, stripped only of `FFNx.pdb`, and represented in its manifest.
- A portable-package test will prove the FFNx files occur under `ff7/workingdir`, not at the package root.
- A native proxy test will prove its generated ready-event name uses the bootstrap-compatible prefix.
- The full builders and verifiers will run against the real pinned dependency.
- A local launch check will confirm FFNx starts and the x86 broker reaches its ready signal. A 7th Heaven launch check will then validate the combined path.

## Distribution

Publish an updated Blind Soldier portable ZIP and checksum. Existing 2013 installations using current 7th Heaven remain supported because 7th Heaven manages FFNx beside the selected x86 executable; a future dedicated 2013-only ZIP can reuse the verified FFNx bundle at its archive root without contaminating the native 2026 layout.
