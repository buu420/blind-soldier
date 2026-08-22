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

`CondorBattleStateReader.ReadCollisionTriangles` now returns `null` when the native collision-record count is zero or invalid. `TryRead` already propagates that as "no coherent snapshot," so neither runtime can hand the speech tracker the pre-load `0 gil / 0 units / cursor 0,0 / nowhere` state. The address map remains unchanged.

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
