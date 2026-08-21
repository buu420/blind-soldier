# Fort Condor placement region and native flag lifetime

Date: 2026-08-21

Scope: static Ghidra analysis of the original x86 FFVII PC executable plus
read-only inspection of the matching `condor.lgp`. No deployed game or mod
files were changed.

Analyzed executable:

- Path: `C:\Games\Final Fantasy VII\workingdir\ff7_en.exe`
- MD5: `72DF0999B2FAD9AE2AA721CE67D8C3AB`

Matching game data:

- `condor.lgp` SHA-256:
  `4ABB4E946B68E3DAC7693ED00C2F73B59A1A47317D7110CD96FCFD5D824DCB6F`
- extracted `id.bin` SHA-256:
  `8A0177C3A0850FE99C7FCE93FEA47F8270C4197AF868C09BC9A9F608F20FA094`
- extracted `vert.bin` SHA-256:
  `EFB721B698668EAEAFF24A82B6F89D220DF00E63EED87089DD536C303EE6D1AD`

Repeatable Ghidra evidence script:

- `analysis/ghidra/DumpCondorPlacementRegion.java`

[FFNx independently identifies calls inside `FUN_005F342C` as Fort Condor
unit-texture loads](https://github.com/julianxhokaxhiu/FFNx/blob/master/src/ff7_opengl.cpp).
That is only the external anchor for module 9. Every predicate, address, field,
and lifetime claim below comes from this exact executable and its matching
archive.

## Confidence labels

- **CODE-PROVEN**: directly read or written by the executable.
- **FILE-VERIFIED**: checked across the matching extracted game data.
- **CODE + FILE DERIVED**: calculated by reproducing the executable's
  predicate against the matching data.
- **INFERRED**: a semantic name for code-proven behavior; the underlying
  numeric behavior is still stated separately.

## Answer in brief

1. Placement is not just a terrain polygon test. It also requires an idle UI,
   no report overlay, no unit under the cursor, fewer than 20 allied units, the
   cursor to be on the permitted side of the deployment frontier, no overlap
   with an allocated live unit, and membership in one of the collision
   triangles.
2. The mod can calculate every legal Y interval for the current X without
   moving the cursor. It has all required static geometry and live state.
3. The legal region is **not** one continuous vertical band. The actual
   `vert.bin` has holes at many X positions, and live units can split a band
   further. A single minimum and maximum would be false information.
4. The player's side is enforced by a Y limit, not by a polygon-side flag.
   During setup the hard limit is `Y <= 671`. During combat it becomes the
   moving frontier at `0x00C60AE8`, with placement requiring
   `Y < frontier`.
5. `0x00CBCC9C` is a frame-local render decision, not a stable asynchronous
   state variable. The game clears it, recomputes it, then renders from it on
   every interactive update. An external 100 ms poll can land in the clear-to-
   recompute window.

The later audit in
`analysis/ghidra/fort-condor-placement-disagreements-20260821.md` classifies
all 35 live disagreement lines. Thirty-three occurred while report state was
nonzero, when the executable does not recompute this flag at all; the other two
were the snapshots in which a hire completed. None is evidence of a terrain or
footprint mismatch.

## 1. Exact world-cursor placement predicate

`FUN_005FE63C` is the complete ordinary-cursor placement validator. In
equivalent pseudocode, a position is legal only when all of these are true:

```text
modalState              == 0
reportOrScriptedState   == 0
unitUnderCursor         == -1
activeAlliedUnitCount   < 20

if phase == 1:          cursorY <= 671
otherwise:              cursorY < deploymentFrontierY

no allocated live unit overlaps cursorX,cursorY
collision triangle exists at cursorX - 256, cursorY - 512
```

When the complete predicate succeeds, `FUN_005FE63C` writes
`*(uint16*)0x00CBCC9C = 1` and returns zero. Any failed condition returns one
and leaves the flag at zero after its caller's per-update clear.

### Direct state used by the predicate

| Address | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `0x00C625E0` | `uint32` | modal/menu owner; must be zero | CODE-PROVEN |
| `0x00C72DEC` | `int16` | report/scripted interaction state; must be zero | CODE-PROVEN |
| `0x00C6097C` | `int16` | unit directly under the cursor; must be `-1` | CODE-PROVEN |
| `0x00C60AD0` | `uint32` | active allied-unit count; must be below 20 | CODE-PROVEN |
| `0x00C625D4` | `uint32` | battle phase; value 1 uses the setup boundary | CODE-PROVEN |
| `0x00C60AE8` | `uint32` | combat deployment-frontier Y | CODE-PROVEN |
| `0x00CBCCC0` | `int16` | world cursor X | LIVE + CODE-PROVEN |
| `0x00CBCCC2` | `int16` | world cursor Y | LIVE + CODE-PROVEN |
| `0x00CBCC98` | `int16` | scratch overlap count | CODE-PROVEN |
| `0x00CBCC9C` | `uint16` | frame-local legal-preview flag | CODE-PROVEN |

The cursor mover clamps the world cursor to X `0..512` and Y `0..1008`.
Pressing Up decreases Y and pressing Down increases Y. There is no additional
X-band test in `FUN_005FE63C`; the collision triangles provide the effective X
boundary.

There is no separate minimum distance from the shed, no distance from an enemy,
and no battle-specific placement-radius test in this path. The side limit,
terrain triangles, and live-unit overlap are the complete spatial tests.

`0x00C6097C` is produced by `FUN_006029FD`. For every allocated normal live
slot it uses this strict hit box, then retains the closest matching slot:

```text
unitX > cursorX - 13 && unitX < cursorX + 13
unitY > cursorY - 10 && unitY < cursorY + 14
```

That is the exact direct-under-cursor test a hypothetical-position evaluator
must reproduce instead of borrowing the global for the real cursor.

### Live-unit exclusion is larger than "unit under cursor"

`FUN_00606F20` builds a temporary position at the cursor and calls
`FUN_00602F7D`. The native call scans allocated live slots `0..38` inclusive;
its upper bound is `0x27` and the loop is exclusive, so slot 39 is not examined
by this particular native test.

For each allocated slot:

```text
unitX       = read_i16(unit + 0x48)
unitY       = read_i16(unit + 0x4A)
halfWidth   = (read_u8(unit + 0x22) + 28) >> 1
heightAbove = read_u8(unit + 0x23)

overlaps when:
    unitX - halfWidth <= cursorX <= unitX + halfWidth
and unitY - heightAbove <= cursorY <= unitY + 22
```

The comparisons are inclusive. The extents at live offsets `+0x22/+0x23` are
copied from unit-data offsets `+0x0D/+0x0E`. The overlap scan checks the
allocated flag only; a dying unit continues to block placement until its slot
is actually cleared.

This is a dynamic source of holes. It includes both sides' normal slot ranges,
apart from the native slot-39 omission above. A hypothetical-Y calculator must
re-evaluate this rectangle test from the live array; it cannot reuse
`0x00C6097C`, because that global describes only the cursor's current position.

## 2. Player-side boundary and combat frontier

### Setup phase

When `*(uint32*)0x00C625D4 == 1`, the executable contains a hard-coded compare:

```text
cursorY <= 0x029F   // 671 decimal
```

That is the setup deployment boundary. The live cursor moves in four-unit
steps, so on the observed `0,4,8,...` lattice the last reachable row below that
limit is Y 668.

### Active combat

Outside phase 1, `FUN_005FE63C` instead requires:

```text
cursorY < *(uint32*)0x00C60AE8
```

`FUN_005FF38A` recomputes `0x00C60AE8` during the active simulation tick:

```text
frontier = 480
for each allocated, non-removing live unit whose native type is below 16:
    candidate = unitY + read_u8(unit + 0x16) * 16
    frontier = max(frontier, candidate)
frontier = min(frontier, 928)
```

The code-proven result is a moving Y cutoff with a minimum of 480 and a maximum
of 928. Calling it the allied deployment front is an **INFERRED** semantic name,
but it is supported by the fact that only the sub-16 unit classes extend it and
that the placement validator consumes it directly.

There is no persistent "player half" bit on the collision triangles. The
setup constant and this live frontier are how the executable prevents building
through the enemy approach.

## 3. Collision records and exact coordinate transform

### Loading

The load chain is code-proven:

```text
FUN_005F4273
  -> FUN_005F3378("data/id.bin")
  -> FUN_005F3160
       collisionCount = read_u16(id.bin + 0)
       FUN_005F434B("data/vert.bin", 0x00C625E8)
```

`FUN_005F434B` copies every `vert.bin` record byte-for-byte into the inline
array at `0x00C625E8`.

For the matching archive:

```text
collisionCount = 333
vert.bin length = 25,308 = 333 * 0x4C
```

Runtime layout:

```text
count     = *(uint32*)0x00C60AA4
record(i) = 0x00C625E8 + i * 0x4C
```

### Placement-relevant record layout

| Offset | Type | Placement meaning | Evidence |
| ---: | --- | --- | --- |
| `+0x20` | `int16` | neighbor across triangle edge 1 | CODE-PROVEN |
| `+0x22` | `int16` | neighbor across triangle edge 2 | CODE-PROVEN |
| `+0x24` | `int16` | neighbor across triangle edge 3 | CODE-PROVEN |
| `+0x28` | 8-byte vertex | triangle A | CODE-PROVEN |
| `+0x30` | 8-byte vertex | triangle B | CODE-PROVEN |
| `+0x38` | 8-byte vertex | triangle C | CODE-PROVEN |
| `+0x40` | `int16` | biased inclusive minimum X | CODE-PROVEN |
| `+0x42` | `int16` | biased inclusive maximum X | CODE-PROVEN |
| `+0x44` | `int16` | biased inclusive minimum Y | CODE-PROVEN |
| `+0x46` | `int16` | biased inclusive maximum Y | CODE-PROVEN |

Each placement vertex is:

```text
struct CollisionVertex {
    int16 x;
    int16 y;
    int16 unusedByPlacement0;
    int16 unusedByPlacement1;
}
```

`FUN_0060A550` reads only the first two signed components. In this exact
`vert.bin`, both unused components are zero in all three vertices of all 333
records.

The bounds are not approximate. A full-file check proved this equation for
every record:

```text
record+0x40 = 0x4000 + min(A.x, B.x, C.x)
record+0x42 = 0x4000 + max(A.x, B.x, C.x)
record+0x44 = 0x4000 + min(A.y, B.y, C.y)
record+0x46 = 0x4000 + max(A.y, B.y, C.y)
```

`FUN_0060A682` first applies those inclusive bounds, then calls
`FUN_0060A550`. The latter performs the native point-in-triangle decision by
checking the point's fixed-point direction angle inside the wedges at vertices
A and B. One edge has an eight-angle-unit tolerance; a full turn is 4096 angle
units. It returns zero for inside and nonzero for outside.

The placement lookup is two-dimensional. Although the temporary object also
receives a Z-like value, `FUN_0060A682` never reads it.

### Cursor-to-collision coordinate conversion

The two nested builders look confusing in decompiled form, but simplifying
their assignments gives the exact lookup point:

```text
collisionX = cursorX - 256
collisionY = cursorY - 512
```

The live-unit overlap test, by contrast, uses the unshifted cursor X/Y.

## 4. Computing the vertical placement region without moving

Yes. There are two safe implementations.

### Exact native-lattice evaluation

For each candidate Y at the current cursor X:

1. apply the global phase/count/UI gates;
2. reproduce the direct unit hit and the live-unit overlap test;
3. convert to `(cursorX - 256, candidateY - 512)`;
4. bounds-test and run the native triangle predicate over the 333 records;
5. group consecutive legal candidates into intervals.

The live cursor uses four-unit steps, so evaluating `0,4,...,1008` needs only
253 candidate rows. This is small even before the record bounds prune most
triangle tests. Porting `FUN_0060A450`, `FUN_0060A4C6`, and `FUN_0060A550`
preserves the native edge tolerance exactly.

### Analytic triangle slicing

For a fixed `collisionX`, intersect the vertical line with each triangle,
union the resulting Y intervals, add 512 to return to cursor coordinates,
apply the setup/combat cutoff, and subtract the live-unit rectangles. This is
faster but must deliberately match the native edge tolerance. For this small
dataset, the exact lattice evaluation is simpler and safer.

The snapshot must be coherent. Read cursor/phase/frontier/count and a sequence
or bookend before and after the live-unit array; retry if those values change.
Reading each field once across an unsynchronized native update can combine two
different game frames.

### The real region has holes

The exact fixed-point predicate was applied to the matching `vert.bin` on the
native four-unit cursor lattice, before live-unit exclusions:

| Cursor X | Terrain-only legal Y intervals | Setup-phase intervals after `Y <= 671` |
| ---: | --- | --- |
| `128` | `484..544`, `652..732`, `792..904` | `484..544`, `652..668` |
| `256` | `420..1008` | `420..668` |
| `260` | `420..476`, `552..1008` | `420..476`, `552..668` |
| `320` | `424..460`, `568..716`, `888..1008` | `424..460`, `568..668` |

This is **CODE + FILE DERIVED** evidence that one minimum and maximum is not a
valid description. At X 260, for example, saying `420 through 1008` would
falsely describe the blocked gap from 480 through 548 as placeable. Allocated
units can then cut additional holes out of any of these bands.

The accessible result should therefore be an ordered interval list, or a
nearest-edge summary that also says how many other bands exist. It should not
collapse the list to one min/max pair.

The capture near X 260 is consistent with both real geometry and the flag
race: Y 440 and 468 are inside the first terrain band; Y 488 is in the real
gap. A false `Blocked` while resting at 440 or 468 can come from sampling the
native flag during its clear window. The log's second-level timestamps are not
fine enough to associate every isolated `Clear` line with the cursor line next
to it.

## 5. Why `0x00CBCC9C` flickers

### All direct writers

Ghidra finds these writes to the 16-bit flag:

| Instruction | Function | Write |
| ---: | --- | --- |
| `0x005F7C56` | `FUN_005F7979` | initialize to zero |
| `0x005FDC20` | `FUN_005FD958` | clear ordinary cursor preview before recomputation |
| `0x005FDF08` | `FUN_005FD958` | clear destination preview before recomputation |
| `0x005FE304` | `FUN_005FD958` | clear accepted destination preview |
| `0x005FE708` | `FUN_005FE63C` | set ordinary placement legal |
| `0x005FE761` | `FUN_005FE718` | set destination legal |

No other function sets ordinary placement legal.

### Per-update lifetime

On the ordinary cursor path, one interactive update does this:

```text
FUN_005FD958
  early input handling may still see the previous completed value
  0x005FDC20: flag = 0
  update cursor/unit-under-cursor state
  call FUN_005FE63C
    if legal: 0x005FE708: flag = 1
  return to the active frame callback
  run simulation/render path
  FUN_005F824A reads the completed flag to draw cursor feedback
```

`FUN_005FD958` is called by the module's active frame callbacks at
`0x005F4A47`, `0x005F4DF4`, `0x005F4F11`, or `0x005F5042`. These are alternate
callback paths, not four independent promises of a fixed refresh rate. Static
analysis proves one clear/recompute sequence per invocation; it does not prove
the user's configured frames per second.

The native renderer is synchronized by call order: it consumes the value after
validation. The Reloaded reader is not. It polls every 100 ms on another
thread, so at a legal stationary point it can observe:

```text
1 after the previous validation
0 after the next update's clear
1 after that update's validation
```

At an illegal stationary point the validator leaves zero. Therefore the
in-frame clear directly creates false `Blocked` reads at legal positions. When
the cursor is moving, separate reads of X, Y, and the flag can also combine
state from adjacent updates and make the flag appear associated with the wrong
coordinate.

This is not a temporary placement object being created and destroyed. It is a
deliberate clear-then-recompute render flag whose lifetime happens to be unsafe
for asynchronous sampling.

### Stable alternatives

There is no second stable native global containing the same decision.

The two correct choices are:

1. **Compute the predicate from a coherent snapshot.** This also supplies the
   full interval list the requested border feature needs.
2. **Latch the native decision on the game thread.** Hook immediately after
   `FUN_005FE63C` returns in `FUN_005FD958`, and copy the cursor X/Y plus the
   result into a mod-owned snapshot with a sequence counter. Entry to
   `FUN_005F824A` is also post-validation, but the post-validator call site is
   the narrower and less ambiguous hook.

Five agreeing asynchronous samples reduce audible chatter, but they do not
turn a frame-local flag into reliable state. Polling can alias repeatedly with
the same clear window, and a multi-field read can still be incoherent.

## 6. Implementation-ready address summary

```text
0x00CBCCC0  int16    cursor X
0x00CBCCC2  int16    cursor Y
0x00CBCC9C  uint16   transient native placement-preview flag
0x00C625D4  uint32   phase; 1 selects fixed setup limit Y <= 671
0x00C60AE8  uint32   active-combat frontier; placement requires Y < value
0x00C625E0  uint32   modal state
0x00C72DEC  int16    report/scripted state
0x00C6097C  int16    current unit-under-cursor slot, -1 for none
0x00C60AD0  uint32   active allied count
0x00CBCCD8           live unit array, stride 0x78
0x00C625E8           collision record array, stride 0x4C
0x00C60AA4  uint32   collision record count
```

All fixed addresses in this report apply only to the executable hash at the
top. Other language executables or builds must be re-anchored before use.
