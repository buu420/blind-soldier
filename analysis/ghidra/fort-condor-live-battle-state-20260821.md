# Fort Condor live battle state

Date: 2026-08-21

Scope: static, read-only Ghidra analysis of the original x86 FFVII PC
executable used by the 2013/FFNx runtime. No deployed game or mod files were
changed.

Analyzed executable:

- Path: `C:\Games\Final Fantasy VII\workingdir\ff7_en.exe`
- Size: 5,997,056 bytes
- MD5: `72DF0999B2FAD9AE2AA721CE67D8C3AB`
- SHA-256: `4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`

Repeatable evidence script:

- `analysis/ghidra/DumpCondorLiveBattleState.java`

[FFNx independently identifies six calls inside `FUN_005F342C` as Condor
unit-texture loads](https://github.com/julianxhokaxhiu/FFNx/blob/665b845f030d08d3e12e4dd1bf08ac2dad3e685f/src/ff7_opengl.cpp).
That was used only as an external anchor into module 9; every address and
field below was then established from the matching executable's own readers
and writers in Ghidra.

## Confidence labels

- **LIVE + CODE**: observed in the supplied live battle and independently
  established from native reads/writes.
- **CODE-WRITE**: the executable directly writes the value for the stated
  purpose.
- **CODE-READ**: the executable directly consumes the value for the stated
  purpose.
- **CODE-DERIVED**: a formula composed solely from code-verified fields and
  native pointer arithmetic; there is no single global holding the result.
- **INFERRED**: semantic interpretation not fully proved. These are kept out
  of the implementation-ready address tables wherever possible.

## Result summary

The cursor address map was right. The other live probe results did not change
because the player either never completed the native open condition or the
probe sampled the right address at the wrong width:

- `0x00C74C50` is a **32-bit interaction mode**, not a byte.
- `0x00C625E0` is a **32-bit modal/overlay state**, not a byte.
- `0x00C6097C` is a **signed 16-bit selected-unit slot**, not a byte.
- OK does not write modal state 7 immediately. It increments a signed 16-bit
  one-shot request at `0x00C60AE4`; a later callback builds the menu and then
  writes `0x00C625E0 = 7`.

The most important new result is the live battle-unit array:

```text
base       = 0x00CBCCD8
slot(i)    = base + i * 0x78
slots 0-19 = player/allied units
slots 20-39 = enemy units
```

Each allocated slot supplies the unit type, side, current and maximum HP, and
world X/Y in the same coordinate space as the cursor. No heuristic memory scan
is required.

## 1. Confirm and Setting Menu open path

`FUN_005FD958` is the per-frame module-9 input/UI coordinator. Its OK path is:

```text
if (*(uint32*)0x00C625E0 == 0 &&       // no modal overlay
    *(uint32*)0x00C72DEC == 0 &&       // no report/message interaction
    (*(uint32*)0x00C74C54 & 0x20) &&   // OK rising edge
    *(uint32*)0x00C74C50 == 1 &&       // ordinary cursor mode
    *(int16*)0x00C6097C == -1)         // no live unit under cursor
{
    if (*(uint16*)0x00CBCC9C != 0)     // valid empty placement point
        ++*(int16*)0x00C60AE4;         // request Setting Menu
}
```

`FUN_00603711`, called later from `FUN_005F7E10`, consumes the request. It
clears `0x00C60AE4`, rechecks that no modal/message owns the UI, and calls
`FUN_0060378B`. That builder populates the unit list and writes
`*(uint32*)0x00C625E0 = 7`.

Implementation-ready state:

| Address | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `0x00C74C54` | `uint32` | Condor rising-edge input; OK is bit `0x20` | CODE-WRITE/READ |
| `0x00C74C50` | `uint32` | interaction mode: `1` cursor, `2` Ally Unit menu, `3` destination cursor | CODE-WRITE/READ |
| `0x00C625E0` | `uint32` | modal state; `0` none, `7` Setting Menu | CODE-WRITE/READ |
| `0x00C6097C` | `int16` | unit slot under/selected by cursor; `-1` none | CODE-WRITE/READ |
| `0x00CBCC9C` | `uint16` | current cursor position is a valid empty placement point | CODE-WRITE/READ |
| `0x00C60AE4` | `int16` | one-shot Setting Menu open request | CODE-WRITE/READ |

Therefore the observed `mode=1`, `modal=0`, `selected=0xFF`, `row=0` session
does not disprove the addresses. In particular, reading only one byte of the
signed selected-unit value turns native `-1` into `255`.

## 2. Live battle units, both sides

The allocator `FUN_00607517` walks the array by adding `0x3C` to a `short*`,
proving a byte stride of `0x78`. Hiring calls it with `[0, 20)`; enemy spawning
calls it with `[20, 40)`. The renderer and combat code use the same split.

For `slot = 0..39`:

```text
unit = 0x00CBCCD8 + slot * 0x78
```

| Offset | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `+0x00` | `uint16` | allocated/active slot flag; zero means free | CODE-WRITE/READ |
| `+0x05` | `int8` | removal/death-animation state; zero is normal, `-1` starts removal | CODE-WRITE/READ |
| `+0x06` | `uint16` | native unit type identifier | CODE-WRITE/READ |
| `+0x10` | `uint8` | current HP | CODE-WRITE/READ |
| `+0x11` | `uint8` | maximum HP | CODE-WRITE/READ |
| `+0x12` | `uint8` | attack value used by damage calculation | CODE-WRITE/READ |
| `+0x48` | `int16` | world X, same coordinate system as cursor X | CODE-WRITE/READ |
| `+0x4A` | `int16` | world Y, same coordinate system as cursor Y | CODE-WRITE/READ |

Side/owner is not a guessed flag:

```text
slot 0..19  => player/allied
slot 20..39 => enemy
```

A safe public snapshot predicate is:

```text
allocated = read_u16(unit + 0x00) != 0
alive     = allocated &&
            read_u8(unit + 0x10) > 0 &&
            read_i8(unit + 0x05) == 0
dying     = allocated && !alive
```

`FUN_005FBD2F` subtracts damage from `+0x10`, clamps to zero, sets `+0x05` to
`-1`, and decrements the appropriate side count. `FUN_005FF38A` later clears
`+0x00` after the death animation. Reading only `+0x00` would therefore expose
a dead unit for several frames; the combined predicate avoids that.

### Unit under the cursor

`FUN_006029FD` scans all 40 normal live units and returns the closest slot in
this hit box around the world cursor:

```text
unitX > cursorX - 13 && unitX < cursorX + 13
unitY > cursorY - 10 && unitY < cursorY + 14
```

The returned slot is stored at `0x00C6097C`. This is the direct state needed
to say which unit a sighted player's cursor is over. The live unit X/Y can be
subtracted from `0x00CBCCC0/2` to describe direction and distance from the
cursor.

Do not call those distances “squares.” The live 4-unit cursor movement is an
input step, not a placement grid, and moving units are not constrained to a
128-by-252 cell array.

## 3. Setting Menu contents and funds

`FUN_0060378B` constructs the exact visible unit-type list. It chooses one of
four tiers using `(*(uint16*)0x00C72E7C & 3)`:

| Tier | Count | Native unit IDs in displayed order |
| ---: | ---: | --- |
| `0` | 8 | `1, 2, 3, 4, 12, 13, 5, 7` |
| `1` | 9 | `1, 2, 3, 4, 12, 13, 5, 7, 8` |
| `2` | 9 | `1, 2, 3, 4, 12, 13, 5, 6, 7` |
| `3` | 10 | `1, 2, 3, 4, 12, 13, 5, 6, 7, 8` |

| Address | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `0x00C625E0` | `uint32` | `7` while Setting Menu owns the UI | CODE-WRITE/READ |
| `0x00CBCCA0` | `int16` | highlighted relative row | CODE-WRITE/READ |
| `0x00C75254` | `int16` | rotation/base index into the available list | CODE-WRITE/READ |
| `0x00C75264` | `int16` | visible/available unit count | CODE-WRITE/READ |
| `0x00C75278` | `int8[]` | available native unit IDs in menu order | CODE-WRITE/READ |
| `0x00CBC7E0` | `uint32` | player's current gil | CODE-WRITE/READ |
| `0x00CBC7E4` | `uint32` | price cached for the highlighted type | CODE-WRITE/READ |

The stable highlighted unit ID is:

```text
count     = read_i16(0x00C75264)
listIndex = (read_i16(0x00CBCCA0) + read_i16(0x00C75254)) % count
typeId    = read_i8(0x00C75278 + listIndex)
```

The game compares `0x00CBC7E0` against the selected record price and rejects
OK when gil is insufficient. The cached `0x00CBC7E4` is refreshed by the
native stat-panel builder, so a defensive reader can also compute price from
the record below rather than trusting a stale cache outside modal 7.

The code proves the native type ID passed to the game's texture-name renderer.
It does **not** prove that the visual order of strings extracted from
`emes01.tex` is the numeric type-ID order. That mapping should not be guessed;
it should be taken from the atlas's sprite-region table or one marked live
selection capture.

## 4. `data.bin` unit records

The loader stores its heap pointer at `0x00C606F0`. The file is copied without
transforming the records. Consumers repeatedly use exactly this formula:

```text
base       = *(uint32*)0x00C606F0
tableStart = base + *(uint16*)base
record     = tableStart + typeId * 0x20
```

The old scan failed because it stopped at `0x02000000`; the allocation can be
above that. Pointer traversal is the correct implementation.

The requested fields, re-derived from code consumers rather than file
inspection, are:

| Record offset | Type | Meaning | Native consumer |
| ---: | --- | --- | --- |
| `+0x00` | `uint16` | hire price | `FUN_006036BF`, `FUN_00609748` |
| `+0x02` | `uint8` | maximum HP; copied to both live `+0x10` and `+0x11` | `FUN_00607123`, `FUN_00609420` |
| `+0x05` | `uint8` | attack; copied to live `+0x12` and used for damage | `FUN_00607123`, `FUN_00601960`, `FUN_00609420` |
| `+0x07` | `uint8` | third displayed stat/category value | `FUN_00607123`, `FUN_00609420` |

The earlier `+0x16` price, `+0x18` HP, and `+0x1B` attack interpretation was
wrong. Those bytes belonged to a different view of the extracted file; no
native unit-record consumer uses them for these fields.

## 5. What the cursor actually indexes

The live-confirmed cursor is:

| Address | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `0x00CBCCC0` | `int16` | world cursor X | LIVE + CODE |
| `0x00CBCCC2` | `int16` | world cursor Y | LIVE + CODE |

The native movement routine `FUN_005FE91B` updates these as map-pixel-like
world coordinates while moving a camera window. There is no second
placement-cell X/Y and no quantization before hiring: `FUN_00604009` passes
the cursor coordinates to `FUN_00607123`, which writes them directly into the
new live unit's `+0x48/+0x4A` position.

The apparent 128-by-252 space came from dividing the live range by the 4-unit
input step. That is not a board-cell count.

### Placement and terrain/collision

`FUN_005FE63C` publishes the information sighted UI actually uses:

| Address | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `0x00CBCC9C` | `uint16` | `1` when the empty cursor point is currently legal for placement | CODE-WRITE/READ |

The collision representation is:

| Address | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `0x00C625E8` | inline record array | collision/terrain polygon records | CODE-READ |
| `0x00C60AA4` | `uint32` | record count | CODE-WRITE/READ |
| record stride | `0x4C` | bytes per collision polygon | CODE-READ |

`FUN_0060A682` bounds-checks X/Y against record offsets `+0x40..+0x46`, runs
the point-in-polygon test, and returns the polygon index. For the temporary
cursor-placement object that index is local; there is no persistent global
holding a named terrain cell under the cursor.

Therefore a first reader should expose:

- unit under cursor from `0x00C6097C`;
- legal/illegal placement from `0x00CBCC9C`;
- nearby units from the live array.

It should not invent semantic terrain names. A later reader can reproduce the
polygon lookup if a terrain/collision boundary cue is useful, but the sighted
interface itself does not display a named terrain field.

## 6. Funds, enemy progress, victory, and invasion

### Direct counters

| Address | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `0x00CBC7E0` | `uint32` | current gil/funds | CODE-WRITE/READ |
| `0x00C60AD0` | `uint32` | currently active allied-unit count | CODE-WRITE/READ |
| `0x00CBC7A4` | `uint32` | currently active enemy-unit count | CODE-WRITE/READ |
| `0x00CBCC8C` | `uint32` | index of the next unspawned wave entry | CODE-WRITE/READ |
| `0x00CBEDD8` | `int16` | selected enemy-wave table index | CODE-WRITE/READ |
| `0x00C625A8` | `uint32` | accumulated enemy-spawn ticks | CODE-WRITE/READ |
| `0x00C72E48` | `uint16` | next entry's spawn threshold | CODE-WRITE/READ |
| `0x00C752B4` | `int16` | game speed, clamped to 1 through 4 | CODE-WRITE/READ |

### Total enemies remaining

There is no separate total-remaining global. It is CODE-DERIVED from the
active count and the resident wave table:

```text
base      = *(uint32*)0x00C606F0
waveId    = *(int16*)0x00CBEDD8
waveBase  = base + *(uint16*)(base + 10 + waveId * 2)
nextIndex = *(uint32*)0x00CBCC8C

unspawned = count 8-byte entries beginning at waveBase + nextIndex * 8
            until signed byte entry[0] == -1

remaining = *(uint32*)0x00CBC7A4 + unspawned
```

`FUN_00607727` reads entry type at `+0`, spawn threshold at `+4`, allocates
enemy slots 20 through 39, increments the active count, and advances
`0x00CBCC8C`. This is a deterministic reader, not a memory-scan heuristic.

### Outcome and shed invasion

`0x00CBEDC0` is a signed 16-bit outcome transition:

| Value | Meaning | Native consequence | Evidence |
| ---: | --- | --- | --- |
| `0` | battle ongoing | simulation continues | CODE-WRITE/READ |
| `1` | enemy attack halted / victory pending | message ID 2, modal `0x0C` | CODE-WRITE/READ + atlas |
| `2` | enemy invasion / defeat pending | message ID 7, modal `0x0B` | CODE-WRITE/READ + atlas |

There is no continuously decreasing shed HP in this code path. Invasion is a
binary transition. `FUN_005FFF45` writes outcome 2 when an enemy reaches one
of the terminal collision indices `204..207`, or reaches the terminal
destination pair held in its live record (`+0x50 == 0x30`, `+0x52 == -0x60`).
`FUN_005F7E10` then immediately enters the defeat overlay.

The accessible equivalent of “shed under attack” is therefore to announce
the native **Enemy invasion** transition when outcome changes `0 -> 2`, not to
fabricate a fort-health meter.

Outcome 1 is written when active enemies reach zero or when the special enemy
type `0x10` finishes its removal animation. It becomes the native **Halted
enemy attack!** result.

### Native message transitions

The game already has stable message IDs even though their words are textures:

| Address | Type | Meaning | Evidence |
| ---: | --- | --- | --- |
| `0x00C60AC4` | `int16` | pending message ID; `-1` means no pending change | CODE-WRITE/READ |
| `0x00901B70` | `int32` | current rendered message ID | CODE-WRITE/READ |

`FUN_005F7D9B` copies a pending ID to the current ID. `FUN_005F86E4` renders
sprite region `currentId + 0x4B`. Matching those code paths to the recovered
`emes00.tex` atlas gives:

| ID | Spoken text | Native trigger |
| ---: | --- | --- |
| `0` | Encountered enemy. | opposing units first acquire one another |
| `1` | Start combat. | active battle initialization |
| `2` | Halted enemy attack! | victory transition |
| `3` | Arrived at the directed position. | allied command destination reached |
| `7` | Enemy invasion. | defeat/invasion transition |
| `10` | Enemy destroyed. | enemy HP reaches zero |
| `12` | Set units. | placement/setup phase |
| `13` | Start the game? Yes. No. | start confirmation overlay |

These IDs are better speech triggers than polling counts for every frame.
Counts and unit snapshots can be supplied on demand; native message-ID edges
can announce the same events a sighted player sees.

## 7. Recommended implementation boundary

The reader can now follow the mod's state-reader/coordinator pattern without
any draw hooks:

1. A memory reader bookends module 9 and snapshots cursor/menu globals, 40
   live slots, funds/counts, message ID, outcome, and the selected wave table.
2. A pure coordinator compares snapshots and emits only meaningful edges:
   menu ownership/row, unit under cursor, native message changes, outcome, and
   on-demand nearby-unit summaries.
3. English labels come from the exact recovered texture vocabulary, but the
   numeric unit-ID-to-name mapping must be proved rather than assumed from
   visual atlas order.
4. Coherence should fail closed if module changes or `data.bin` pointer/list
   bounds are invalid. Do not expose stale module-9 memory in field mode.

For one final narrow live confirmation before enabling synthesized unit names,
capture:

```text
0x00C625E0 uint32   modal state
0x00C74C50 uint32   interaction mode
0x00C60AE4 int16    Setting Menu open request
0x00C6097C int16    unit under cursor
0x00CBCCA0 int16    Setting Menu row
0x00C75264 int16    Setting Menu count
0x00C75278 bytes    displayed native type IDs
0x00CBC7E0 uint32   gil
0x00CBC7E4 uint32   selected price
0x00CBEDC0 int16    outcome
0x00901B70 int32    current message ID
```

That capture is for executable parity and the remaining name map, not for
finding the core state again.

## Reproduction

After importing and analyzing the matching executable in Ghidra:

```powershell
$env:JAVA_HOME = 'C:\Program Files\Microsoft\jdk-21.0.11.10-hotspot'
$env:XDG_CONFIG_HOME = '<worktree>\.artifacts\ghidra-xdg'

& '<worktree>\.tools\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat' `
  '<project-directory>' '<project-name>' `
  -process 'ff7_en.exe' -noanalysis `
  -scriptPath '<worktree>\analysis\ghidra' `
  -postScript DumpCondorLiveBattleState.java
```

All fixed addresses in this report apply to the executable hash above. A
translated or different executable must be re-anchored rather than assumed to
share them.
