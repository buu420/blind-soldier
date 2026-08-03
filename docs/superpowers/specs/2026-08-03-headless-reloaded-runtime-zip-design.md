# Blind Soldier Portable Native Installer ZIP Design

## Goal

Publish a direct-extract Blind Soldier prerelease ZIP that preserves the small
native installer and launcher behavior supplied in `stuff.zip`, replaces its
DSTS-specific identities with Blind Soldier and Final Fantasy VII, and ships
only the Reloaded-II files that are executed by the game.

The existing `v0.1.0-pre.6` WinForms setup and update channel remain unchanged.
This ZIP is a separate portable installation path.

## Preserved native behavior

The supplied installer is not a copying installer. The player extracts the ZIP
into the Final Fantasy VII Steam installation directory and runs the native
installer. The installer:

1. requests administrator elevation;
2. validates the adjacent launcher, Reloaded loader, Shared Hooks, and Blind
   Soldier files;
3. offers a standard Windows Yes/No confirmation;
4. writes an Image File Execution Options `Debugger` registration so ordinary
   game launches are redirected through the adjacent native launcher; and
5. supports `/uninstall`, which removes that registration but leaves files.

The launcher behavior also remains the same. It receives the real game command
line from IFEO, creates the game suspended with `DEBUG_ONLY_THIS_PROCESS` to
bypass recursive IFEO interception, temporarily points Reloaded at the portable
tree, injects the matching Reloaded bootstrapper, resumes the game, waits for
exit, restores the prior Reloaded pointer, and logs every step. If required mod
files are missing, its existing fail-safe launches the game without the mod.

## Required dual-runtime adaptation

Remote DLL injection must use the same process architecture as the target.
Therefore the package contains:

- `Blind-Soldier-Launcher-x86.exe` for `ff7_en.exe`;
- `Blind-Soldier-Launcher-x64.exe` for `FFVII.exe`; and
- one x64 `Blind-Soldier-Installer.exe` that registers whichever supported
  executable is present, or both when both are present.

The installer accepts the two layouts currently used by supported Steam
installs: an x86 `ff7_en.exe` beside the installer or under
`ff7\workingdir`, and an x64 `FFVII.exe` beside the installer. This is an
architecture extension of the supplied workflow, not a replacement launch
mechanism.

Each launcher writes an application configuration that enables, in dependency
order, `reloaded.sharedlib.hooks` and `ff7.accessibility.reloaded`.

## Direct-extract package layout

```text
Blind-Soldier-Installer.exe
Blind-Soldier-Launcher-x86.exe
Blind-Soldier-Launcher-x64.exe
FFVII_LAUNCHER.exe
FFVII_LAUNCHER.exe.config
launcher_accessibility/
  native/x86/FFVII_LAUNCHER.prism.x86.dll
Reloaded-II/
  Loader/X86/...
  Loader/X64/...
  Mods/ff7.accessibility.reloaded/...
  Mods/reloaded.sharedlib.hooks/...
LICENSES/
README-PORTABLE.txt
```

The accessible FFVII launcher files are included because they are part of the
Blind Soldier release. As in the supplied installer model, extraction performs
all file placement; the native installer only validates and registers the
launch redirect.

## Minimal Reloaded closure

For each of `Loader/X86` and `Loader/X64`, the ZIP contains exactly:

- `Bootstrapper/Reloaded.Mod.Loader.Bootstrapper.dll`
- `Colorful.Console.dll`
- `DelayInjectHooks.json`
- `Indieteur.SAMAPI.dll`
- `Indieteur.VDFAPI.dll`
- `McMaster.NETCore.Plugins.dll`
- `Reloaded.Memory.dll`
- `Reloaded.Mod.Interfaces.dll`
- `Reloaded.Mod.Loader.deps.json`
- `Reloaded.Mod.Loader.dll`
- `Reloaded.Mod.Loader.IO.dll`
- `Reloaded.Mod.Loader.runtimeconfig.json`

No Reloaded manager executable, WPF UI, updater, languages, themes, package
browser, NuGet tooling, server tooling, PDB, object, or incremental build files
are published. ASI loaders are also unnecessary in this portable ZIP because
the preserved native launcher injects the bootstrapper directly.

Shared Hooks remains mandatory because Blind Soldier depends on it. Both the
x86 and x64 Blind Soldier ReadyToRun payloads, Prism libraries, audio
description, footsteps, navigation data, and other accessibility assets remain
present.

Reloaded-II 1.30.3 requires the Microsoft .NET 9.0.8 Desktop Runtime for the
matching game architecture. The native installer supplied in `stuff.zip` does
not install dependencies, so preserving its behavior means the portable ZIP
checks and documents that prerequisite rather than silently changing its
installation model. The standard pre.6 setup remains the game-only dependency
path that installs missing runtimes automatically.

## Build and validation

The build downloads and hash-verifies the pinned official Reloaded-II and
Shared Hooks archives, extracts them only into private staging, and copies an
exact allowlist. It builds the native installer and both launchers with the
Visual C++ v143 static runtime, then validates:

- installer and launcher PE architectures;
- exact loader closure for both architectures;
- complete `.deps.json` resolution;
- both Blind Soldier entry assemblies and their native Prism dependencies;
- complete Shared Hooks x86/x64 payloads;
- accessible FFVII launcher identity and Prism file;
- absence of Reloaded manager/build debris; and
- deterministic file hashes inside the final ZIP.

Ghidra is used to confirm the final native binaries retain the intended IFEO,
suspended-process, remote `LoadLibraryW`, and resume flow.

## Release contract

The prerelease publishes only:

- `Blind-Soldier-Portable.zip`
- `Blind-Soldier-Portable.zip.sha256`

It does not replace `Blind-Soldier-Setup.exe` or publish a new channel manifest.

## Acceptance criteria

- Extracting the ZIP at a supported Steam root places every file where the
  preserved native installer expects it.
- The installer succeeds when only x86 is present, only x64 is present, or both
  are present; it never requires both games.
- Installation and `/uninstall` retain the supplied confirmation, IFEO, file
  retention, logging, and dialog behavior.
- The x86 launcher injects only the x86 bootstrapper and the x64 launcher
  injects only the x64 bootstrapper.
- App configuration enables Shared Hooks before Blind Soldier.
- The final Reloaded loader tree contains exactly 24 files, twelve per
  architecture, and contains no manager or ASI loader.
- The complete dual-runtime mod, Shared Hooks, accessible launcher, Prism, and
  accessibility assets are present.
- The final public ZIP downloads successfully and matches its SHA-256 sidecar.
