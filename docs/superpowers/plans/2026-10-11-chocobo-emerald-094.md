# Chocobo navigation and Emerald diagnostics implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this approved scope in this session. Steps use checkboxes for tracking.

**Goal:** Restore destination pathfinding while mounted on each native chocobo, verify current visible Emerald tracking, and release 0.9.4 for both runtimes.

**Architecture:** Read the mounted bird's native color and terrain-mask table through the shared guest address space. Carry that coherent profile through routing, connected-region caches and automatic steps. Preserve Emerald's renderer-backed visibility boundary and retain its diagnostic in both runtime logs.

**Tech stack:** C#/.NET 8, shared legacy guest readers, Reloaded-II x86 and Steam x64 DirectInput overlay, Ghidra and native world-map data.

**Spec:** User-approved scope in this chat: native terrain abilities including gold-chocobo routes to Knights of the Round and Mime; normal buttons for entry, dismounting and encounters; both runtimes; 0.9.4.

## Global constraints

- No hidden enemy positions, automatic Confirm, dismounting, encounters or firing.
- Preserve player settings, narration history/assets and saves.
- Deploy both playable installations and publish only after complete release and exact-package verification.
- The latest log's underwater visits predate 0.9.2. Do not present them as proof that its packed-camera fix failed.

## Review focus

- Changing color while model 19 stays the same must invalidate cached reachability and active routes.
- Missing, invalid or torn native capability reads must not grant ocean or mountain access.
- Native route shortcuts and automatic steps must obey the same permissions as full paths.
- Gold-chocobo cave arrivals must preserve the game's actual entry model and normal dismount controls.
- Emerald becoming hidden, undrawn or stale must still stop tracking; diagnostics must reset with the runtime.

### Task 1: Native capability and shared routing

**Files:** LegacyLayout/WorldMapStateReader.cs; Reloaded/WorldMapRoutePlanner.cs, WorldMapTargetCatalog.cs, WorldMapVehicleDetourPlanner.cs, WorldMapNavigationController.cs; shared WorldMapChocoboNavigationTests.cs and both test hosts.

**Interfaces:** Snapshot adds HasNativeChocoboTerrainMask, NativeChocoboColor and NativeChocoboTerrainMask. CanTraverse(WorldMapStateSnapshot, int) and GetComponentId(WorldMapStateSnapshot, int) consume the profile. Existing model-only overloads remain for fixed-profile callers.

- [x] Write and run failing native-reader-to-route tests for green mountains, blue shallow water, black combined terrain and gold ocean; reject unsupported terrain, stale profile caches and missing/torn reads.
- [x] Read guest color 0xDE6B54 and table 0x969A40 coherently for model 19, propagate the observed mask through all state-backed passability callers and cache keys, and replan on profile changes.
- [x] Run focused tests in x86 and x64.

### Task 2: Native destinations and Emerald observation

**Files:** shared chocobo and underwater visibility tests; WorldMapRuntimeContext.cs; Mod.cs; Steam2026WorldMapAccessibilityCoordinator.cs; documentation.

- [x] Replay the logged gold bird's position against each installed map and assert the actual Knights/Mime cave entrances are offered and routed; verify controller input, terrain boundaries and normal entry handoff.
- [x] Check cave entry scripts and movement against native evidence; implement only defects reproduced by failing tests.
- [x] Add a failing runtime diagnostic test for visible, hidden, unreadable and reset underwater observations, then retain and log the native visibility result on change in both runtimes.
- [x] Run current Emerald reader-to-Events-to-pursuit regressions and ordinary-route avoidance checks; document the old-log timing accurately.

### Task 3: Review and 0.9.4 delivery

- [ ] Run all release gates and fresh whole-branch review; repair material findings with regressions.
- [ ] Update release contracts, build both packages, and run the four exact-package suites.
- [ ] Back up and deploy both playable installs, verify payload and protected-file hashes.
- [ ] Publish GitHub release and both AMM records using Author CLI 0.30; verify public assets and directory records.
- [ ] Save release evidence and a new handoff. Report any unperformed live gameplay/controller/speech checks.

Execution ledger lives in the private chocobo-emerald-20261011 research folder. Scope approval is already granted; execute without another confirmation.
