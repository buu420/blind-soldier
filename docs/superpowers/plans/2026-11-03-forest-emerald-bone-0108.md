# Ancient Forest, Emerald and Bone Village implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task. The user permits only actual Opus 5.5/max for Claude helpers.

**Goal:** Release 0.10.8 with available Ancient Forest navigation, a persistent underwater Emerald Events entry, and the excavated Key to Sector 5 pickup.

**Architecture:** Keep the shared, renderer-backed underwater reader unchanged. A separate per-runtime memory retains only positively observed Emerald positions, supplies an explicitly unlocated row before sighting, and guides to the last-seen area at normal submarine travel depth. Native model-contact and field-script evidence govern the Forest entrance and excavation chest; both runtime hosts consume the same semantics.

**Tech stack:** C#/.NET 8, shared guest-memory readers, installed native world/field archives, PowerShell release verification, Ghidra.

**Spec:** Confirmed scope below, approved by the user on 3 November 2026.

## Confirmed scope and constraints

- Restore Ancient Forest whenever native terrain and the selected vehicle permit reaching it, including after Ultimate Weapon.
- Emerald remains in Events while its native entity exists; report unknown until first sighting, then current visible or clearly labelled last-seen information. Never update it with hidden patrol coordinates.
- Expose Bone Village's excavated Key pickup through the native results interaction. Preserve normal interaction and encounter controls.
- Both runtimes and five Echo-S layouts; use the canonical Dropbox checkout without another source worktree.
- Deploy and publish 0.10.8, preserving saves, settings, audio history, native game files and unrelated mods. Distinguish automated/native evidence from live gameplay and listening.

## Review focus

- Hidden XYZ changes must never move a remembered route or manufacture a sighting.
- Unlocated targets must neither produce an origin route nor claim arrival.
- A new load, native pool replacement or defeated Emerald must not reuse an old sighting.
- Last-seen travel must retain ordinary Emerald avoidance and stop without claiming the creature is present.
- Before/after Ultimate Weapon, native vehicle contact and unopened/empty excavation results must follow the installed scripts, including Echo-S entity remapping.

## Task 1: Persistent Emerald selection and honest remembered guidance

**Files:** Create `Ff7.Accessibility.Reloaded/WorldMapEmeraldMemory.cs`; modify `WorldMapRuntimeContext.cs`, `WorldMapTargetCatalog.cs` (target knowledge only), `WorldMapRoutePlanner.cs`, `WorldMapNavigationController.cs`, `WorldMapSubmarineNavigationController.cs`; test `Ff7.Accessibility.Reloaded.Tests/WorldMapUnderwaterVisibilityTests.cs`.

- [ ] Add a regression through the native camera -> runtime -> Events -> selection -> automatic input boundary. Assert the unknown row cannot start movement; visible-to-hidden menu delay keeps identity; hidden coordinate drift does not move its last-seen route; remembered travel rises to safe depth; arrival says last-seen and stops; resighting updates it; reset/absence removes stale knowledge.
- [ ] Run `dotnet run --project Ff7.Accessibility.Reloaded.Tests -c Release -- --emerald-tracking-only` with installed data/source roots and retain the expected failure.
- [ ] Add explicit Current/LastSeen/Unknown target knowledge with an invalid unlocated sentinel and planner/arrival guards. Store only positively visible snapshots in `WorldMapEmeraldMemory`, clear on empty/replaced native pools, and wrap the runtime catalog provider with the retained row.
- [ ] Describe unknown/last-seen states in selection and navigation. Remembered approaches use normal travel depth and terminate at the recorded approach area; only positively visible explicit pursuit uses Emerald depth. Keep existing strict direct-catalog/visibility behavior and ordinary route avoidance.
- [ ] Rerun focused checks and relevant submarine scenarios; commit the independently verified change.

## Task 2: Native Ancient Forest entrance

**Files:** Create `Ff7.Accessibility.Reloaded/WorldMapAncientForest.cs` and `Ff7.Accessibility.Reloaded.Tests/WorldMapAncientForestTests.cs`; modify `WorldMapTargetCatalog.cs`, `WorldMapVehicleObstacles.cs`, `WorldMapNavigationController.cs`, the linked x64 project and both test harnesses. `wm0.ev` model25 fn3 (0x5903) enters location55/field622 on Confirm for models0/1/2; fn0 places it at (92960,179731). Its executable collision mask is `00 00 18 3C 3C 18 00 00`. A valid live pool must contain the unhidden model; offline catalog construction uses that fixed native placement. Contact ground is not an automatic field entrance.

- [ ] Prove a failing native stage-4 Locations/menu/route test from the supplied log's progress state, with pre-unlock foot, legal chocobo and Highwind landing cases.
- [ ] Add the native model-25 entrance using its actual collision, handler and available terrain; preserve normal Confirm and dismount behavior.
- [ ] Verify the native contact handoff and that no unreachable plateau or unrelated field entrance is offered; commit.

## Task 3: Bone Village excavation results

**Files:** Modify the nine 617/entity13 rows in `Assets/navigation/field_objects.json`, setting `usesTalkInteraction` and `cueKindOverride=Chest`. Keep a neutral results-chest label until native opening reveals the reward. Add `RunResults` coverage in `BoneVillageDigSpotTests.cs` and wire fast selectors in both harnesses. Both hosts already share this reader/profile; their existing discovery gates remain unchanged.

- [ ] Reproduce the missing Key result after excavation against bonevil/box1, checking both hosts and five Echo-S mappings. Pin unopened, empty, obtained and non-Key result behavior.
- [ ] Add the smallest native-backed results-chest discovery/naming correction without revealing an unknown buried reward or opening the chest automatically.
- [ ] Verify the actual interaction approach and disappearance after collection; commit.

## Task 4: Review, verify and release

- [ ] Bump runtime, launcher, metadata and release documentation to 0.10.8; independent Opus 5.5/max whole-branch review; resolve material findings.
- [ ] Run the Release gate, all contracts, native integration on both roots/five Echo layouts, and full suites against the exact four packaged payloads.
- [ ] Verify protected paths, install only tested payloads into both runtimes, merge the verified tree without rewriting raw line endings, publish tagged release and both Mod Manager records, and verify all public digests and tagged CI.
- [ ] Write the handoff, remove only verified owned scratch, and run post-cleanup verification. No live game/controller/speech claim without an actual test.
