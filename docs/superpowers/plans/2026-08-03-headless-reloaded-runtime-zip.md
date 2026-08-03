# Blind Soldier Portable Native Installer ZIP Implementation Plan

**Goal:** Publish a direct-extract ZIP that preserves the supplied native
installer/IFEO launcher workflow while adapting it to Blind Soldier's x86 and
x64 FFVII runtimes and a dependency-closed Reloaded tree.

**Architecture:** A native x64 installer registers architecture-matched x86 and
x64 native launchers for the FFVII executable names found in the extraction
root. Each launcher performs the supplied suspended-start bootstrapper injection
flow against a portable `Reloaded-II` tree. A deterministic PowerShell builder
assembles the native executables, dual-runtime mod, Shared Hooks, accessible
launcher, licenses, and exact loader allowlist into one ZIP.

**Tech stack:** C++20/Win32, Visual C++ v143 static runtime, Windows PowerShell
5.1, Pester 3.4, .NET 8/9 build tooling, Reloaded-II 1.30.3, Shared Hooks
1.16.3, Ghidra 12, GitHub CLI.

## Constraints

- Preserve the supplied install/uninstall and launcher behavior.
- Support x86-only, x64-only, and combined Steam layouts without requiring both.
- Enable Shared Hooks before Blind Soldier.
- Do not ship Reloaded's manager, UI, updater, package tooling, ASI loaders, or
  native build debris.
- Keep the current WinForms installer and update channel on v0.1.0-pre.6.
- Demonstrate every production behavior change with a failing test first.

## Task 1: Native installer and dual-architecture launcher

**Create:**

- `native/BlindSoldier.Common/common.h`
- `native/BlindSoldier.Installer/installer.cpp`
- `native/BlindSoldier.Installer/BlindSoldier.Installer.vcxproj`
- `native/BlindSoldier.Launcher/launcher.cpp`
- `native/BlindSoldier.Launcher/BlindSoldier.Launcher.vcxproj`
- `native/BlindSoldier.Native.Tests.ps1`

- [x] Add failing tests for Blind Soldier identity, both FFVII executable
  mappings, architecture-specific bootstrapper selection, Shared Hooks ordering,
  install/uninstall switches, and output PE machines.
- [x] Run the native test suite and observe missing-project failures.
- [x] Adapt the supplied common, installer, and launcher source without changing
  the interaction or IFEO injection workflow.
- [x] Build installer x64 and launchers Win32/x64; rerun tests to green.

## Task 2: Exact portable package builder

**Create/modify:**

- `Build-BlindSoldierPortablePackage.ps1`
- `Build-BlindSoldierPortablePackage.Tests.ps1`
- `Build-BlindSwordsmanPrerequisiteBundle.ps1`
- `Build-BlindSwordsmanPrerequisiteBundle.Tests.ps1`

- [x] Add a failing fixture that contains loader UI/manager/build debris and
  asserts an exact twelve-file closure per architecture.
- [x] Add a failing portable-layout test for the three native executables,
  loader closure, both mods, accessible launcher, licenses, and forbidden files.
- [x] Change prerequisite staging to publish only the dependency-closed loader
  allowlist while retaining Shared Hooks, .NET provenance, and notices for the
  standard installer pipeline.
- [x] Implement deterministic direct-extract staging and ZIP generation. The
  portable output omits ASI loaders and offline runtime installers because its
  preserved native launcher performs injection and its preserved installer does
  not install dependencies.
- [x] Rerun both suites to green.

## Task 3: Consumer compatibility and release validation

**Modify:**

- `ReloadedPrerequisiteInstall.psm1`
- `ReloadedPrerequisiteInstall.Tests.ps1`
- `Invoke-BlindSwordsmanPreflight.ps1`
- `Invoke-BlindSwordsmanPreflight.Tests.ps1`
- `Install-FF7ReloadedMod.ps1`
- `InstallerEntrypoint.Tests.ps1`
- `installer/BlindSwordsman.Setup.Core/ReleasePayloadLayoutValidator.cs`
- `installer/BlindSwordsman.Setup.Tests/ArtifactSecurityTests.cs`
- relevant release fixture scripts

- [x] Add failing tests for a fresh headless Reloaded root and preservation of an
  existing full manager installation.
- [x] Remove the manager executable requirement from bundle validation and
  discover roots from loader paths as well as an existing manager path.
- [x] Write an empty `LauncherPath` for a fresh headless root while preserving a
  valid existing manager path.
- [x] Rerun the PowerShell and C# installer suites to green.

## Task 4: Documentation, full verification, and release

**Create/modify:**

- `docs/releases/v0.1.0-pre.7.md`
- `installer-dependencies/THIRD-PARTY-NOTICES.md`
- `README.md` only if a portable download note is needed

- [x] Document extraction, UAC/IFEO behavior, `/uninstall`, supported layouts,
  the .NET 9 Desktop Runtime prerequisite, and the standard setup alternative.
- [x] Analyze final native binaries in Ghidra and confirm IFEO registration,
  `CREATE_SUSPENDED | DEBUG_ONLY_THIS_PROCESS`, remote `LoadLibraryW`, and
  `ResumeThread` behavior.
- [x] Harden the final native review findings: serialize and atomically restore
  Reloaded's pointer, fail closed on configuration errors, resolve the remote
  injection address, check architecture-matched .NET runtimes, and make IFEO
  install/uninstall ownership-aware.
- [x] Exercise pointer write/backup/restore failures, concurrent launch swaps,
  runtime compatibility, IFEO conflicts, and ownership-aware uninstall in
  compiled native behavior tests.
- [x] Run all targeted suites and `Run-DualRuntimeVerification.ps1 -Mode Research`.
- [x] Build the portable ZIP, independently inspect every member and hash, and
  run `git diff --check`.
- [ ] Commit and push the verified changes.
- [ ] Create prerelease `v0.1.0-pre.7` with only the portable ZIP and checksum,
  download both to a fresh directory, and verify the public checksum.
