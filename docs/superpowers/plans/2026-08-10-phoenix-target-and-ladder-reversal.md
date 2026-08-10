# Phoenix Target Recovery and Ladder Reversal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep field navigation useful after a collected object disappears and prevent an active route from directing Cloud to finish a ladder traversal at the wrong endpoint before reversing him.

**Architecture:** Preserve the existing native target and ladder readers. In `FieldNavigationController`, recover an empty selected category to the first useful live category after a target vanishes, and recognize when a route planned from a mounted ladder's projected landing immediately traverses that same ladder in the opposite direction. In that second case, the route action owns the expected landing and required input while Cloud remains mounted.

**Tech Stack:** C# 12, .NET 8, Reloaded-II x86 shared field-navigation controller, Steam 2026 x64 shared deployment assets, native FFVII field state, Ghidra 12.1.2.

## Global Constraints

- Use native field targets, walkmesh transitions, and ladder state; do not add OCR or guessed coordinates.
- Preserve one shared field-navigation behavior for the legacy x86 host and native x64 host.
- Do not modify 7th Heaven or FFNx.
- Keep the installed user's settings and unrelated `.artifacts/` files untouched.

---

### Task 1: Recover navigation selection after collecting an object

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/FieldNavigationAssistant.cs`
- Test: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

**Interfaces:**
- Consumes: `FieldNavigationTargetSource.GetTargets(FieldPositionSnapshot, FieldNavigationCategory)`
- Produces: `FieldNavigationController.CurrentCategory` pointing at a live Story, Exit, or NPC category after the active category becomes empty.

- [x] **Step 1: Write the failing regression**

  Extend the live-object disappearance test with a real live Story target and assert that after the Phoenix Down target disappears, `CurrentCategory` becomes `Story` and `RepeatTarget` reports the story target instead of `Objects: none`.

- [x] **Step 2: Run the Reloaded test harness and verify RED**

  Run: `dotnet run --project Ff7.Accessibility.Reloaded.Tests/Ff7.Accessibility.Reloaded.Tests.csproj -c Release`

  Expected: FAIL because the controller currently leaves `CurrentCategory` on the now-empty Objects category.

- [x] **Step 3: Implement the minimal category recovery**

  After an unavailable target turns navigation off, retain the current category when it still has targets. Otherwise choose the first nonempty category in this order: Story, Exits, NPCs. Do not automatically start another route.

- [x] **Step 4: Run the focused harness and verify GREEN**

  Run the same Reloaded test command and require exit code 0.

### Task 2: Correct a route that starts against an active ladder traversal

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/FieldNavigationAssistant.cs`
- Test: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

**Interfaces:**
- Consumes: `FieldNavigationRouteGuidance.NextAction`, `FieldLadderStateSnapshot.RequiredInput`, and `FieldLadderStateSnapshot.Target`.
- Produces: route-owned mounted-ladder guidance and expected landing through the existing `pendingLadderAction` state.

- [x] **Step 1: Write the failing Reactor 1 regression**

  Use the installed field 124 walkmesh and native script transitions. Start navigation to the upper piping exit while the trace-backed ladder sample is mounted downward toward `(-233, 1956, -185)`. Assert that activation and repeated guidance say `climb up`, not `climb down`, and that reaching the upper landing advances the route without sending Cloud back down.

- [x] **Step 2: Run the Reloaded test harness and verify RED**

  Expected: FAIL because `TryLockBeacon` currently copies the physical Down input and marks the route as beginning after the wrong landing.

- [x] **Step 3: Implement route-owned reverse correction**

  If the route planned from the projected native landing immediately exposes a ladder action whose waypoint is that landing and whose required input opposes the current native input, capture that route action as pending. Use its destination and required input as the expected mounted landing. Preserve the existing `routeStartsAfterMountedLadder` behavior for routes that genuinely continue from the native landing.

- [x] **Step 4: Run the focused harness and verify GREEN**

  Run the Reloaded test command and require exit code 0.

### Task 3: Verify dual-runtime packaging and deploy locally

**Files:**
- Modify only generated build/package output under existing ignored output directories.

**Interfaces:**
- Consumes: the shared Reloaded assembly built by Tasks 1 and 2.
- Produces: locally deployed x86/7th Heaven and x64-compatible Blind Soldier files.

- [x] **Step 1: Run focused and dual-runtime verification**

  Run the Reloaded harness, Steam2026 x64 harness, parity harness, `git diff --check`, and the repository's dual-runtime verification script.

- [x] **Step 2: Build and deploy the current package**

  Use the repository's existing release/staging scripts; do not edit 7th Heaven or FFNx. Verify the installed DLL hashes match the built artifacts.

- [x] **Step 3: Review the final diff and commit only source, tests, and the plan**

  Exclude `.artifacts/` and generated local logs.
