# Ancient Forest Puzzle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Make the visible Ancient Forest puzzle understandable and navigable in 0.10.9.

**Architecture:** Extend the existing shared field activity readout with a focused
forest state reader and narrator. Keep native-gated throwing/takeoff targets in the
existing Objects catalog and add only crossings verified against native scripts and
walkmesh. Both runtimes compile the same implementation and use newest-only speech.

**Tech Stack:** C#/.NET 8, native FFVII field scripts, Ghidra, PowerShell/Python release tools.

**Spec:** `docs/superpowers/specs/2026-11-04-ancient-forest-puzzle.md`

## Global Constraints

- Release 0.10.9 for both runtimes and the five reviewed Echo-S layouts.
- Creature placement, puzzle choices, button-triggered jumps and reset remain normal game actions.
- No hidden solution, future timer, hidden contents, changed timers or automatic puzzle actions.
- Retain the sole source checkout in Dropbox and preserve player data and unrelated mods.

## Review Focus

- Failed or torn reads must not become empty hands or an open plant.
- Timed changes during dialogue must replace obsolete speech.
- Field reset, focus loss and room changes must retire previous puzzle state.
- Multiple visually identical creatures need stable names shared by navigation and carrying speech.
- Routes must not cross a flytrap or invent a gated jump while its native prerequisite is absent.

## Task 1: Native state feedback and target identity

**Files:** New `AncientForestPuzzleReadout.cs` and `AncientForestPuzzleStateReader.cs`
in `Ff7.Accessibility.Reloaded`; shared links in the x64 project; activity readout,
observation builder and the two runtime hosts; `TownInteractionObjectCatalog.cs`;
new forest regression tests in the existing test projects.

**Interfaces:** The state reader supplies a coherent reviewed native bank snapshot.
The readout consumes `FieldActivityObservation` and produces a current descriptive
line plus a change cue. It never owns or injects player input.

- [x] Write and run failing tests for distinct creature names and the first-screen pickup/throw/plant sequence.
- [x] Write failing tests for unreadable state, reset, unknown script identity, focus/mute and stale speech replacement.
- [x] Implement the smallest shared reader/readout and integrate with existing activity repeat/delivery.
- [x] Run focused tests on both architectures; check native bindings across reviewed Echo-S layouts.

## Task 2: Usable crossings and complete room coverage

**Files:** `TownInteractionObjectCatalog.cs`, focused forest tests and
`GuideRouteRegressionTests.cs`; route code only where a demonstrated native gap requires it.

**Interfaces:** Existing `FieldNavigationObjectDefinition` gates, triangles, lines,
manual handoff and stable target identifiers.

- [x] Pin each missing vine/takeoff/throwing approach to the audited native script and collision geometry in a failing regression.
- [x] Add the verified targets or repair the demonstrated route gap, retaining native manual actions and hazards.
- [x] Replay all forest rooms, items and exits with native gates; retain honest unavailability where a puzzle prerequisite is missing.

## Task 3: Verify, deploy and publish

**Files:** Version metadata, README/release notes, validation evidence and handoff.

- [x] Update all release metadata to 0.10.9 and normalize changed source bytes.
- [ ] Obtain an independent Opus 5.5 max review of the final source; resolve material findings with regressions.
- [ ] Run the full dual-runtime release gate and exact packaged-assembly suites with native game and Echo-S data.
- [ ] Deploy the exact verified payloads with rollback and preservation checks while the game stays closed.
- [ ] Merge verified source, publish GitHub assets and both Mod Manager records; verify anonymous downloads and catalog preservation.
- [ ] Wait for tagged CI, remove only owned scratch after evidence retention, and perform final installed/public/protected-file checks.

## Execution ledger

- 2026-11-04: Scope explicitly confirmed by user. Canonical checkout clean at
  1f6c390d3e76bb630a63522dd7be7e4ced7444d0; created fix/ancient-forest-puzzle-0109.
- Ruling: Execute in this branch in the sole canonical Dropbox checkout. The user's
  no-duplicate-workspace instruction overrides skill worktree defaults.
- Ruling: No further design or publishing permission question: the user's confirmed
  implementation/deployment/release scope already authorizes these steps.
- Ghidra evidence retained privately for 00637ABB LINE crossing and 00615CA3 JUMP;
  read-only analysis session closed. Native field audit runs in Opus 5.5 at max effort.
- Root RED/GREEN receipts retained privately: duplicate names/carrying/plant feedback;
  missing raised throwing targets; flytrap closure; Echo-S missing frog map, wrong
  Down wording and bite-as-route; misleading carrying arrival; narrow-throat chest.
- Native x86 and x64 focused checks passed before the delegated route/state extensions,
  including all five loaded Echo-S layouts. These are intermediate results, not release proof.
- Native audit exposed 37 repeated auto-walk bites in the tester's journey. Ruling:
  repair active jaw routing within the approved usable-crossing scope. Opus helper
  ff7-forest-route-0109 owns FieldWalkmeshNavigation and its new hazard files/tests;
  ff7-midgar-native-0106 owns AncientForestPuzzleReadout, StateReader and PuzzleTests.
  Root owns their runtime integration, Echo/catalog fixes and release work.
- Native replay rejected a proposed 620 plant-top throw: it is an ejection zone,
  not a creature throw. Removed it. All three useful 622 top throws are now bound
  to native landings and creature branches, without choosing the player's puzzle sequence.
- Fresh preservation baselines retained: 11,377 protected files, three 7th Heaven
  files and 124 saves/settings files; the installed accessible launcher has a separate
  original hash. Fresh Echo mirror: 3,535 verified temporary files, originals unchanged.
- Accessible launcher built as 2.0.0.0+blind-soldier.0.10.9 before package creation.

- Final integration regressions reproduce and fix carrying-aware arrival instructions,
  an assembled bank/model snapshot spanning a pickup, underfoot shaking warnings
  blocked by a long instruction, and unreadable Echo-S motion being mistaken for
  an open plant. The combined native x86 check passed all six layouts before the
  route helper's final margin refinement; full final release proof remains pending.
- Route helper native guide cases pass, including safe local ground, elevated vines,
  closed-mouth access, dynamic movement guards and honest manual/unavailable rewards.
  Its full-suite attempt needed the existing native runtime environment; rerunning
  with that setup is distinct from a game-logic failure.
- The first 19-step release gate passed, then its artifacts were retired after
  independent review identified interrupted puzzle feedback and unsafe movement
  holds during slow scans. Native steering replay reproduced the latter; the
  first archives remain private historical evidence and must not be deployed.
- Final review and release verification bind a new raw-byte source manifest after
  those corrections. Subsequent release progress is recorded in private receipts
  and the completed handoff; source bytes remain unchanged after that freeze.
- Final corrections use fresh position measurements and native input polls to
  bound movement during slow scans, including both native script-key banks on
  x86 and a conservative Steam 3x bound. The replay retains a biting unleased
  control and a stalled stale-mark control; the corrected schedule makes progress.
- Current facts survive tracked speech interruptions and slow backend output;
  routine directions defer while a forest fact is pending. Native Talk reach
  restores Supershot ST guidance outside the jaws, with a spoken manual handoff
  when another automatic step cannot be checked safely.
