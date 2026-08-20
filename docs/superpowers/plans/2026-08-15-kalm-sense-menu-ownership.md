# Kalm, Sense, and Menu Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct Kalm navigation labels and treasure coverage, speak the complete native Sense result atomically, and prevent stale main-menu speech from bleeding into victory and shop screens on both supported runtimes.

**Architecture:** Keep interpretation shared. A stable-ID Kalm presentation policy sits after native exit discovery; a native Sense snapshot plus typed message resolution feeds one atomic speech coordinator; module and exact shop ownership gate the existing main-menu readers. All ambiguous native reads fail closed.

**Tech Stack:** C#/.NET 8, Reloaded-II x86, Steam 2026 x64 translated legacy state, Prism, Ghidra, Kujata field data.

## Global Constraints

- Preserve every pre-existing dirty change in this worktree.
- Stage only task-owned hunks; do not sweep unrelated modifications into commits.
- Use native state, not OCR, timing guesses, or English-only text matching.
- Add a failing regression before production code for every behavior change.
- Keep x86 and x64 behavior equivalent.
- Do not publish a release in this plan. Deploy local test artifacts after verification.

---

## Task 1: Correct Kalm exits and verify every real treasure

**Files:**

- Create: `Ff7.Accessibility.Reloaded/FieldExitPresentationPolicy.cs`
- Modify: `Ff7.Accessibility.Reloaded/FieldExitLabelResolver.cs`
- Modify: `Ff7.Accessibility.Reloaded/NativeFieldExitTargetProvider.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Field/Steam2026FieldNavigationCoordinator.cs`
- Create: `Ff7.Accessibility.Reloaded.Tests/KalmExitPresentationTests.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify if a failing publication test requires it: `Ff7.Accessibility.Reloaded/Assets/navigation/field_objects.json`

- [ ] **Step 1: Add RED stable-label tests**

Assert all field-335 stable IDs receive the approved labels, including Materia versus Weapon Store and named houses. Assert the two field-328 return gateways remain distinct.

- [ ] **Step 2: Add RED presentation-policy tests**

Feed both world-boundary segments to the policy. Assert exactly one logical world-map exit, no broad dedupe of the two field-328 storefronts, and native Kalm-completion gating.

- [ ] **Step 3: Add RED treasure coverage/publication tests**

Assert the six guide treasures exist exactly once with their collection gates and can publish when collectible. Cover line and model records explicitly.

- [ ] **Step 4: Run focused tests and record the expected failures**

Run:

`dotnet run --project Ff7.Accessibility.Reloaded.Tests -c Release -- --kalm-only`

If the harness does not yet support `--kalm-only`, add the narrow selector before rerunning.

- [ ] **Step 5: Implement the smallest shared policy and labels**

Use stable IDs. Collapse only the two world-boundary segments. Inject a native completion predicate/read and fail closed if it cannot be read. Wire the policy after native discovery in x86 and x64.

- [ ] **Step 6: Repair only treasure records proven broken**

Do not duplicate existing pickups. If a test proves an existing line target cannot publish while collectible, change only that entry to a static location and retain the collected flag.

- [ ] **Step 7: Run focused tests to GREEN**

Run the same focused command and confirm every Kalm assertion passes.

- [ ] **Step 8: Review and commit task-owned hunks**

Run `git diff --check`. Stage only this task's hunks, including narrow hunks in already-dirty files, and commit with:

`git commit -m "fix: correct Kalm navigation presentation"`

---

## Task 2: Speak complete native Sense results atomically

**Files:**

- Modify: `Ff7.Accessibility.LegacyLayout/BattleRuntimeTextReader.cs`
- Modify: `Ff7.Accessibility.LegacyLayout/BattleStateReader.cs`
- Modify: `Ff7.Accessibility.Runtime.Abstractions/Observations/BattleObservations.cs`
- Create: `Ff7.Accessibility.Reloaded/BattleSenseSpeechCoordinator.cs`
- Modify: `Ff7.Accessibility.Reloaded/BattleMessageSpeechTracker.cs`
- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Battle/Steam2026BattleObservationReader.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Battle/Steam2026BattleAccessibilityCoordinator.cs`
- Create: `Ff7.Accessibility.Reloaded.Tests/BattleSenseSpeechTests.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Create or modify focused x64 battle tests under `Ff7.Accessibility.Steam2026X64.Tests/`

- [ ] **Step 1: Add RED decoder tests**

Encode native `{ID}`, `{ELEMENT}`, HP, and MP controls. Assert correct byte consumption, punctuation, target ID, and localized element resolution.

- [ ] **Step 2: Add RED native snapshot/privacy tests**

Seed scene enemy records and the persistent sensed flag. Assert level and weaknesses are available only when the native bit is set and actor/scene reads remain coherent.

- [ ] **Step 3: Add RED atomic speech tests**

Assert a successful Sense produces exactly one utterance in native visual order, following fragments do not interrupt it, multiple weaknesses are represented correctly, and `Couldn't sense.` still speaks.

- [ ] **Step 4: Add x64 parity tests**

Prove the translated-address runtime produces the same snapshot and one utterance without exposing unsensed data.

- [ ] **Step 5: Run focused tests and record the expected failures**

Run:

`dotnet run --project Ff7.Accessibility.Reloaded.Tests -c Release -- --battle-sense-only`

`dotnet run --project Ff7.Accessibility.Steam2026X64.Tests -c Release -- --battle-sense-only`

- [ ] **Step 6: Correct the shared text decoder**

Fix `0xEF`, `0xF0`, and control-token lengths. Resolve elements from the game's localized text source rather than an English-only parser.

- [ ] **Step 7: Implement coherent native Sense state**

Read level, element IDs/rates, and the persistent sensed bit from Ghidra-confirmed structures. Keep public snapshots redacted until the native flag permits disclosure.

- [ ] **Step 8: Implement one atomic speech owner**

Classify the native Sense sequence structurally, format one utterance, and suppress only that sequence's redundant fragments. Preserve ordinary battle-message behavior and the native failure line.

- [ ] **Step 9: Wire both runtimes and run focused tests to GREEN**

Run both focused commands and shared abstraction tests:

`dotnet run --project Ff7.Accessibility.Shared.Tests -c Release`

- [ ] **Step 10: Review and commit task-owned hunks**

Run `git diff --check`. Stage only Sense-related hunks and commit with:

`git commit -m "fix: announce complete Sense results"`

---

## Task 3: Enforce exact main-menu speech ownership

**Files:**

- Modify: `Ff7.Accessibility.Reloaded/Mod.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchObservationPump.cs`
- Create or modify: `Ff7.Accessibility.Reloaded.Tests/MainMenuOwnershipTests.cs`
- Modify: `Ff7.Accessibility.Reloaded.Tests/Program.cs`
- Modify: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026ResearchObservationPumpTests.cs`

- [ ] **Step 1: Add RED x86 ownership tests**

Assert stale valid main-menu bytes remain silent under module 17, exact shop ownership, and incoherent shop ownership. Assert module 5 plus proven non-shop ownership speaks normally and reacquisition speaks again.

- [ ] **Step 2: Add RED x64 ownership tests**

Assert the observation pump returns `Closed` and clears its state for module 17, exact shop ownership, and incoherent ownership. Assert the real root menu reacquires and speaks.

- [ ] **Step 3: Run focused tests and record the expected failures**

Run:

`dotnet run --project Ff7.Accessibility.Reloaded.Tests -c Release -- --main-menu-ownership-only`

`dotnet run --project Ff7.Accessibility.Steam2026X64.Tests -c Release -- --main-menu-ownership-only`

- [ ] **Step 4: Implement x86 gating and reset**

Require module 5, a coherent exact-shop read proving false, and no save owner. On ownership loss, reset both selection and scheduler state.

- [ ] **Step 5: Implement x64 lifecycle/shop gating and reset**

Pass `lifecycle.ModuleId` into the main-menu read. Return `Closed` and clear the last state key whenever ownership is absent or incoherent.

- [ ] **Step 6: Run focused tests to GREEN**

Run both focused commands and verify reacquisition behavior.

- [ ] **Step 7: Review and commit task-owned hunks**

Run `git diff --check`. Stage only menu-ownership hunks and commit with:

`git commit -m "fix: isolate main menu speech ownership"`

---

## Task 4: Integrate, verify, build, and deploy locally

**Files:**

- Modify only if required by verified integration failures: project files or deployment manifests associated with the two runtimes.

- [ ] **Step 1: Run all focused suites together**

Run the three focused x86 selectors, both focused x64 selectors, and the shared suite.

- [ ] **Step 2: Run full automated suites**

Run:

`dotnet run --project Ff7.Accessibility.Reloaded.Tests -c Release`

`dotnet run --project Ff7.Accessibility.Steam2026X64.Tests -c Release`

`dotnet run --project Ff7.Accessibility.Shared.Tests -c Release`

- [ ] **Step 3: Build both runtime deliverables**

Use the repository's documented release/development build commands. Capture successful exit codes and artifact paths.

- [ ] **Step 4: Inspect the final diff**

Run `git status --short`, `git diff --check`, and an intentional diff review. Confirm no pre-existing unrelated change was altered or staged accidentally.

- [ ] **Step 5: Deploy both runtimes to the local installation**

Use the repository's existing deployment script or documented copy process. Verify deployed file timestamps/hashes and dependency presence.

- [ ] **Step 6: Report live-test handoff**

State the exact changes, automated evidence, deployed paths, and the smallest useful in-game checks: Kalm exits/items, Sense on a weak enemy, victory results, and Kalm shop entry.
