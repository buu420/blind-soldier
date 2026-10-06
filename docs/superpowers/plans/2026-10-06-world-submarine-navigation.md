# World-map submarine navigation implementation plan

> Execution: native implementation in this chat with a separate Claude review. The user confirmed this scope on October 6; existing authorization covers release and deployment after checks. Do not request the same approval again.

**Goal:** Give the ordinary world-map submarine the existing destination selection and automatic navigation, avoiding unintended Emerald encounters and approaching Emerald only when explicitly selected.

**Architecture:** Extend the shared world-map navigator used by both hosts. Recover native underwater geometry in global coordinates; resolve turn, depth and thrust through the live keyboard mapping. Observe the player's own depth for ordinary safe travel and expose moving underwater entities only from verified native visibility.

**Tech stack:** C#/.NET 8, x86 legacy guest layout, Steam x64 translated guest memory and native keyboard overlay, Ghidra and installed world scripts.

**Spec:** User-confirmed scope and native findings in `C:/Users/buu42/Documents/FFVII-ActiveBuild/submarine-navigation-20261006/integration-notes.md`.

## Global constraints

- Support x86/7th Heaven and Steam x64 through the same world navigation implementation.
- Convey ordinary sighted-player information; never route with hidden live Emerald positions.
- Only native inputs; no teleport, physics, camera-state, encounter or story writes.
- Yield to pause, menu, dialogue, combat, lost focus, unavailable memory and failed input delivery.
- Retain settings, saves, narration and description history during verified deployment.
- Versions 0.8.3 through 0.8.5 are finished. Next normal release is 0.8.6, after fresh checks.

## Review focus

- Turn/thrust actions on remapped controls, including aliases to menu/dive/other conflicting actions.
- Map2 coordinate wrap and native block remapping, including approach to installed destinations.
- Initial deep player state and changing safe-depth ownership while navigation is active.
- Emerald leaving view after explicit selection, defeat/unloaded state and normal destination descent.
- Unselected field/story entries and stopped native movement, with timely spoken recovery.

## Task 1: Native underwater geometry and movement profile

Files: WorldMapDataLoader.cs, WorldMapRoutePlanner.cs, WorldMapDataLoaderTests.cs, submarine navigation tests.

- [x] Add installed-map failures for global wrap0x48000/0x38000, 9x7 logical versus12 physical blocks and central-mesh source mapping; run red.
- [x] Reconstruct logical wm2 blocks with column modulo3, row `(row+2)%4`; accept12 physical blocks. Preserve map0/map3.
- [x] Test native model13 surface/underwater terrain mask0x4048008 and reject model28 as a playable submarine; run red then green.

## Task 2: Native turn, depth and thrust through shared input

Files: HighwayDirectionInputMappingResolver.cs, HighwayAutoSteeringController.cs, NavigationAutoWalkController.cs, WorldMapNavigationController.cs, both world host adapters and meaningful input tests.

- [x] Add failures for pure yaw, pure thrust and recovery depth; use slot5 Confirm with no Highwind strafe or minigame throttle action.
- [x] Resolve commanded world-sub actions from one live mapping snapshot and reject aliases with unintended native actions.
- [x] Follow accepted native heading with bounded turning; only thrust when the planned forward step is clear. Route progress is horizontal while depth is independent.
- [x] Test stop/suspend/read failure/focus/menu boundaries and x64 keyboard overlay delivery.

## Task 3: Destinations, Emerald observations and useful arrival

Files: world entity/state readers, target catalog, submarine navigation policy and tests.

- [x] Complete renderer/encounter research and record the exact visibility predicate and depth proof.
- [x] Add native underwater destinations, useful approach points and native proximity arrival. Exclude unselected story/field triggers.
- [x] Travel at native entry depth without hidden enemy tracking; require verified visible Emerald observations for approach and any risky descent. Stop with explanation when safe approach cannot be established.
- [x] Exercise ordinary routes, depth changes, explicit Emerald selection, hidden/expired observations and no-safe-route behavior.

## Task 4: Review, release and verified deployment

- [x] Run focused shared-world/native-input tests on both architectures; have Claude independently review the resulting diff and resolve material findings. Final approval: `final-review-approved.md` in the research workspace.
- [x] Document controls, actual behavior and unperformed live-game/hook validation.
- [ ] Update0.8.6 release metadata, run all19 release gates and four suites against exact packages in a fresh workspace.
- [ ] Create/attach PR, publish verified archives/catalog records, deploy verified payloads with protected-file hashes retained, and write a continuation handoff.
