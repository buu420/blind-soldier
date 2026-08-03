# Sector 6 Through World Map Accessibility Implementation Plan

> **For agentic workers:** Execute this plan inline. Do not create subagents
> unless the user explicitly requests delegation.

**Goal:** Provide complete, native-state-backed story and object coverage from
the post-pillar Sector 6 scene through the motorcycle escape and first
world-map control, while fixing stacked-walkmesh navigation.

**Architecture:** Extend the shared data-driven field catalogs and the native
walkmesh pathfinder. Story and object availability comes from native script
state; routing keeps native triangle/layer continuity and only smooths within
verified corridors.

**Tech Stack:** C# 12, .NET 8 Windows, JSON navigation catalogs, FFVII field
scripts/walkmeshes, Ghidra 12.0.4, PowerShell.

## Constraints

- Both supported runtimes consume the same story/object/navigation semantics.
- No OCR, input guessing, walkthrough-only coordinates, or always-visible
  synthetic targets.
- Do not launch FFVII; the user performs live gameplay validation.
- Preserve installed user configuration during deployment.
- The source directory has no Git metadata, so commit steps are omitted.

### Task 1: Freeze guide and native coverage

**Files:**
- Modify: `docs/superpowers/specs/2026-07-28-sector6-through-world-map-accessibility-design.md`
- Create: `docs/research/sector6-through-world-map-native-coverage.md`

- [ ] Read the walkthrough from the plate-collapse aftermath through first
      world-map control and enumerate every required and guide-listed optional
      interaction.
- [ ] Resolve the involved field IDs/names from maplist and native transition
      scripts.
- [ ] For each checklist row, record the native entity, model or trigger,
      coordinates, availability bank/bit or game-moment range, completion
      condition, and current catalog status.
- [ ] Mark unresolved rows as failures; do not substitute guessed coordinates.

### Task 2: Reproduce the Sector 6 route failure

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Add a regression using field 191's installed walkmesh, start triangle
      209, target triangle 34, and the captured post-collapse trace.
- [ ] Assert that the route retains a reachable lower-layer transition before
      any elevated continuation.
- [ ] Assert that the captured ground-level movement cannot be described as
      progress toward the elevated shortcut.
- [ ] Run the test and verify RED against the current planar-funnel behavior.

### Task 3: Preserve native layer continuity

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/FieldWalkmeshNavigation.cs`
- Modify: `Ff7.Accessibility.Reloaded/FieldNavigationRouteTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Segment funnel smoothing at elevation-transition portals.
- [ ] Emit mandatory transition gates in native route order.
- [ ] Prevent lookahead from skipping mandatory gates.
- [ ] Make obstacle recovery depend on corridor progress rather than repeated
      direction text.
- [ ] Run the new Sector 6 regression and existing pillar, tunnel, Sector 6,
      Aeris-house, and route-tracker tests until GREEN.

### Task 4: Add the immediate post-collapse party-join beat

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Assets/navigation/field_story_events.json`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Dump field `mds6_1` scripts and identify the Barret/Tifa catch-up trigger,
      exact game moment, and completion write.
- [ ] Add a state-bounded target for the join event before the long Sector 6
      route becomes available.
- [ ] Add tests proving moment 247 never exposes the later exit route and that
      the target advances after the party joins.

### Task 5: Complete story coverage through the world map

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Assets/navigation/field_story_events.json`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Reconcile every guide checklist row with native scripts.
- [ ] Add missing state-bounded story targets for Sector 5/6, Aeris's house,
      Wall Market return, plate climb, Shinra floors 1 through 70, prison and
      blood trail, rooftop/escape, motorcycle, and first world-map control.
- [ ] Add sequence tests for multi-stage puzzles and party transitions.
- [ ] Assert that no audited field reports `Story: none for this field yet`
      while a required native event is actionable.

### Task 6: Complete objects and interactables

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Assets/navigation/field_objects.json`
- Modify relevant native NPC/traversal catalog source only where extraction
  cannot represent the native interaction.
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

- [ ] Add or correct all pickups, key items, sockets, doors, computers, ducts,
      statues, files, lockers, model slots, vending machine, save points,
      elevators, party controls, and minigame objectives in the coverage
      matrix.
- [ ] Add state predicates so consumed or unavailable controls disappear.
- [ ] Add completeness tests keyed by stable native identity.

### Task 7: Verify both runtimes

- [ ] Run the Reloaded test executable in Release.
- [ ] Run `Run-DualRuntimeVerification.ps1 -Mode Research
      -SkipProtectedSeventhHeaven -SkipSecondPesterRepeat`.
- [ ] Inspect failures and fix regressions; do not weaken assertions.

### Task 8: Deploy

- [ ] Run `Install-FF7ReloadedMod.ps1` against
      `X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition` with
      the research-native profile, preserving config.
- [ ] Validate the installed dual-runtime package and PE architectures.
- [ ] Report automated evidence, installed backup path, and the shortest live
      validation route for the user.
