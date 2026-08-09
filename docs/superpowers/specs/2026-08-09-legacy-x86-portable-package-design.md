# Blind Soldier 2013 x86 Portable Package Design

## Goal

Publish `Blind-Soldier-2013-x86-Portable.zip` beside the existing dual-runtime
portable archive so players using the 2013 x86 game can install Blind Soldier
without extracting Steam 2026 launcher files or any x64 runtime component.
The same x86 archive must support a direct 2013 launch and an unmodified 7th
Heaven/FFNx launch.

## Chosen approach

The x86 archive is a deterministic derivative of the already built and
verified dual-runtime archive. The derivative builder first runs the existing
dual-archive verifier, records the source archive SHA-256, safely extracts it,
and selects only files owned by the x86 runtime. This keeps shared assets and
x86 binaries byte-identical to the dual release and avoids maintaining a
second independent dependency downloader or compiler pipeline.

The alternatives were rejected:

- Adding an architecture mode throughout the existing dual builder would
  duplicate conditional logic across every copy and validation step and make
  the primary release path riskier.
- Zipping the current mod-manager staging folder would be quick, but it would
  not provide the complete direct-plus-7th-Heaven proxy layout, a dedicated
  portable manifest, or an independently verifiable release contract.

## Archive boundary

The x86 archive contains:

```text
version.dll
ff7_en.exe.local/version.dll
ff7.exe.local/version.dll
workingdir/version.dll
workingdir/ff7_en.exe.local/version.dll
workingdir/ff7.exe.local/version.dll
Blind-Soldier/Bootstrap/x86/**
Blind-Soldier/Runtime/dotnet/x86/**
Blind-Soldier/Policy/**
Blind-Soldier/Tools/**
Reloaded-II/Loader/X86/**
Reloaded-II/Mods/ff7.accessibility.reloaded/{common files and x86/**}
Reloaded-II/Mods/reloaded.sharedlib.hooks/{common files and x86/**}
Reloaded-II/{Apps,Plugins,User}/**
Reloaded-II/portable.txt
LICENSES/**
Remove-Amethyst-Registry-Entries.cmd
README-2013-PORTABLE.txt
portable-manifest.json
```

`version.dll` beside `ff7_en.exe` is the direct-launch placement already used
by the released 2013 mod-manager package. The executable-local copies provide
the guarded stock and converted x86 host placements. The `workingdir` copies
support 7th Heaven and FFNx without packaging or editing either project.

The x86 archive must not contain:

- `FFVII_LAUNCHER.exe`, its configuration, or launcher Prism files;
- an x64 bootstrap or private x64 .NET runtime;
- `Reloaded-II/Loader/X64`;
- an x64 Blind Soldier or Shared Hooks subtree;
- the nested `ff7/workingdir` layout used when x86 compatibility files live
  beneath a Steam 2026 installation;
- FFNx, 7th Heaven, graphics-driver, audio-driver, or optional mod files; or
- any game executable, game data, registry installer, or absolute development
  path.

Shared Blind Soldier assets are retained because field navigation, audio
descriptions, sounds, language catalogs, and gameplay metadata are required by
the x86 backend.

## Safety and error handling

The builder refuses a missing, invalid, or version-mismatched dual archive; an
existing destination ZIP or checksum; unsafe ZIP member names; a changed
source archive during extraction; reparse points; missing required x86 files;
or any forbidden x64/launcher path. Failure removes only its uniquely named
temporary staging directory and never changes the source archive or a game
installation.

The archive README tells players to close FFVII and 7th Heaven, extract at the
directory containing `ff7_en.exe` or `workingdir`, and not overwrite an unknown
pre-existing `version.dll`. Blind Soldier does not claim or replace 7th
Heaven's `dinput.dll`, FFNx files, or configuration.

## Manifest and reproducibility

`portable-manifest.json` uses an explicit `legacy-x86` profile, the same Blind
Soldier version as the source dual archive, the source archive SHA-256, and a
Windows-canonical sorted record for every other packaged file. Each record
contains its relative path, length, and SHA-256. ZIP entry timestamps and
attributes are normalized so identical inputs produce byte-identical output.
A standard `.sha256` sidecar is generated for the finished archive.

## Verification and release

Tests must first fail because the x86 builder, verifier, workflow assets, and
release contract do not exist. Passing coverage must prove:

- the exact required direct and 7th Heaven proxy placements;
- total absence of every x64, launcher, nested-2026, FFNx, and 7th Heaven file;
- x86 PE architecture for the bootstrap, Version proxy, private hostfxr,
  Reloaded loader, mod entry point, Shared Hooks, and Prism;
- manifest completeness, ordering, hashes, source binding, and deterministic
  output;
- rejection of an unsafe or modified archive; and
- release workflow creation of both ZIP files and both checksum sidecars.

The existing Ghidra gate remains bound to the dual source archive before the
x86 derivative is produced. The derivative verifier requires its proxy and
x86 bootstrap hashes to equal the Ghidra-verified source entries, proving the
released x86 package contains the analyzed binaries without modification.

For the current multilingual beta, the verified x86 archive is attached as a
second asset to `v0.2.1-beta.1`. Future release workflow runs publish both
portable profiles automatically.
