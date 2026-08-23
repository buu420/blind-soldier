# Field navigation beacon coverage audit — 2026-08-23

## Baseline preserved

- Branch: `fix/condor-probe-silence`
- Starting HEAD: `82761f1a639a578ac77c2b88eb970f5d0d837b7f`
- Pre-existing tracked changes belong to Claude and remain present: six unused configuration properties removed from `AccessibilityConfig.cs` and `Configuration/config.json`, plus the matching constant-only default test removed from `Ff7.Accessibility.Reloaded.Tests/Program.cs`.
- Pre-existing untracked artifact/research directories were not modified or removed.
- `FieldNavigationAssistant.cs` is byte-identical to HEAD at the start of this audit.
- During the audit, `Build-DualRuntimePackage.ps1` acquired a separate uncommitted change that
  excludes `Assets\footsteps\real_samples` from packages. That is Claude's shipping-assets side
  of the split; this work will preserve it and will not edit that script.

## Finding 1 — current production call graph

`CreateBeaconCue` has no production caller in the current tree. Its only callers are in `Ff7.Accessibility.Reloaded.Tests/Program.cs`. Both runtime hosts call `CreateSpokenGuidance`:

- x86: `Ff7.Accessibility.Reloaded/Mod.cs`
- x64: `Ff7.Accessibility.Steam2026X64/Runtime/Field/Steam2026FieldNavigationCoordinator.cs`

`FieldNavigationController` also calls `CreateSpokenGuidance` internally for resumed and ladder-related guidance. This establishes the present discrepancy but does not yet establish history or semantic equivalence; those are traced below.

## Finding 2 — repository history

- `git log --all -S CreateBeaconCue` finds exactly one introducing commit:
  `bb7106a99c1caea6d0d71a5e80d7d2ea2e42a903` (`feat: publish Blind Swordsman source`, 2026-08-03).
- `git blame` assigns the entire `CreateBeaconCue` implementation and the entire
  `FieldNavigationBeacon` geometry class to that initial publication commit.
- The x86 `Mod.cs` at that same commit contains no call to `CreateBeaconCue`, no field-navigation
  beacon player, and no field-navigation beacon configuration. The x64 runtime likewise has no
  historical call. Searches for a removed call or player field find no later unwiring commit.
- Therefore the strongest statement supported by version control is: **the method has been
  test-only for the repository's entire recorded history.** There is no recorded production
  regression or unwiring commit. Unversioned development before the first source publication
  cannot be reconstructed from Git.
- Earlier project conversation uses “beacon” both for an active navigation target and for an
  audible cue, so those messages are not evidence that this method was wired. The unambiguous
  decision was already speech-only: on 2026-08-03 Brice rejected the world-map audio beacon
  because regular field pathing did not use one.

## Finding 3 — what the dead entry point does and does not cover

The route state machine is not inside either output method:

- `HandleAction` locks a selected target and starts the native route.
- `UpdateLiveTracking` owns field-transition completion, suppression/recovery, live-target
  removal, movement observation, route progress, ladder state/actions, arrival, interaction
  pause/resume, and route completion.
- `CreateBeaconCue` and `CreateSpokenGuidance` are downstream readers of the already-current
  target and `currentGuidance`.

The two output methods share target locking, `currentGuidance`, `GetBeaconTarget`,
`IsWithinArrivalDistance`, and `ResolveGuidanceWaypoint`. They do **not** share the final output
algorithm:

- `CreateBeaconCue` asks `FieldNavigationMovementObserver.ResolveStickDirection`, turns that
  into a `NavigationBeaconCue`, tracks `lastBeaconPosition`, and classifies the audio pulse as
  correcting/on-course.
- `CreateSpokenGuidance` uses `FormatSpokenRoute` and its connected-run formatter, supports
  predictive turns, and explicitly speaks the required climb direction while mounted.
- `CreateBeaconCue` returns nothing while mounted; the production speech path does not.

Consequently, the controller tests that assert route state through `UpdateLiveTracking` remain
valid, even if a dead beacon call appears as incidental setup. Assertions about a beacon's
direction, Steam Audio vector, movement state, or mounted silence do **not** prove that the
production spoken formatter works. Those assertions are false confidence for the shipped field
navigation output.

This is identical for both runtimes: x64 links the same `FieldNavigationAssistant.cs`, and both
hosts call `UpdateLiveTracking` followed by `CreateSpokenGuidance`; neither host constructs a
field-navigation beacon player.

## Finding 4 — test-by-test disposition

| Existing use | What it currently proves | Correct disposition |
|---|---|---|
| Native ladder entry: beacon returns null | Dead beacon's mounted gate | Delete that assertion; the same test already asserts production speech says `climb down` |
| Trigger-line crossing: beacon returns null | Shared arrival calculation through a dead output | Repoint to `CreateSpokenGuidance` returning null |
| Missing route planner: beacon returns null | No dead direct-audio fallback | Repoint to `CreateSpokenGuidance` returning null; retain the route-unavailable activation assertion |
| Walkmesh waypoint: beacon says right and reports route distance | Dead movement-observer/audio formatter, plus current route distance | Assert production spoken guidance points right and `CurrentRouteGuidance.RemainingDistance` retains the lookahead distance |
| No synthetic control overload | Dead public method requires a transform | Apply the same public-contract check to `CreateSpokenGuidance` and retain the `UpdateLiveTracking` check |
| Distance-increase test's unasserted beacon call | Only primes `lastBeaconPosition`; no route effect | Remove it; route build count and diagnostics are the real assertions |
| Suppressed movement: two beacon directions | Dead movement-observer output; route-state assertion is separate | Capture production spoken guidance, retain the unchanged `CurrentRouteGuidance` assertion, and verify spoken direction remains consistent rather than querying the beacon |
| Reactor-exit test's unasserted beacon call | Only primes dead audio state | Remove it; `UpdateLiveTracking`, portal indices, build count, and field-transition completion cover production state |
| Live object moves: beacon changes to up | Locked live target and route refresh, but dead output formatter | Repoint to production spoken guidance and keep the native-removal/category-recovery assertions |
| Interaction arrival: beacon stops/resumes | Duplicates one existing spoken-null assertion and checks dead output on resume | Drop the duplicate arrival assertion; assert production speech resumes after leaving the interaction radius |
| Non-field module: beacon returns null | Dead output gate | Repoint to `CreateSpokenGuidance` returning null |
| Three `FieldNavigationBeacon` direction/vector tests | Field-only audio geometry that Brice rejected and production never calls | Delete registrations and test bodies |
| Steam Audio/player, object, exit, ladder asset tests using `typeof(FieldNavigationBeacon).Assembly` | Live shared spatial-audio infrastructure; the type is only an assembly locator | Keep the tests and use `typeof(FieldNavigationController).Assembly` as the neutral assembly handle |

After removing the controller method, `lastBeaconPosition` and all of its resets are dead. After
removing the static geometry class, no field-navigation audio code remains. The generic
`NavigationBeaconCue`, `NavigationBeaconPlayer`, `NavigationBeaconSound`, Steam Audio renderer,
and their tests must remain because highway, world-map, object, exit, ladder, and other live
features still consume them.

## Recommended resolution

Choose option (a): migrate every meaningful controller assertion to the production speech entry
point, remove incidental dead-beacon calls, delete the three field-only audio-geometry tests,
delete `FieldNavigationController.CreateBeaconCue`, delete `lastBeaconPosition`, and delete the
static `FieldNavigationBeacon` class. Retain the generic spatial-audio implementation and tests,
using `FieldNavigationController` only as the assembly locator where reflection is required.

This is a deletion/refactor, not a missing production behavior: there is no honest new behavior
test that can fail before the edit because the shipped speech path already exists. The safe test
sequence is therefore characterization plus mutation rather than inventing a source-shape test:

1. Repoint the affected assertions to `CreateSpokenGuidance` and run the relevant suite.
2. Mutate a shared route/arrival/ladder branch briefly and prove the migrated tests fail for their
   named behavior, then restore it.
3. Delete the unreachable field-beacon code and prove no production/test reference remains.
4. Run all four executable suites with the required real runtime/data/source roots.

Adding a reflection test whose only assertion is “the dead method does not exist” would be weak:
it would not prevent a future audio beacon under another name and would test source shape rather
than player behavior.

## Finding 4a — field tests now enter through production speech

The beacon-only assertions have been migrated before deleting production code. Ladder repetition,
trigger-line arrival, missing-planner refusal, walkmesh lookahead, suppressed movement, moving live
targets, interaction pause/resume, and non-field rejection now call `CreateSpokenGuidance`, the
same method both runtime hosts use. Incidental calls that only primed `lastBeaconPosition` were
removed, and the three tests of field-only spatial geometry were deleted. Generic Steam Audio,
player, and proximity-cue tests remain and now use `FieldNavigationController` only as their
assembly locator.

A focused `--field-navigation-output-only` characterization mode passed. Two temporary mutations
then proved the migrated assertions bite: suppressing mounted-ladder output failed on `repeat
guidance should use the native mounted ladder direction`, and suppressing normal route speech
failed the migrated walkmesh test because `right` was absent. Both mutations were restored; a
source diff confirmed `FieldNavigationAssistant.cs` was byte-for-byte back to its pre-mutation
content before the focused suite was rebuilt and passed again.

## Approved scope correction and verified baseline

Brice clarified that continuous destination beacons are rejected for both field and world-map
navigation. Proximity/object/ladder/exit cues, the Floor 60 statue, highway/motorcycle cues, and
the shared Steam Audio infrastructure remain separate features.

Before making any implementation change, the existing linked worktree was built and all four
executable suites were run with the required runtime, data-root, and source-root environment
variables. Baseline result: Reloaded, Steam2026X64, Shared, and Parity each exited 0; all four
builds reported zero warnings and zero errors.

## Finding 5 — the world-map player is wired but can never receive a cue

The x86 and x64 hosts both allocate a `NavigationBeaconPlayer`, pass a beacon interval into
`WorldMapRuntimeContext`, inspect `WorldMapNavigationOutput.Beacon`, and contain `Play`/`StopAll`
scaffolding. That makes the feature look live when reading only the hosts.

The shared controller proves otherwise:

- Every `new WorldMapNavigationOutput(...)` in `WorldMapNavigationController.cs` passes `null`
  for `Beacon`.
- The constructor accepts `beaconInterval` but never stores or reads it.
- `WorldMapRuntimeContext` merely forwards that unused interval.
- `WorldMapNavigationControllerTests.NeverEmitsWorldMapAudioBeaconCues` explicitly requires both
  route activation and an active-route observation to have a null beacon.

Therefore neither runtime can reach its player's `Play` call. It may construct the player and
load/initialize its sound infrastructure, but it never emits the continuous world-map navigation
sound.

History resolves the apparent contradiction. The controller's null-only output, the no-audio
regression test, both hosts' stale player scaffolding, the unused interval, and all three
world-map beacon config keys were already present together in the first public source commit
`bb7106a9` on 2026-08-03. No later commit added the beacon back. The recorded source begins in an
internally inconsistent post-removal state: spoken-only behavior was implemented, while dead
host/config/API remnants survived. This is a removal of unreachable scaffolding, not a change to
what players currently hear.

## Finding 6 — world-map speech and progress cover the complete route lifecycle

The shared `WorldMapNavigationController` is the production state machine used by both runtimes.
It supplies speech for every route state that previously could have justified a directional
beacon:

- category and target selection name the current choice;
- route activation says `Navigation on`, names the target, and gives the first connected-run
  direction;
- an unavailable route, an already-reached target, manual navigation-off, and a target that
  becomes unavailable each have explicit speech;
- a changed connected-run direction and a sustained off-route replan speak the next usable
  direction without repeating `Route updated`;
- returning from world-map combat says `Navigation resumed`, retains the target, and gives the
  current direction;
- native arrival says `Arrived at <target>` and turns navigation off;
- the repeat action names the target and says the exact route-progress percentage even when the
  current straight run has not changed.

The lack of periodic speech while the same straight instruction remains valid is deliberate, not
a silence gap: progress is updated on every observation through `IFieldNavigationProgressSink`,
and an explicit repeat always reports the active route. `NavigationProgressController` provides
the shared enabled state and the 5, 10, 15, or 20 percent interval selected with F5/F6/F7.
Activation starts at zero, normal observations update the percentage, backtracking lowers it,
combat temporarily hides it and the world return restores it, arrival completes it, and route
off/reset deactivates it.

The x86 host forwards every `HandleAction`/`Observe` result to its speech output and constructs an
interval progress sink from the shared progress controller. The x64 coordinator uses the same
controller and the same progress types, forwards every returned speech line through its supplied
speech delegate, and the x64 session handles the same F5/F6/F7 actions. Both hosts therefore have
the same player-facing world-map guidance; x64 is not behaviorally thinner in this path.

There is, however, a test-coverage gap: the shared world-map controller and progress-control tests
are currently run only by the x86 test executable. The x64 assembly compiles the production code
but its test project does not link or invoke those shared tests. Before deleting the stale player
scaffolding, those characterization suites should be linked into the x64 test project and run by a
focused x64 world-map mode as well as its normal suite. This will prove the common state machine is
present and exercised from both runtime builds rather than relying on source inspection alone.

That test gap is now closed in the working tree: both shared characterization suites are linked
into `Ff7.Accessibility.Steam2026X64.Tests`, run by its normal path, and exposed through a focused
`--world-map-only` mode. A fresh x64 build completed with zero warnings/errors and the focused
suite passed against the configured real world-map data and target catalog. The first attempted
build correctly failed because the linked files retained their x86-test namespace; no result from
the stale pre-failure executable was accepted. After calling the linked types by their full
namespace and rebuilding, the new x64 tests executed and passed.

Two temporary shared-code mutations proved that these are behavioral guards rather than merely
linked files. Suppressing the route-start speech made both x86 and x64 fail on `route announces
navigation on`; suppressing `progressSink.Activate(0)` made both fail on `an internal replan keeps
one continuous route progress control`. Each mutation was restored with an exact inverse patch,
both projects were rebuilt, and both focused suites passed again. This directly establishes that
removing the unreachable world-map player cannot silently remove the speech or progress paths
without failing both runtime test executables.

The approved scope boundary is supported by production references. The shared
`navigation_beacon_214_remix.wav` asset remains required by the highway steering cue, and the
generic beacon player/renderer remains used by highway and proximity cues. Removing the world-map
player and the field-only beacon formatter does not remove those separate features.

## Finding 7 — approved deletion is isolated from every retained cue

The production deletion now compiles in both runtime trees. Removed items are limited to:

- `FieldNavigationController.CreateBeaconCue`, its `lastBeaconPosition` state, and the static
  `FieldNavigationBeacon` geometry formatter;
- the world-map output's permanently-null `Beacon` member and unused `beaconInterval` argument;
- x86 and x64 world-map `NavigationBeaconPlayer` construction, play/stop/dispose scaffolding;
- the three world-map beacon configuration keys and the x86 path resolver.

A repository-wide production/test search now has zero references to `CreateBeaconCue`,
`FieldNavigationBeacon`, `lastBeaconPosition`, `WorldMapNavigationBeacon`,
`worldMapNavigationBeaconPlayer`, `ResolveWorldMapNavigationBeaconSoundPath`, or
`beaconInterval`. The configuration JSON still parses successfully.

The retained `navigation_beacon_214_remix.wav` has direct live references from
`HighwaySteeringCueSoundPath`, `HighwayAccessibilityCoordinator`, both runtime project asset
declarations, and shared renderer tests. `NavigationBeaconPlayer` still has live constructors for
highway steering/enemy/truck cues and for field object, exit, ladder, ladder-mount, and Floor 60
proximity cues in the two hosts. This confirms the requested boundary: destination beacons are
gone, while the separate sight-equivalent proximity and minigame cues remain.

Fresh builds after deletion succeeded for x86 and x64. The migrated field-output suite, x86
world-map suite, and x64 world-map suite all passed against the configured real game data.

As a native-boundary sanity check, pinned Ghidra 12.1.2 re-imported and analyzed the exact legacy
runtime used by the tests (`ff7_en.exe`, MD5 `72df0999b2fad9ae2aa721ce67d8c3ab`) and reran
`BlindSoldierFieldNavigationStateEvidence.java`. The script completed successfully with 1,187
references across the field ID, current model, model table/count, object array, event table, and
entity-model globals; all three required core targets were present. This does not substitute for
the managed speech tests, but it confirms that the source of native route/player/object state is
unchanged and that the deletion is confined to the mod's unused audio-output layer.

## Final verification

With `FF7_ACCESSIBILITY_RUNTIME` and `FF7_ACCESSIBILITY_DATA_ROOT` set to the real working
directory and `FF7_ACCESSIBILITY_SOURCE_ROOT` set to this worktree, all four projects were rebuilt
and their executable test hosts were run directly:

- `Ff7.Accessibility.Reloaded.Tests`: build 0 warnings / 0 errors; executable passed.
- `Ff7.Accessibility.Steam2026X64.Tests`: build 0 warnings / 0 errors; executable passed.
- `Ff7.Accessibility.Shared.Tests`: build 0 warnings / 0 errors; executable passed.
- `Ff7.Accessibility.Parity.Tests`: build 0 warnings / 0 errors; executable passed.

No game or 7th Heaven file was modified, and no commit was created.
