# Winding Tunnel HQ entrance implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement these tasks in the existing Dropbox branch. Use the approved Opus 5.5 helper for independent review.

**Goal:** Release 0.10.7 with usable, accurately labelled Shinra HQ ladder navigation in the late Midgar tunnels.

**Architecture:** Keep the native exit, movement and climbing behavior. Add the tunnel initializer's section seal to the catalog's exit guards, share the bound exit's availability with ladder traversals, and suppress the incoming ladder landing as a second proximity entrance. Both runtime providers consume the same shared rules.

**Tech Stack:** C#, .NET 8, native field scripts and walkmesh, Ghidra evidence, PowerShell release tooling.

**Spec:** User-approved 0.10.7 scope: repair the winding-tunnel HQ entrance and route guidance, both runtimes and Echo-S, normal climbing buttons, deployment and publication. The retained tester log and independent native audit establish the section-zero seal and false ladder cues.

## Global constraints

- Work in the canonical Dropbox checkout; do not create a Documents checkout or another worktree.
- Preserve saves, preferences, recordings, native game files and unrelated mods/catalog entries.
- Do not automate Confirm, climbing or game transitions, or expose a ladder the game hides.
- Use only actual Claude Opus 5.5 at max effort for helper work.
- Separate automated/native-data verification from unperformed live gameplay and audible/controller proof.

## Review focus

- Reused field 736 with bit 6 clear must not emit a ladder cue.
- Section-zero's removed walkway must not remain a selectable route through narrow mesh slivers.
- The real ladder must remain selectable after entering the correct branch and stop auto walk for the native action/climb.
- Echo-S's shifted entity indices must follow current LINE geometry and native guards.
- Field 778's unconditional ladder and other fields' automatic traversals must remain available.

### Task 1: Native tunnel availability and cue regression

**Files:** `Ff7.Accessibility.Reloaded.Tests/GuideRouteRegressionTests.cs`, `Ff7.Accessibility.Reloaded.Tests/WholeGameEchoCompatibilityTests.cs`, `Ff7.Accessibility.Reloaded/FieldScriptNavigationCatalog.cs`, `Ff7.Accessibility.Reloaded/FieldScriptExitGuards.cs`, `Ff7.Accessibility.Reloaded/FieldLadderProximityCueTracker.cs`, both runtime transition providers.

**Interfaces:** Keep catalog `ExitGuards` authoritative. Add `FieldScriptExitGuards.ApplyToExitTraversals(transitions, field, memory, checkedNativeGuard)` to filter traversals bound to a closed exit after the LINE grace period.

- [ ] Add native section-zero/section-two/section-eighteen cases with actual initializer IDLCK bits and the native t54 arrival; capture the failing sealed-exit assertion before implementation.
- [ ] Verify the installed initializer's section test and lock sequence, then attach its inverse availability guard to the matching native LINE exit.
- [ ] Add guarded traversal and proximity regressions: hidden ladder is silent, available ladder has one entrance, closing the guard is immediate, and field 778 remains unconditional.
- [ ] Apply guarded traversal filtering after transition resolution in both runtimes. Scope incoming-landing cue suppression to the two reviewed tunnel fields.
- [ ] Replay the native junction/bit-6 journey and existing manual mount/climb holds. Run both focused hosts against C: and D: native data and five exact loaded Echo-S layouts.

### Task 2: Clear label and independent review

**Files:** `FieldExitLabelResolver.cs`, `MidgarTunnelStoryTests.cs`, release notes and version metadata.

- [ ] Name the verified HQ approach ladder clearly while retaining LINE/destination identity checks and the native main-story route.
- [ ] Run the label/identity regressions and request independent Opus 5.5 max review of the final diff and native proof.
- [ ] Resolve actionable findings, repeat the affected checks, and freeze the source for 0.10.7.

### Task 3: Verified 0.10.7 release

**Files:** Existing dual-runtime verification/package/deployment scripts and new private release evidence.

- [ ] Run the complete required release gate, Ghidra checks, and exact-package tests for all four runtime DLL copies.
- [ ] Verify fresh launcher versions and archive content; hash all final archives and sidecars.
- [ ] Deploy both runtimes with preservation checks, publish reviewed source/release and both Mod Manager entries, and verify public downloads/catalog hashes.
- [ ] Finish owned bounded cleanup and record actual automated coverage and live-test limits in the handoff.
