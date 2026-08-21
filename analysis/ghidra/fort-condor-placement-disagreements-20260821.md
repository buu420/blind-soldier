# Fort Condor placement-preview disagreement audit

Date: 2026-08-21

Scope: static Ghidra analysis of the x86 executable and classification of all
35 disagreement lines from the 2026-08-21 live battle log. The deployed game
and mod were read only.

Analyzed executable:

- `C:\Games\Final Fantasy VII\workingdir\ff7_en.exe`
- MD5 `72DF0999B2FAD9AE2AA721CE67D8C3AB`

Repeatable evidence:

- `analysis/ghidra/DumpCondorPlacementGeometryExact.java`
- `analysis/ghidra/DumpCondorPlacementFlagControlFlow.java`
- `analysis/ghidra/DumpCondorPlacementDisagreementEvidence.java`
- `analysis/ghidra/RunCondorPlacementGeometryExact.cmd`

The [FFNx source](https://github.com/julianxhokaxhiu/FFNx/blob/master/src/ff7_opengl.cpp)
independently names calls within `FUN_005F342C` as the Fort Condor unit-texture
loader. That remains the external module anchor; every predicate and address
conclusion below comes from the executable above.

## Conclusion

The 35 lines are **not 35 geometry disagreements**. They are comparisons made
when the native preview flag did not describe the same state as the managed
snapshot:

| Cause | Count | Live evidence |
| --- | ---: | --- |
| Report overlay owns input, so the native flag is deliberately not recomputed | 33 | 7 samples during `Enemy destroyed` with report state 11; 26 during `Encountered enemy` with report state 1 |
| A hire completes between the preview and the post-hire unit state | 2 | one sample at `(120,650)` and one at `(80,650)`, each immediately followed by `Placed` and a newly selected Attacker |

Every one of the 35 was `managed False, native True`. There were no
`managed True, native False` cases.

The removal-byte correction remains valid, but it cannot explain this capture.
The direct-under hit box is a strict subset of the footprint scan for slots
0..38. Skipping a removing unit in the former changes the final answer only in
slot 39, which the footprint scan omits.

## Code-proven answers to the three open candidates

### Terrain tests a bare point

`FUN_00606F20` builds a temporary candidate and copies the selected unit
record's width and height into it, but `FUN_0060A682` reads only:

```text
candidate + 0x50: cursorX - 256
candidate + 0x52: cursorY - 512
```

It bounds-checks that point against each `0x4C` terrain record and passes the
same two coordinates to `FUN_0060A550`. No width, height, footprint corner, or
candidate extent enters the terrain lookup. Unit extent is used only by the
separate live-unit overlap scan `FUN_00602F7D`.

### The allied-count gate is exactly `0x00C60AD0`

At `0x005FE67D`, `FUN_005FE63C` executes the equivalent of:

```text
cmp dword ptr [0x00C60AD0], 0x14
```

Placement is rejected at 20. There is no second or wider allied counter in
this validator path.

### `0x00CBCC9C` is aligned, but not an asynchronous truth latch

It is a 16-bit aligned field, so the problem is not a torn two-byte load. The
problem is its validity window.

`FUN_005FD958` first gates the entire interactive placement path:

```text
if (modalState == 0) {
    if (reportState == 0) {
        ...
        if (interactionMode < 2) {
            placementFlag = 0;
            ...
            FUN_005FE63C(); // sets it to 1 only if the current point is legal
        }
    }
}
```

Therefore:

- while report state is nonzero, the old flag is neither cleared nor
  recomputed;
- during an ordinary update, an async reader may see the clear-to-set window;
- because the mod reads the flag before the 40 live-unit records, a hire or
  simulation update can make one snapshot combine the flag from one state with
  units from the next.

The native renderer is safe because it consumes the result in game-thread
order. A 100 ms external poll has no such ordering guarantee.

## Classification of the 35 live lines

Runtime log:

`C:\Games\Final Fantasy VII\Reloaded-II\Mods\ff7.accessibility.reloaded\ff7_accessibility_reloaded.log`

### Two completed hires

- `11:38:50Z`, `(120,650)`, allied count 7.
- `11:38:56Z`, `(80,650)`, allied count 8.

Both occur on the sample where the Setting Menu closes. Each line is
immediately followed by `Placed`, the gil deduction, and the new Attacker under
the cursor. The true flag described the pre-placement clear point; the managed
unit array described the post-placement occupied point.

### Seven samples during report state 11

At `11:51:33Z` through `11:51:34Z`, all seven samples are at `(248,678)` and
the probe reports route/report state 11. The game has just displayed `Enemy
destroyed.` The validator is skipped for that state, so the previous true flag
is retained while the managed predicate correctly rejects interaction.

### Twenty-six samples during report state 1

- 20 samples at `(444,698)`, `11:53:39Z` through `11:53:42Z`.
- 6 samples at `(444,702)`, `11:54:12Z` through `11:54:13Z`.

Both runs begin with `Encountered enemy.` and the probe reports route/report
state 1. Again, the validator does not run and the old true flag is retained.

The counts sum to `2 + 7 + 20 + 6 = 35`.

## Resulting code policy

The calculated predicate remains the source used for accessible placement
speech. The native flag is only a diagnostic comparison when all of these are
true:

- the tracker has an established previous snapshot;
- modal state and report state are both zero;
- interaction mode is the ordinary world cursor;
- the Setting Menu was not open in the prior snapshot;
- the allied count did not change in this snapshot.

Placement-band speech is also held while a report overlay owns input. The
overlay is a battle event, not a change in the hill, and saying `nowhere in
this column` under `Encountered enemy` was false context for the player.

For a true native-versus-managed validation, hook immediately after
`FUN_005FE63C` returns and latch the flag, cursor, report/modal state, allied
count, and unit records from the game thread. Polling the standalone flag can
never provide that proof.

## Remaining confidence boundary

The static path proves that the terrain input is a point, the counter address,
the overlap geometry, and the native flag's lifetime. The managed triangle
test uses integer cross products rather than the game's fixed-point angle
routine. The four audited cursor columns agree on the shipped mesh, but that is
not an exhaustive equivalence proof over every point. Nothing in these 35
samples implicates that substitution; all 35 have a complete state-lifetime
explanation above.
