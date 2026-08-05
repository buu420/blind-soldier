# Runtime Startup and Description Ownership Design

## Problem

Two post-release failures need defensive fixes:

1. Steam 2026 x64 can exit back to the launcher before Blind Soldier starts. The Windows event log identifies a `Collection was modified` exception in Reloaded-II 1.30.3 `DelayInjector.Dispose`. Reloaded installs a broad set of active hooks while the injected initialization thread is still constructing the hook list; a D3D11 worker call can enter cleanup before construction finishes.
2. Players report audio descriptions playing twice after updating. The released narration code and assets did not change between 0.1 and 0.1.1, and a fresh local install loads one mod instance. The safe release boundary is therefore to guarantee that only one Blind Soldier runtime instance owns speech and audio playback in each game process, while retaining diagnostics for any duplication originating outside Blind Soldier.

## Considered Approaches

### Patch Reloaded-II's managed loader

Adding construction synchronization and a one-shot gate to `DelayInjector` would fix the upstream race generally. It would also make Blind Soldier maintain a forked GPL loader binary and expand the patch surface beyond FFVII.

### Mark the suspended-process injection as externally loaded

This bypasses delayed initialization. It risks starting signature scans before Steam's wrapper has transferred control to the unpacked game, which could turn a visible startup crash into silent accessibility failure.

### Specialize the x64 delayed hook list (selected)

Ghidra confirms that the supported x64 `FFVII.exe` imports `D3D11CreateDevice` and does not directly import the D3DKMT vertical-blank function. A live launch proved that the launcher resumes the game before it injects Reloaded, so `D3D11CreateDevice` has already occurred by the time the hook is installed and cannot trigger the delayed mod load. Successful pre-fix logs show that the later `D3DKMTWaitForVerticalBlankEvent` callback reliably triggers after injection. Blind Soldier therefore installs only that proven later callback. One hook preserves Reloaded's Steam-DRM delayed initialization while eliminating the concurrent mutation of its large hook collection. The x86 hook list remains unchanged.

## Description Ownership

A process-scoped named semaphore will represent ownership of the Blind Soldier runtime. Both entry assemblies acquire it before starting speech, audio, timers, or hooks and hold it until unload. A second copy in another assembly load context will remain inert and log that ownership was denied. A semaphore is used because Reloaded may unload on a different managed thread, while mutex release is thread-affine. This prevents duplicate descriptions, footsteps, menu speech, and hooks when an update or third-party loader exposes the same mod twice.

The fix will not suppress an external mod's audio based on timing guesses. If duplication remains while logs prove one Blind Soldier owner, that evidence will identify a separate Echo-S or FFNx playback conflict without risking lost descriptions.

## Packaging and Compatibility

- Add a reviewed x64 `DelayInjectHooks.json` override containing only `d3d11!D3DKMTWaitForVerticalBlankEvent`.
- Make the portable/package builder overwrite only the x64 loader copy with that file.
- Preserve the official Reloaded-II 1.30.3 binaries and the complete x86 delayed-hook configuration.
- Include the same x64 override in both portable and Accessibility Mod Manager release payloads.

## Verification

- A package test must fail while the x64 archive still contains the broad official hook list, then pass only when the x64 output contains the single supported entry and x86 remains intact.
- A runtime-lease test must prove that a second acquisition for one process is rejected and that ownership can be reacquired after clean disposal.
- Build and run all affected .NET and package tests.
- Deploy to the installed Steam 2026 game, launch repeatedly, and verify a non-empty Reloaded log, exactly one Blind Soldier initialization, and no new CLR crash event.
