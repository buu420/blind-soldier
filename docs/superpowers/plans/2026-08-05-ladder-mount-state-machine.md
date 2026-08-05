# Ladder Mount State Machine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the original traversal sound and make route-aware ladder mounting and dismounting reliable from live native position and movement state.

**Architecture:** Keep discovery and mount feedback as separate audio channels. Put entrance ownership, repeated prompting, completion, and stale-state rejection in the shared route controller so x86 and x64 receive identical behavior.

**Tech Stack:** C#/.NET, Reloaded-II, Prism output, PowerShell packaging, native FFVII field memory observations.

## Global Constraints

- Preserve `ladder_061.wav` for ordinary traversal discovery.
- Use `ladder_approach_214.wav` only as the active route's at-entrance mount cue.
- Use native XYZ, triangle, movement mode, phase, and endpoint data; do not infer ladder completion from elapsed time alone.
- Ship and deploy both x86 and x64 implementations together.

---

### Task 1: Protect the shared ladder behavior with failing tests

**Files:**
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

**Interfaces:**
- Consumes: `FieldNavigationController.UpdateLiveTracking`, `CreateSpokenGuidance`, and native route fixtures.
- Produces: regression coverage for repeated entrance prompts, drift recovery, state bounce rejection, and live-position completion.

- [ ] **Step 1: Write behavior tests with literal speech and route expectations.**
- [ ] **Step 2: Run `dotnet run --project Ff7.Accessibility.Reloaded.Tests -- --reactor-ladder-only` and confirm the new cases fail for the observed regressions.**
- [ ] **Step 3: Record the failing assertions before production edits.**

### Task 2: Split traversal and mount cue configuration

**Files:**
- Modify: `Ff7.Accessibility.Core/AccessibilityConfig.cs`
- Modify: `Ff7.Accessibility.Core/AccessibilityConfigMigration.cs`
- Modify: `Ff7.Accessibility.Reloaded/Configuration/config.json`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Create: `Ff7.Accessibility.Reloaded/FieldLadderMountCueTracker.cs`
- Modify: both runtime project/package asset manifests as required.

**Interfaces:**
- Produces: `FieldLadderMountCueIntervalMs`, `FieldLadderMountCueVolumePercent`, `FieldLadderMountCueSoundPath`, and an entrance-only tracker keyed by native transition ID.

- [ ] **Step 1: Add failing config, migration, asset, and tracker tests.**
- [ ] **Step 2: Run the focused ladder suite and confirm the split is absent.**
- [ ] **Step 3: Implement the defaults and migration: discovery `1600 ms / ladder_061.wav`, mount `700 ms / ladder_approach_214.wav`.**
- [ ] **Step 4: Run the focused ladder suite and confirm the config/tracker cases pass.**

### Task 3: Implement the shared route-aware ladder state machine

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/FieldNavigationAssistant.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

**Interfaces:**
- Produces: repeatable at-entrance speech, pending-action endpoint validation, live-position completion, and completed-action suppression.

- [ ] **Step 1: Make ladder prompts interval-driven and reset their interval whenever Cloud leaves the entrance radius.**
- [ ] **Step 2: Accept a fresh mounted state only when it matches the pending route ladder endpoint or navigation began mid-ladder.**
- [ ] **Step 3: complete an active ladder at its verified landing even if the native read is temporarily unavailable or still reports the completing phase.**
- [ ] **Step 4: prevent the completed forward or reverse traversal from being recaptured for the current route.**
- [ ] **Step 5: Run the focused ladder suite and confirm every new regression passes.**

### Task 4: Wire both audio channels into x86 and x64

**Files:**
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Field/Steam2026FieldNavigationCoordinator.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Field/Steam2026FieldNavigationRuntime.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026FieldNavigationRuntimeTests.cs`

**Interfaces:**
- Consumes: the shared route priority ID and entrance-only mount tracker.
- Produces: independent discovery and mount playback with immediate stop on mount, drift, suppression, field change, or loss of coherent ownership.

- [ ] **Step 1: Add failing x64 coordinator tests proving the mount player is silent outside the entrance and repeats inside it.**
- [ ] **Step 2: Add the independent player/tracker to both hosts.**
- [ ] **Step 3: Run both runtime test projects and confirm parity.**

### Task 5: Package, deploy, and verify

**Files:**
- Modify: package/install validation only if the new source file or config keys require it.

**Interfaces:**
- Produces: a dual-runtime package installed into the active C-drive FFVII tree with the user's remaining settings preserved.

- [ ] **Step 1: Run the focused and full unit suites plus installer/package validation.**
- [ ] **Step 2: Build `dist/ff7.accessibility.reloaded` with both runtime DLLs and both WAV assets.**
- [ ] **Step 3: Deploy transactionally to the active FFVII install and preserve non-ladder user configuration.**
- [ ] **Step 4: Compare packaged and installed hashes and inspect the installed ladder config.**
- [ ] **Step 5: Commit the verified source and report the exact live test scenario.**
