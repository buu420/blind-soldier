# Midgar Zolom Accessibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restore marsh footsteps and add a native-state, sight-equivalent notification when the Midgar Zolom reaches the far side of the marsh.

**Architecture:** Extend the shared Cosmo world-surface table, read the stock legacy Zolom position ring through `ILegacyAddressSpace`, determine shoreline proximity from the native world mesh, and drive one shared notification tracker from both runtime hosts.

**Tech Stack:** C#/.NET 8, Reloaded-II x86, Steam 2026 x64 guest translation, Prism, Cosmo footsteps, Ghidra.

### Task 1: Lock behavior with regressions

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/WorldMapFootstepTests.cs`
- Add: `Ff7.Accessibility.Reloaded.Tests/MidgarZolomStateReaderTests.cs`
- Add: `Ff7.Accessibility.Reloaded.Tests/MidgarZolomCrossingTrackerTests.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Assert the shipped Cosmo configuration returns sound 5130 for terrain 7 on every supported walking/Chocobo model.
- [ ] Assert the native reader translates a coherent active ring record and rejects invalid or torn data.
- [ ] Assert the tracker announces once at either native far-side anchor only at a marsh shoreline and while on foot.
- [ ] Run `--world-map-only` and confirm the new tests fail before production changes.

### Task 2: Implement shared behavior

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Assets/footsteps/cosmo/config.toml`
- Add: `Ff7.Accessibility.LegacyLayout/MidgarZolomStateReader.cs`
- Add: `Ff7.Accessibility.Reloaded/WorldMapTerrainProximity.cs`
- Add: `Ff7.Accessibility.Reloaded/MidgarZolomCrossingTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/WorldMapRuntimeContext.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Ff7.Accessibility.Steam2026X64.csproj`

- [ ] Add exact terrain-7 sound mappings.
- [ ] Implement coherent, fail-closed native reads.
- [ ] Implement native-mesh shoreline resolution and rising-edge cue tracking.
- [ ] Link the shared tracker/proximity sources into the x64 project.
- [ ] Run the focused suite and confirm it passes.

### Task 3: Integrate both runtime hosts

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/World/Steam2026WorldMapAccessibilityCoordinator.cs`

- [ ] Initialize one native reader per runtime over its existing address space.
- [ ] Observe the Zolom on every usable foreground overworld frame, independent of whether navigation is enabled.
- [ ] Speak the shared cue through Prism and log the exact native state that triggered it.
- [ ] Reset the tracker across lifecycle, battle, map, focus, and recovery boundaries.

### Task 4: Verify and deploy

- [ ] Remove the temporary world-map research dumper and its command-line switch.
- [ ] Run focused world-map, complete x86, and complete x64 test suites.
- [ ] Build both runtime projects in Release configuration and run repository validation checks.
- [ ] Back up the current installed Blind Soldier mod.
- [ ] Deploy the verified x86 and x64 outputs to `C:\Games\Final Fantasy VII` without changing configuration or unrelated mods.
- [ ] Verify deployed file hashes and architecture-specific assemblies.
