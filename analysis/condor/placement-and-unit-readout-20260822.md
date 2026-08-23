# Fort Condor placement and unit readout — 2026-08-22

## Scope and evidence standard

This investigation covers two questions against the x86 `ff7_en.exe` code used by module 9:

1. Whether the Fort Condor reader's zero gil and zero-unit snapshot during the Set Units phase reflects native game state or incorrect addresses/lifecycle assumptions.
2. The complete native placement predicate, including terrain, occupancy, cursor granularity, and the relationship between the placement validator and unit-under-cursor selection.

Findings below are appended as they are established. An address is described as proved only when its producer or consumer is visible in the native code; runtime-log observations alone are identified separately.

## Investigation status

Complete for Parts 1 and 2. Native initialization, gil, unit allocation, placement, occupancy, cursor movement, and hire-position paths were traced. The demonstrated pre-initialization speech defect now has a shared-reader regression and fix.

## Finding 1 — the gil address is correct; the first spoken zero is pre-initialization state

**Proved from native code.** The reader's working-gil address, `0x00CBC7E0`, is the address the game itself uses during the Fort Condor battle:

- `FUN_005F7756`, the module-session initializer, copies the persistent player gil at `0x00DC08B4` into `0x00CBC7E0`.
- `FUN_00609748`, which draws the Fort Condor gil counter, formats its value from `0x00CBC7E0`.
- `FUN_00604208` compares `0x00CBC7E0` with the highlighted unit's price to decide affordability.
- `FUN_00604009` subtracts that price from `0x00CBC7E0` after a hire.
- `FUN_005F7818`, the module-session exit path, copies `0x00CBC7E0` back to `0x00DC08B4`.

There is therefore no replacement gil address to wire. A legitimate save with 9436 gil must become 9436 at `0x00CBC7E0` once `FUN_005F7756` has executed its copy.

**Proved from native ordering plus the current x64/x86 observation path.** Module 9 becomes observable before its module initializer has finished. The speech tracker currently treats its first readable module-9 snapshot as fully initialized and immediately constructs a status line. At that instant the module globals can still contain their zero-initialized values: gil 0, counts 0, cursor 0,0, and no loaded collision records. The later `Set units.` and `248, 96` lines in the supplied session are the game completing that initialization, not the player losing 9436 gil.

The supplied log does **not** show gil being re-read as zero for two minutes. The `entered module 9 ... 0 gil` diagnostic and the first spoken status are both made from the one entry snapshot. Later automatic lines report cursor/ground changes but do not include gil, and Allies/Enemies navigation reports only the unit lists. Thus the evidence establishes one premature zero snapshot, not a persistent zero address.

**Conclusion for part 1(a):** the zero-gil announcement is a reader lifecycle defect, not an address defect. Speech must be held until the native setup state is ready; substituting another gil address would be wrong because the battle intentionally maintains and mutates its own working copy.

## Finding 2 — no enemy units exist during the Set Units phase

**Proved from native code.** `FUN_005F7979`, the new-battle initialization routine:

- clears allied count `0x00C60AD0` and enemy count `0x00CBC7A4`;
- calls `FUN_005F2BF0`, which clears the live-unit storage;
- calls `FUN_00607570`; and
- queues the `Set units` message.

The phase value is `0x00C625D4`. `FUN_005F7756` sets it to `1` for setup. In that phase, `FUN_00607570` does not seed either side. The enemy-wave allocator `FUN_00607727` explicitly refuses to instantiate an enemy while `0x00C625D4 == 1`. Once setup ends it allocates enemies in slots 20 through 39 and increments `0x00CBC7A4`. Player hires are created by `FUN_00604009` in slots 0 through 19 and increment `0x00C60AD0`.

**Conclusion for part 1(b/c):** during `Set units`, `0 enemies` is the truth. `0 allies` and `0 live slots` are also true until the player hires a unit. The correct state is:

- phase: `0x00C625D4` (`1` means Set Units/setup);
- allied count: `0x00C60AD0`;
- enemy count: `0x00CBC7A4`;
- live-unit array: `0x00CBCCD8`, stride `0x78`, ordinary player slots 0–19 and enemy slots 20–39;
- battle working gil: `0x00CBC7E0`, initialized from and later copied back to `0x00DC08B4`.

The initializer clears a wider 70-record arena, but the normal player/enemy allocators and the placement scans use the 40 ordinary battle slots above. There is no code evidence that the reader should enumerate all 70 as fielded units.

## Finding 3 — native placement includes occupancy in two separate ways

**Proved from native code.** `FUN_005FE63C` is the complete placement-preview decision. For the ordinary placement path it requires all of the following:

1. normal cursor mode and no report overlay;
2. no selectable unit directly under the cursor;
3. fewer than 20 allied units;
4. the setup/combat vertical boundary to admit the cursor;
5. `FUN_00606F20` to accept a temporary candidate built at the cursor coordinate.

`FUN_00606F20` then rejects the candidate when either:

- `FUN_00602F7D` finds an overlap with an allocated live-unit footprint; or
- `FUN_0060A682` cannot find a terrain/collision polygon containing the candidate point.

This means occupancy is not merely a visual convention layered over a terrain-only result. It is part of the game's actual placement predicate, through two distinct tests:

- **Unit-under-cursor selection box:** `FUN_006029FD` scans all 40 ordinary slots, skips a unit whose removal byte `+0x05` is nonzero, and selects a unit when its center is strictly within 13 horizontal units and the asymmetric vertical interval from 10 above to 14 below the cursor.
- **Placement-overlap footprint:** `FUN_00602F7D` scans slots 0 through 38, tests allocated records, and rejects when the candidate center lies in an existing unit's larger footprint. Horizontally the half-width is `(existing width + 28) >> 1`; vertically it runs from `existing Y - existing height-above` through `existing Y + 22`, inclusive. A removing/dying unit continues blocking through this footprint until its slot is deallocated.

Consequently there can be a position where no unit is selected under the cursor but placement is still illegal because the cursor lies inside the larger overlap footprint. The truthful speech branch there is `Cannot place`, not unit information.

## Finding 4 — the current region model includes the native occupancy checks

**Source comparison.** `CondorPlacementRegion.IsLegalAt` is not terrain-only despite the class name. It applies the setup/combat Y boundary, the native direct-under-cursor selection box, the slot-0-through-38 overlap scan, and then its reconstructed terrain-polygon test. Its occupancy arithmetic and removal handling agree with the native routines above.

The terrain half is an independent reconstruction using integer point-in-triangle cross products. The game calls `FUN_0060A682`, whose edge handling is not implemented as the identical expression. The existing live comparison found zero disagreements in Brice's session, which is useful runtime evidence, but it is not a proof of bit-for-bit equivalence at every polygon edge. The model is therefore suitable for the simple `Can place` / `Cannot place` answer, with the honest residual risk confined to exact terrain boundaries.

The unit-information branch should use the native `UnitUnderCursor` result (or the faithfully reconstructed direct-selection box), not exact coordinate equality and not the wider placement-overlap footprint.

## Finding 5 — there is no placement tile or quantized cell

**Proved from native code.** `FUN_005FE91B` applies cursor movement as integer deltas in the same coordinate system stored at `0x00CBCCC0/2`. In the ordinary-speed path a directional update changes the cursor or camera origin by one coordinate unit per call. In the accelerated path it uses the current horizontal and vertical repeat increments at `0x00CBF2BC` and `0x00CBF2C0`. It then writes `camera origin + camera-relative cursor`; it never rounds, divides, masks, or snaps the result to a cell.

`FUN_005FE771` invokes that movement path repeatedly as a held direction begins repeating. A reader sampling at 100 ms therefore observes the sum of several native per-frame changes. The 16- and 2-unit differences in the log are sampling/repeat artifacts, not two different tile sizes.

**Proved from the hire path.** `FUN_00604009` passes the current cursor coordinates to `FUN_00607123`, and `FUN_00607123` stores those exact 16-bit values at live-unit offsets `+0x48` and `+0x4A`. There is no intervening placement-grid conversion.

**Conclusion for part 2(b):** Fort Condor placement is continuous integer-coordinate placement, not tile placement. Exact coordinate equality is insufficient for occupancy. Use the native direct-selection hit box to decide whether to speak unit information, and the larger native footprint overlap to decide `Cannot place` when no unit is directly selected.

### Consequence for `CondorPlacementRegion`

`IsLegalAt(snapshot, x, y)` does not quantize and is the right entry point for the requested one-position answer. `LegalIntervals`, however, scans with `CursorStep = 4`. Four is the common held-input repeat delta seen by a 10 Hz reader, not a native grid. A one-frame direction press reaches the one-unit path in `FUN_005FE91B`, so the executable can evaluate rows between those four-unit samples, including setup rows 669 through 671. The interval list can therefore skip narrow edge rows and must not be presented as a complete native placement map.

This matters less once the band/nearest-row narration is removed: the requested `Can place` / `Cannot place` decision calls `IsLegalAt` at the exact live cursor coordinate and does not depend on `LegalIntervals`. The constants and comments claiming a four-unit placement step should nevertheless be treated as an approximation, not game law.

## Recommended correction for the premature zero status

The address table should remain unchanged. The narrowest source-grounded readiness fix is for `CondorBattleStateReader.TryRead()` to return no snapshot while the collision-record count at `0x00C60AA4` is zero or otherwise invalid, rather than constructing a snapshot with an empty terrain list. Every real Fort Condor battlefield loads collision records; `FUN_005F7756` copies gil before the load, so a positive collision count guarantees the working-gil copy has already happened. This directly prevents the reported `0 gil` pre-initialization announcement.

There remains a much narrower asynchronous window between geometry loading and `FUN_005F7979` finishing its unit/cursor initialization. If the reader is to promise a completely coherent opening status, it should additionally require one fully initialized setup snapshot before starting speech: phase `1`, interaction mode `1`, and a positive collision count, then confirm those invariant fields on the next 100 ms sample before announcing. Gil must not be used as a readiness test because zero gil is legal. Cursor `(0,0)` must not be used as a readiness test because zero is a reachable coordinate. The second-sample confirmation costs 100 ms and avoids treating an in-progress native initialization as player-visible state.

This is a lifecycle correction, not a substitute-address correction. It should be shared by x86 and x64 through the common reader/tracker code.

## Implemented shared readiness correction

`CondorBattleStateReader.ReadCollisionTriangles` now returns the battle's cached collision triangles when the native collision-record count is zero or invalid. Before this battle has loaded geometry the cache is `null`, so neither runtime can hand the speech tracker the pre-load `0 gil / 0 units / cursor 0,0 / nowhere` state. After geometry has loaded, retaining the cache prevents a later zero/torn count from suppressing the battle-result snapshot. `Reset()` drops the cache on module exit, so terrain cannot cross battle epochs. The address map remains unchanged.

The regression was written first with a readable, zero-initialized module-9 address space. Before the production change it failed with:

> A module 9 snapshot without loaded battlefield geometry must not be spoken.

After the shared-reader change, the focused portable test passes:

```text
FFVII Fort Condor research probe silence tests passed.
```

This deliberately fixes the demonstrated entry-snapshot defect without treating legitimate zero gil, zero hired allies, or zero setup-phase enemies as invalid.

## Verification

- Red phase: the new pre-load snapshot regression failed on the old reader with the intended exception.
- Green phase: `--condor-probe-silence-only` passed from an isolated output tree.
- Dual-runtime contract: `--dual-runtime-sources-only` passed.
- x86 Reloaded project build: succeeded with zero warnings and zero errors.
- Steam 2026 x64 project build: succeeded with zero warnings and zero errors.
- `git diff --check`: passed; the only output was Git's existing CRLF-to-LF working-copy warning.

The data-backed full Condor suite was not run because it requires the external game-data/runtime root, and this task explicitly forbids reading anything under `C:\Games\Final Fantasy VII`. The focused regression is self-contained and both production assemblies were built without accessing that directory.

## Follow-up — confirming setup initialization across two samples

The geometry gate closes the observed entry failure but not the shorter interval between the collision load and `FUN_005F7979` finishing its unit/cursor initialization. The approved follow-up treats this as snapshot coherence in `CondorBattleStateReader`, so automatic speech, the K status path, navigation, and module-entry logging all share one gate on both runtimes.

The native code does **not** prove that every observable battle must begin in phase 1. `FUN_005F7979` contains a conditional path that writes phase 2 when `0x00CBC80C == 3`; that is enough to reject phase 1 as a permanent prerequisite even though the normal new-battle path uses phase 1. The gate therefore works as follows:

- native collision count must be positive and interaction mode must be one of the game's active modes 1, 2, or 3;
- a phase-1 setup snapshot is withheld until the same initialization signature is observed at least 100 ms later;
- any non-phase-1 snapshot meeting the geometry/mode conditions is treated as already initialized and accepted immediately;
- a failed read or changed setup signature restarts the candidate;
- after confirmation the battle is never re-gated, including when collision count later reads zero and cached terrain carries the result snapshot;
- `Reset()` clears both terrain and confirmation state.

### K status key audit

The early K press is not presently preserved. On x86, `TickCondorBattleReader` samples the rising edge into a local `statusRequested`, then returns when `TryRead()` yields `null`. On x64, the session passes the one-frame rising edge into `ObserveCondorBattle`, which likewise returns on `null`. Navigation actions survive because both hosts bank them; K has no equivalent state. Since failed reads or a changed candidate can extend confirmation beyond one interval, relying on the later automatic status is not a bounded answer to the key the player pressed. Both hosts must bank K until the first accepted snapshot, then clear it only after delivering the requested status.

### Information preserved during the 100 ms hold

Phase 1 does not autonomously spawn enemies or advance combat, and it cannot finish without player input. The second snapshot carries the current cursor, funds, units, and placement state. The speech tracker's first accepted snapshot must additionally expose any current result latch, banner, or already-open Setting Menu instead of making those values an unseen baseline. This protects `Set units.` and also fails safely if the first accepted observation is unexpectedly later in the battle.

### Implemented readiness contract (uncommitted)

`CondorBattleStateReader` now owns the confirmation epoch and accepts an injected `TimeProvider`. A phase-1 signature is `(phase, interaction mode, native collision count)`; the first sample is held, a matching sample before 100 ms remains held, and a matching sample at or after 100 ms confirms the battle. An unreadable sample or a changed signature clears the candidate. Modes 1 through 3 are accepted as the native handler's live modes. A non-phase-1 snapshot with a positive native collision count and a valid mode is accepted immediately. After confirmation no late transient value re-enables the gate, while `Reset()` clears both the confirmation and candidate for the next battle.

The cached-terrain amendment remains correct. Returning `collisionTriangles` for an invalid late native count preserves the result snapshot after geometry has loaded; before the first successful geometry load the same expression is `null`, and `Reset()` prevents terrain crossing battle epochs. The initialization gate separately requires the **native** count to be positive until confirmation, so the cache cannot accidentally confirm a pre-load battle.

`CondorBattleSpeechTracker` now preserves the first accepted snapshot's status, result latch/banner and already-open Setting Menu, and initializes the battlefield navigator from that snapshot before banked navigation is applied. The result and menu are remembered after being emitted, so the next sample does not repeat them.

K is now a tracker-owned pending request shared by both runtimes. Each host banks the edge before throttling or calling `TryRead()`. Null snapshots retain it. The first accepted call consumes it only because `Observe()` emits the opening status in that same call; later requests emit an interrupting status directly. `Reset()` drops a request belonging to a battle that ended. A dual-runtime wiring regression checks that both host methods request before reading and retain/consume the pending state.

`CondorPlacementRegion.Describe` and the test for its rejected band wording were removed. `LegalIntervals`, `PlacementIntervals` and their geometry regressions remain because they still model and verify the native placement region even though production speech now answers only for the live cursor coordinate.

### Verification for the uncommitted follow-up

- TDD red: the first phase-1 sample was accepted immediately before the gate; the focused test failed on that exact assertion.
- TDD red: the first accepted snapshot returned only one line before preservation; the regression expected status, `Set units.`, Setting Menu and highlighted hire row and failed with `expected 4, got 1`.
- TDD red: the tracker had no K request API before banking; the regression failed to compile on the absent `RequestStatus`, `HasPendingStatusRequest` and `ConsumeRequestedStatus` members.
- `dotnet build Ff7.Accessibility.Reloaded\Ff7.Accessibility.Reloaded.csproj --no-restore`: succeeded, 0 warnings, 0 errors.
- `dotnet build Ff7.Accessibility.Steam2026X64\Ff7.Accessibility.Steam2026X64.csproj --no-restore`: succeeded, 0 warnings, 0 errors.
- x86 portable Fort Condor initialization/probe suite (`--condor-probe-silence-only`): passed.
- dual-runtime compile/wiring contract (`--dual-runtime-sources-only`): passed.
- Steam 2026 x64 module suite (`--module-tests-only`, now including the shared initialization/K tests): passed.
- The older Condor reader tests ran through every state/speech test before their first external `condor.lgp` fixture and found no regression; the data-backed remainder was intentionally not run because this task forbids accessing `C:\Games\Final Fantasy VII`.
- `git diff --check`: exit 0; only the repository's existing CRLF conversion warnings were printed.

All changes remain uncommitted for review. Nothing under the game directory was read or modified.

## Follow-up — suppressing the duplicate opening cursor

The initialization gate made the first accepted cursor trustworthy, but the speech tracker still
preserved the older pre-gate behavior of leaving `lastCursorKey` empty. The opening status already
said the coordinates and either the unit under the cursor or the placement answer; the next
unchanged sample consequently treated the same cursor as new and repeated it as a standalone line.

The opening status loses no cursor information when it serves as the baseline. `DescribeStatus`
always adds `cursor at X, Y` before branching to either `on <unit, current HP of max>` or
`Can place` / `Cannot place`. A cursor over a unit therefore retains both its coordinates and the
same unit/HP detail that the standalone readout would have supplied.

Priming is safe for every snapshot that can reach `CondorBattleSpeechTracker.Observe`. A phase-1
snapshot was stable across two samples at least one read interval apart. A non-phase-1 snapshot is
the already-initialized path: the native initializer has advanced beyond setup, and the reader still
requires loaded geometry and a valid interaction mode. There is no separate unconfirmed snapshot
path into the tracker.

The tracker now derives the opening baseline and every later comparison through the same
`CursorKey` helper: X, Y, native unit-under-cursor slot and reconstructed placement legality. The
regression was written first and failed on the old code with:

> unchanged opening cursor is not repeated: expected 0, got 1.

The shared regression also proves that moving from `(248,96)` to `(248,112)` still emits
`248, 112. Cannot place.`, and that entering over a Fighter says both `cursor at 248, 96` and
`Fighter, 200 of 200` before suppressing only the unchanged follow-up.

Verification after the fix:

- focused x86/shared Fort Condor initialization suite (`--condor-probe-silence-only`): passed;
- Steam 2026 x64 module suite, which links the same regression (`--module-tests-only`): passed;
- dual-runtime shared-source contract (`--dual-runtime-sources-only`): passed;
- x86 Reloaded production build: succeeded with zero errors;
- Steam 2026 x64 production build: succeeded with zero errors.

NuGet emitted `NU1900` warnings because its vulnerability service index was unavailable; no
package restore or game data was required for these focused checks. Nothing under the game
directory was read or modified, and the change remains uncommitted for review.

## Follow-up — cursor repeat and the reported extra movement

### Finding 6 — the 21-to-24-unit motion per 100 ms is native

**Proved from native code.** The held-key acceleration is controlled by the 16-bit counter at
`0x00CBC7BC`, not by the cursor-motion magnitudes at `0x00CBF2BC/0x00CBF2C0` as the earlier
Finding 5 loosely implied. `FUN_005FD958` tests the held-direction mask at `0x00C72E80 &
0xF000`, calls `FUN_005FE771` while a direction remains held, and clears `0x00CBC7BC` when no
direction is held (`0x005FE30D` through `0x005FE341`).

`FUN_005FE771` increments the counter, clamps it at 16, and calls the movement dispatcher this
many times on successive native updates:

| Consecutive held-direction update | Movement-dispatch calls | Cumulative coordinate movement |
| ---: | ---: | ---: |
| 1 | 1 | 1 |
| 2 | 1 | 2 |
| 3 | 2 | 4 |
| 4 | 4 | 8 |
| 5 and later | 4 per update | +4 per update |

The exact branches are the comparisons against 3 and 4 at `0x005FE7AC` and `0x005FE7CD`; the
fourth dispatch is at `0x005FE7EC`. `FUN_005FE8CF` sends each call to `FUN_005FE91B`, and the
ordinary keyboard path in `FUN_005FE91B` changes the cursor/camera by one coordinate unit per
call. Thus full native repeat begins on the **fourth consecutive module update**, after an
initial `1, 1, 2` ramp. A player who can see the screen and holds the same direction gets the
same movement.

If native input bit `0x80` is held at the same time, `FUN_005FD958` deliberately calls
`FUN_005FE771` a second time in that update. That is a separate native button combination, not
ordinary arrow repeat; Blind Soldier does not synthesize it, and the supplied 21-to-24-unit
cadence matches the ordinary one-call path rather than a doubled path.

The supplied 21-to-24-unit deltas per 100 ms are the expected sum of several native updates
sampled asynchronously at 10 Hz. They are not evidence that Blind Soldier holds a direction.
The apparently discrete `348,433 -> 348,456` movement is also consistent with the native
sequence: the first read caught the first one-unit update at 433, while the next read caught
the remaining 23 units of a 24-unit run ending at 456. A human tap is not a one-update game
event; if the physical key remains down across several native updates it enters the same repeat
path as a hold.

### Finding 7 — a minimal tap is one unit; there is no fixed wall-clock tap distance

**Proved from native code.** If a direction is present for exactly one module update and absent
on the next, the cursor moves exactly one coordinate unit and the counter is reset. Two updates
move two units total; three move four; four move eight. The threshold is therefore four native
updates, not a Windows keyboard-repeat delay and not a fixed number of milliseconds. The live
capture's 10 Hz sampling cannot tell exactly when within an interval the key went down or up, so
it cannot assign one fixed distance to every ordinary human tap.

`0x00CBF2BC/0x00CBF2C0` belong to the game's separate cursor-motion-accumulator path.
`FUN_005FB20A` initializes each to one and derives them from half the absolute pointer delta;
`FUN_005FADD4` enters that path when `0x00CBF2A8` is active. They are not the held-arrow repeat
threshold that explains this report.

### Finding 8 — Blind Soldier's key polling cannot hold a Condor direction

**Proved from mod source and the native input chain.** The Condor hotkeys poll K, U, O, J, L
and I through `GetAsyncKeyState`, retain only the high-order current-down bit, and pass that
boolean to an edge tracker (`Ff7.Accessibility.Reloaded/Mod.cs:1894-1912,1990-2015,4957-4960`;
`Ff7.Accessibility.Steam2026X64/Runtime/Input/Steam2026ForegroundInputAdapter.cs:62-77`). These
calls read state; they do not synthesize a key-down or alter the physical current-down bit.

There is one precision worth recording. Calling `GetAsyncKeyState` can make its unreliable
low-order “pressed since the last query” bit unavailable to another caller for the **same**
virtual key. Merely masking that bit does not undo the query. That cannot affect this movement:

- the Condor path uses only the high-order bit;
- it asks about letter/function virtual keys, not `VK_LEFT`/`VK_UP`/`VK_RIGHT`/`VK_DOWN`;
- the numeric value `0x4B` is K in the virtual-key namespace even though `0x4B` is also the
  DirectInput scan code used by the mod's synthetic left arrow; `VK_LEFT` is `0x25`; and
- native module 9 rebuilds its held-direction mask `0x00C72E80` from the game's input source
  every update (`FUN_005FD958`), independently of Blind Soldier's hotkey edge trackers.

The only x86 polling path that consults a low-order bit is the Ctrl+Q transition diagnostic
(`Ff7.Accessibility.Reloaded/Mod.cs:1470-1524`). Those keys are unrelated to the four Condor
directions. No production source uses `SetKeyboardState`, `keybd_event`, a keyboard hook, or a
window-message key-down path.

### Finding 9 — neither synthetic-arrow owner can drive module 9

**Proved from mod source.** The one production input injector is
`Win32HighwayKeyboardInputSink`, shared by highway auto-steering and field/world auto-walk. It
uses `SendInput` scan-code transitions
(`Ff7.Accessibility.Reloaded/HighwayAutoSteeringController.cs:297-412`). Both owners relinquish
their keys before any steady module-9 processing can drive them:

- Highway accessibility is active only when the current module equals the highway module.
  Every other module calls `Reset`, which calls `ReleaseAutomaticDirection` and then
  `ReleaseAll` (`Ff7.Accessibility.Reloaded/Mod.cs:4900-4920`;
  `Ff7.Accessibility.Reloaded/HighwayAccessibilityCoordinator.cs:113-139,288-339`). The x64
  session uses the identical coordinator and exact module test
  (`Ff7.Accessibility.Steam2026X64/Runtime/Steam2026ResearchSession.cs:1052-1076`).
- x86 auto-walk resolves ownership only for field module 1 or world module 3. Module 9 resolves
  to `None` and calls `Suspend`, which releases every owned direction
  (`Ff7.Accessibility.Reloaded/Mod.cs:4671-4699`;
  `Ff7.Accessibility.Reloaded/NavigationAutoWalkController.cs:162-174`).
- x64 field navigation resolves module 9 to `Suspended` and calls the same release path
  (`Steam2026FieldNavigationCoordinator.cs:272-323,827-851,898-918`). Its world coordinator
  resets or suspends auto-walk as soon as the module is no longer the world map
  (`Steam2026WorldMapAccessibilityCoordinator.cs:191-217,346-375`).

On a module transition, a previously owned synthetic key is released on the next host pass. A
failed `SendInput` key-up is the only exceptional way an owned key could remain down; the
controller retries cleanup, retains the failed ownership state, and logs/faults instead of
pretending it succeeded (`HighwayAutoSteeringController.cs:71-108,112-142,159-240`). There is
no such failure in the supplied evidence, and a residual key would not explain the exact native
`1,1,2,4` ramp seen here.

### Finding 10 — the mod does not write the Condor cursor or camera state

**Proved by production-source reference audit.** `CondorCursorMover.TryMoveTo` always logs a
refusal and returns false; it never calls its writer
(`Ff7.Accessibility.Reloaded/CondorCursorMover.cs:52-90`). The only production references to
`0x00CBCCC0` are the battle reader and the disabled-by-default research probe. The camera origins
`0x00C60B00/04` and scroll accumulators `0x00C74C38/3C` appear only in comments and analysis,
not in a production write path.

The sole x86 production call to `TryWriteInt32` is the Echo-S reactor timer override at
`Mod.cs:8095-8098`; it is gated to field 125 and is unrelated to module 9. The x64 translated
writer exists as a capability but has no Condor-cursor caller. The research probe is also
read-only and defaults off (`AccessibilityConfig.cs:161`;
`Mod.cs:2039-2075`). The x64 native system-menu direction hook is observational: it calls the
game's original callback first and only then records the direction code
(`Steam2026NativeSystemMenuHookSet.cs:187-213`).

### Conclusion for the reported symptom

The movement should be left unchanged. It is the game's own cursor repeat, and slowing or
replacing it would make the blind player's controls differ from a sighted player's. The actual
regression was the speech queue describing every intermediate 10 Hz sample after it was already
obsolete. Cursor speech should therefore coalesce/interrupt aggressively while event lines such
as banners and casualties remain queued; movement itself needs no correction.

### Evidence and verification for this follow-up

- `analysis/ghidra/DumpCondorCursorRepeatEvidence.java` records the native globals, callers,
  instructions and decompilation used above; `RunCondorCursorRepeatEvidence.cmd` replays it
  against the existing read-only Ghidra project.
- The headless Ghidra replay completed with exit code 0 after the final script change.
- The production-source audit found no second `SendInput` implementation and no Condor cursor,
  camera-origin or scroll-accumulator writer.
- `git diff --check` passed for this report; Git emitted only the repository's existing CRLF
  conversion warning.
- No production speech/input code and nothing under `C:\Games\Final Fantasy VII` was modified.
