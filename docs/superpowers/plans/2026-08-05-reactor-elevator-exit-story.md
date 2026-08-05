# Reactor Elevator Exit and Story Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the post-elevator exit and Story route lead through the Reactor 1 main staircase to the piping and ladder room in both runtimes.

**Architecture:** A pure shared policy converts the branch-insensitive field-121 script exit into one progression-correct destination before labeling and reachability. Generated Story definitions cover the missing forward and escape transitions in fields 121 and 122.

**Tech Stack:** C#/.NET 8 and 9, PowerShell, Reloaded-II, native FFVII field scripts and walkmeshes, Ghidra 12.0.4.

## Global Constraints

- Preserve all unrelated exits and Story targets.
- Keep x86 and x64 behavior identical.
- Use native trigger geometry and progression state; do not guess or expose future objectives.
- Deploy both runtimes to the active install after verification.

---

### Task 1: Conditional elevator exit policy

**Files:**
- Create: `Ff7.Accessibility.LegacyLayout/FieldScriptExitBranchPolicy.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Field/Steam2026FieldScriptExitPolicy.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Test: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026FieldNavigationRuntimeTests.cs`

**Interfaces:**
- Consumes: `FieldNavigationTarget`, field id, and current GameMoment.
- Produces: `FieldScriptExitBranchPolicy.Resolve(int, int, IReadOnlyList<FieldNavigationTarget>)`.

- [ ] Add assertions that field 121 resolves to destination 122 at GameMoment 12, 120 at GameMoment 27, 129 during the Reactor 5 descent, and 128 during its return.
- [ ] Run the x64 test project and confirm the new assertions fail against the ambiguous target.
- [ ] Implement the pure resolver and invoke it from both runtime adapters before labels and reachability.
- [ ] Run the x64 and shared tests and confirm the branch cases pass.

### Task 2: Missing Story chain

**Files:**
- Modify: `tools/Generate-FieldStoryEvents.ps1`
- Modify: `Ff7.Accessibility.Reloaded/Assets/navigation/field_story_events.json`
- Test: `Ff7.Accessibility.Reloaded.Tests/Program.cs`

**Interfaces:**
- Consumes: native field 121 script line and field 122 gateway lines.
- Produces: four state-gated Story targets for forward and escape traversal.

- [ ] Extend the Reactor regression to require the four field-121/122 targets and verify their GameMoment windows and coordinates.
- [ ] Run `--reactor-ladder-only` and confirm the missing-target failure.
- [ ] Add the generator definitions and regenerate the embedded catalog.
- [ ] Rerun the focused test and confirm it passes.

### Task 3: Release verification and deployment

**Files:**
- Modify only generated build/package outputs outside source control.

**Interfaces:**
- Consumes: verified source tree.
- Produces: active x86 and x64 Blind Soldier runtime files.

- [ ] Run the focused tests, full affected test projects, and Release builds.
- [ ] Compare generated catalog output and inspect the source diff for unrelated changes.
- [ ] Stage the dual-runtime package and verify required assemblies/assets.
- [ ] Back up the active mod, deploy atomically while preserving user config, and compare installed hashes with staged hashes.
