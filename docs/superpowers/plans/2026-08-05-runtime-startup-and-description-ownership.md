# Runtime Startup and Description Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the intermittent x64 launcher bounce and prevent more than one Blind Soldier runtime from producing descriptions in a game process.

**Architecture:** Retain Reloaded-II's delayed Steam initialization but replace its x64 mass-hook configuration with the one later D3D11 callback proven to fire after launcher injection. Add a named-semaphore runtime lease shared by the x86 and x64 entry assemblies so duplicate loads fail closed before any accessibility output begins.

**Tech Stack:** PowerShell/Pester-style package tests, C#/.NET 9, Reloaded-II 1.30.3, named Windows semaphores through `System.Threading.Semaphore`, Ghidra evidence from the supported x64 executable, and live Reloaded launch logs.

## Global Constraints

- Do not alter the x86 delayed-injection hook list.
- Do not bypass Steam delayed initialization.
- A denied runtime lease must produce no speech, audio playback, monitor thread, or game hook.
- Preserve current FFNx and Echo-S narration behavior unless single-owner logs prove an external conflict.

---

### Task 1: Deterministic x64 delayed-injection configuration

**Files:**
- Create: `installer-assets/reloaded/x64/DelayInjectHooks.json`
- Modify: `Build-BlindSoldierPortablePackage.ps1`
- Test: `Build-BlindSoldierPortablePackage.Tests.ps1`

**Interfaces:**
- Consumes: the official prerequisite bundle under `reloaded/Loader/X64`.
- Produces: an x64 package whose delayed-injection list is exactly `d3d11!D3DKMTWaitForVerticalBlankEvent`.

- [x] Add a package behavior test that builds a fixture and opens the resulting ZIP.
- [x] Assert the x64 JSON has one DLL entry and one function, while the x86 JSON retains the fixture content.
- [x] Run the focused test and confirm it fails because the package still copies the broad prerequisite file.
- [x] Add the x64 JSON asset and overwrite the staged x64 loader configuration after prerequisite copying.
- [x] Run the focused and complete portable-package tests and confirm they pass.

### Task 2: Process-scoped runtime ownership

**Files:**
- Create: `Ff7.Accessibility.Reloaded/BlindSoldierRuntimeLease.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Mod.cs`
- Test: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

**Interfaces:**
- Produces: `BlindSoldierRuntimeLease.TryAcquire(int processId, out BlindSoldierRuntimeLease? lease)` and `Dispose()`.
- Consumes: the lease in both Reloaded entry points before runtime initialization.

- [x] Add a test that acquires one unique process-keyed lease, rejects a concurrent second lease, disposes the first, and then reacquires ownership.
- [x] Run the test and confirm it fails because the lease type does not exist.
- [x] Implement the minimal named-semaphore lease.
- [x] Gate both entry assemblies before any accessibility subsystem starts and release the lease on unload or failed startup.
- [x] Run all affected .NET tests and confirm they pass.

### Task 3: Deployment and release verification

**Files:**
- Modify: release staging payloads generated from the repository.
- Modify: installed `Reloaded-II/Loader/X64/DelayInjectHooks.json` and Blind Soldier assemblies through deployment, not hand editing.

**Interfaces:**
- Consumes: Task 1 package output and Task 2 built assemblies.
- Produces: an installed and published patch package.

- [x] Build the dual-runtime mod payload and x64/x86 manager packages with the next patch version.
- [x] Install the x64 payload into the detected Steam 2026 folder.
- [x] Launch the game at least three times and verify Reloaded selects `d3d11, D3DKMTWaitForVerticalBlankEvent`, Blind Soldier initializes once, and no CLR exception is recorded.
- [ ] Validate archive contents and checksums, push the source change, publish the GitHub release, and update the Accessibility Mod Manager entry.
