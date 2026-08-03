# Steam 2026 x64 Field, Battle, and Navigation Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the native Steam 2026 x64 runtime expose the same field object/exit cues, battle information, and six navigation commands as the working x86 runtime.

**Architecture:** Keep all memory reads behind the exact-fingerprint translated guest address space and move only checked, pointer-free observations to worker-side coordinators. Reuse the x86 speech trackers, route planner, target policies, and audio players; add no OCR, guessed labels, host-pointer arithmetic, or direct-line navigation fallback.

**Tech Stack:** C#/.NET 8 Windows, Reloaded.Hooks, translated `ILegacyAddressSpace`, Prism, NAudio/Steam Audio, console regression suites, PowerShell dual-runtime packaging.

## Global Constraints

- Normal launch of `FFVII.exe` must load the enabled mod; no alternate launcher or manual step.
- Preserve working x86 behavior and dual-architecture packaging.
- Use native/state-backed information only; silence is preferable to a guessed identity or label.
- Never expose unsensed enemy private HP/MP/status information.
- Native detours must invoke their original exactly once and must fail closed.
- U/O/J/L/K/I must retain their exact x86 action order and semantics.
- Do not launch or focus the game during implementation; the user performs live validation.

---

### Task 1: Exact x64 battle lifecycle ingress and speech

**Files:**
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Battle/Steam2026BattleRendererCallbackCatalog.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Battle/Steam2026BattleRendererCallbackContract.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Battle/Steam2026BattleRendererDetourIngressCoordinator.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Battle/Steam2026BattleRendererHookSet.cs`
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/Battle/Steam2026BattleAccessibilityCoordinator.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchSession.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Ff7.Accessibility.Steam2026X64.csproj`
- Test: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026BattleRendererIngressTests.cs`

**Interfaces:**
- Consumes: exact translated identities for menu `006D797C`, update `006CE8B3`, text `006D721C`, results `006C9543`, damage `005BB410`; checked `Steam2026BattleObservationReader`.
- Produces: bounded ingress records and worker-owned speech for menu, encounter, target, battle text, enemy action, damage, status, and results.

- [ ] Add regressions requiring all five identities/hooks, callback original-once behavior, translated stack arguments, stale renderer dedupe, and each checked speech domain.
- [ ] Run the x64 test executable and record the expected failures against the one-hook implementation.
- [ ] Implement the five-hook cohort and worker coordinator with module/focus/reset ownership.
- [ ] Run the targeted x64 suite and confirm all battle regressions pass.

### Task 2: Independent field object and exit proximity cues

**Files:**
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Field/Steam2026FieldObjectObservationReader.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchSession.cs`
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/Field/Steam2026FieldExitSpatialCoordinator.cs`
- Test: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026FieldObjectObservationTests.cs`
- Test: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026FieldObjectSpatialCoordinatorTests.cs`
- Create: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026FieldExitSpatialCoordinatorTests.cs`

**Interfaces:**
- Consumes: independently bookended position/control/suppression/object state and checked native gateways.
- Produces: authoritative item/chest/materia spatial pulses and exit/zone-point spatial pulses without requiring volatile script-context equality.

- [ ] Add regressions proving object cues survive missing/advancing script context and remain suppressed for focus, messages, movies, incoherent reads, and foreign fields.
- [ ] Run the x64 test executable and record the expected failure before changing production code.
- [ ] Move object cue ownership to its own coherent snapshot and add an exit cue coordinator using the existing proximity policy/player.
- [ ] Run field cue tests and confirm item and exit cues pass without weakening read coherence.

### Task 3: Exact x64 navigation command routing

**Files:**
- Create: `Ff7.Accessibility.Steam2026X64/Runtime/Field/Steam2026FieldNavigationCoordinator.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchSession.cs`
- Modify: `Ff7.Accessibility.Steam2026X64/Ff7.Accessibility.Steam2026X64.csproj`
- Create: `Ff7.Accessibility.Steam2026X64.Tests/Steam2026FieldNavigationCoordinatorTests.cs`

**Interfaces:**
- Consumes: `Steam2026ForegroundInputAdapter.ObserveRisingEdge`, checked translated field readers, native object/exit/story providers, walkmesh route planner, and config.
- Produces: U previous category, O next category, J previous target, L next target, K repeat target, I toggle spoken route guidance.

- [ ] Add regressions for exact six-key mapping/order, held/refocus silence, non-field/suppressed/torn-state reset, target wrapping, route failure, guidance cadence, and arrival.
- [ ] Run the x64 test executable and record the missing-coordinator failure.
- [ ] Implement the coordinator using the shared controller/route components and native targets, excluding unverified NPC label guesses.
- [ ] Run the navigation and shared parity suites.

### Task 4: Dual-runtime verification and installation

**Files:**
- Verify: all source projects and test executables.
- Build: `dist/ff7.accessibility.reloaded`.
- Install: Reloaded-II mod directory and native/legacy bootstraps.

**Interfaces:**
- Consumes: passing x64, x86, shared, parity, and packaging checks.
- Produces: enabled installed x86/x64 mod ready for the user's normal `FFVII.exe` live test.

- [ ] Build both architecture artifacts and run focused plus dual-runtime verification.
- [ ] Run the installer with the exact Steam game root and enabled research-native profile.
- [ ] Verify installed PE machines, hashes, config flags, assets, dependencies, and absence of backup directories inside `Mods`.
- [ ] Hand off a short live checklist covering item/chest/materia cues, exit points, all six nav keys, and battle encounter/target/action/damage/status/results.
