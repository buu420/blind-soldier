# Fort Condor combat result and placement verification

Date: 2026-08-21

Scope: read-only Ghidra analysis of module 9 in the original x86
`ff7_en.exe`. No game or deployed mod files are changed.

Analyzed executable (hashes rechecked on 2026-08-21):

- Path: `C:\Games\Final Fantasy VII\workingdir\ff7_en.exe`
- MD5: `72DF0999B2FAD9AE2AA721CE67D8C3AB`
- SHA-256: `4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`

Repeatable Ghidra evidence script and raw output:

- `analysis/ghidra/DumpCondorCombatResultExact.java`
- `analysis/ghidra/DecodeFf7Tex.ps1`
- `.artifacts/condor-ghidra/CondorCombatResultExact9.application.log`
- `.artifacts/condor-lgp/data.bin`
- `.artifacts/condor-lgp/emes01.tex`
- `.artifacts/condor-lgp/emes01.png`

Evidence policy:

- Every gameplay claim below must be supported by decompiled executable code
  and an address.
- Labels inherited from earlier reports are provisional. In particular,
  `0x00CBEDC0` is not called an outcome until its complete writer/reader chain
  and the module-exit path have been proved.
- Any requested fact that cannot be established is marked **UNESTABLISHED**;
  it is not inferred from guides or the live trace.

## Incremental findings

## Q1. Exact placement exclusion and phase values

### Complete native gate

`FUN_005FE63C` is the ordinary-world-cursor validator. Its machine-code
branches establish this complete gate:

```text
modalState == 0                         0x005FE642-0x005FE649
reportState == 0                        0x005FE654-0x005FE65D
unitUnderCurrentCursor == -1            0x005FE668-0x005FE672
activeAlliedCount < 20                  0x005FE67D-0x005FE684

if phase == 1:
    cursorY < 672                       0x005FE68F-0x005FE6A5
else:
    cursorY < frontierY                0x005FE6AF-0x005FE6BC

terrainAndOverlapTest(typeId=0, cursor) == 0
                                           0x005FE6C4-0x005FE700
```

Success writes the 16-bit preview flag `0x00CBCC9C = 1` at `0x005FE708`.
The setup comparison is signed `cursorY < 0x2A0`, exactly equivalent to
`cursorY <= 671`; the non-setup comparison is signed and strict against
`*(uint32*)0x00C60AE8` (`0x005FE698-0x005FE6BC`).

Therefore `0x00C72DEC` is **genuinely part of the placement gate**, not a
condition invented by the mod. The validator reads it as a signed 16-bit value
at `0x005FE654`, tests it at `0x005FE65B`, and rejects every nonzero value at
`0x005FE65D-0x005FE663`.

The final helper is `FUN_00606F20`. For this placement call, the validator
passes native type ID zero at `0x005FE6EB`. The helper constructs a temporary
unit, calls the overlap scan with half-open slot range `[0, 0x27)` at
`0x00606FE5-0x00606FED`, rejects a nonzero overlap count at
`0x00606FF5-0x00607004`, and otherwise requires the terrain lookup
`FUN_0060A682` to return something other than `-1` at
`0x00607006-0x00607029`.

### Exact overlap rectangle

`FUN_00602F7D` receives the half-open slot range. The placement caller pushes
end value `0x27` at `0x00606FE5`; the loop exits when `slot >= end` at
`0x00602FB2-0x00602FBC`. It therefore scans **slots 0 through 38 inclusive**
and omits slot 39.

For each slot, the only liveness condition is:

```text
read_u16(unit + 0x00) != 0
```

That allocation-word test is at `0x00602FC2-0x00602FCC`. There is no read of
HP `+0x10` or removal state `+0x05` anywhere in the scan
(`0x00602F7D-0x006030E8`). Consequently an allocated dying/dead-animation
slot **does block** until its allocation word is cleared.

The exact exclusion calculation is:

```text
halfWidth = (read_u8(unit + 0x22) + 28) >> 1

blocked iff
    unitX - halfWidth <= candidateX <= unitX + halfWidth
and unitY - read_u8(unit + 0x23) <= candidateY <= unitY + 22
```

All four edges are inclusive. The width load, `+28`, and arithmetic divide by
two are at `0x00602FD8-0x00602FE5`; the two inclusive X cases are
`0x00602FE9-0x00603041`; the `+22` lower-unit case is
`0x00603057-0x00603086`; and the `unitY-heightAbove` case is
`0x0060308C-0x006030B0`. The live-unit stride is `0x78` at
`0x006030D6-0x006030DF`.

This confirms the constants `28` and `22` and the rectangle already stated in
the question. It does **not** support skipping HP-zero or removing slots in
this overlap scan.

### Separate unit-under-cursor test

The validator does not recalculate a hit box itself. It consumes the current
global `*(int16*)0x00C6097C` at `0x005FE668`. That global is produced by
`FUN_006029FD`, whose scan differs from the overlap scan in two important ways:

- It scans all 40 slots, `0..39` (`0x00602A48-0x00602A4F`).
- After the allocation check at `0x00602A55-0x00602A5F`, it skips every slot
  whose removal byte `unit+0x05` is nonzero (`0x00602A65-0x00602A6F`). It
  does not read HP `+0x10`.

For a non-removing allocated slot, its exact strict hit box is:

```text
unitX > cursorX - 13 && unitX < cursorX + 13
unitY > cursorY - 10 && unitY < cursorY + 14
```

The X comparisons are at `0x00602A75-0x00602AA0`; the Y comparisons are at
`0x00602AA6-0x00602AD1`.

Thus the stated reimplementation is not byte-for-byte exact if its **hit-box**
pass includes dying/removing units. The native hit-box pass skips removal
state nonzero, while the native **overlap** pass still includes those slots.
This is the only established divergence in the predicate described in the
question. Whether it accounts for every one of the 35 logged disagreements is
not established by static code alone; the native flag and all inputs would
need a same-game-thread snapshot to attribute each sample.

### Phase values

Ghidra finds 27 direct references to `0x00C625D4` and exactly four write sites:

| Value | Write instruction(s) | Code-proven behavior |
| ---: | --- | --- |
| `0` | `0x005F78F5` | Alternate non-setup initialization; `FUN_00607570` loads its initial allied list from the resident `data.bin` section at `0x00607576-0x00607606`. Enemy spawning is enabled because only phase 1 is excluded at `0x0060772D-0x00607736`. |
| `1` | `0x005F7765`, `0x005F7901` | Setup. It uses `Y <= 671` in the validator (`0x005FE68F-0x005FE6A5`) and suppresses enemy spawning (`0x0060772D-0x00607736`). |
| `2` | `0x005F7986` | Started battle using the staged player placements; `FUN_00607570` takes the phase-2 branch at `0x00607608` and reconstructs those units at `0x00607618-0x00607673`. It uses the live frontier and permits enemy spawning. |

The normal Start confirmation writes transition state `3` to `0x00CBC80C` at
`0x005FCD93`; initialization tests that state at `0x005F797D` and writes
phase `2` at `0x005F7986`. This proves the live log's `phase==2` is the normal
active-combat value. The dispatcher can toggle phase 1 to 0 and 0/2 to 1 at
`0x005F78C6-0x005F7901`. No direct writer stores any value other than
`0`, `1`, or `2`. A more specific user-facing name for the alternate phase-0
mode is **UNESTABLISHED**; the executable behavior above is established.

## Q2. Enemy unit names

### Executable mapping: native type ID to `emes01` cell

The stat-panel name is not selected from `eunit01.tex`. The executable loads
`emes01.tim` into handle global `0x00C60728` in `FUN_005F342C` (the handle
write is `0x005F368F`) and loads `eunit01.tim` separately into
`0x00C60718` (write `0x005F370F`). `FUN_005F2CE4` then binds the `eunit01`
handle as texture-table slot 2 at `0x005F2D0C-0x005F2D12`, but binds the
`emes01` handle as texture-table slot 6 at `0x005F2D4F-0x005F2D54`.
The loader strings use the `.tim` spelling; the corresponding shipped archive
member inspected here is `emes01.tex`.

`FUN_005F933F` creates exactly 24 name regions, for native type IDs `0..23`.
The loop is `0x005F9C29-0x005F9C82`; its `0x18` bound is at
`0x005F9C3B-0x005F9C3F`. For each `typeId`, its arguments to
`FUN_00607B91` at `0x005F9C7A` are code-equivalent to:

```text
regionIndex = 0x5F + typeId
width       = 64
height      = 16
sourceX     = (typeId / 6) * 64
sourceY     = 32 + (typeId % 6) * 16
textureSlot = 6
```

The modulo/division and Y calculation are at `0x005F9C4E-0x005F9C5F`; the
division and X calculation are at `0x005F9C60-0x005F9C6E`; and the region
offset is at `0x005F9C73-0x005F9C79`. The pushed texture-slot constant 6 is
at `0x005F9C46`. `FUN_00607BCD` stores that texture selector in each region's
`+0x18` field at `0x00607C52-0x00607C58`, and `FUN_00607CC5` uses the field
to select the bound texture (notably its slot-6 comparison is at
`0x00607D89`).

The display builder `FUN_006092C2` preserves its first argument as the native
type ID in `0x00CBF110` at `0x00609355-0x00609363`. The drawing routine
`FUN_005FA132` adds `0x5F` to that value at
`0x005FA148-0x005FA151` and draws the resulting region at
`0x005FA173-0x005FA184`. Thus this is a direct executable mapping, not a
guide-derived name table.

### Shipped labels for enemy records 16 through 19

Reading the exact cells selected above from the shipped `emes01.tex` gives:

| `data.bin` record / native type | Record HP / attack | `emes01` source cell `(x,y,w,h)` | Sprite region | Exact drawn label |
| ---: | ---: | ---: | ---: | --- |
| `16` | `250 / 60` | `(128,96,64,16)` | `0x6F` | `Commander` |
| `17` | `190 / 25` | `(128,112,64,16)` | `0x70` | `Wyvern` |
| `18` | `230 / 30` | `(192,32,64,16)` | `0x71` | `Beast` |
| `19` | `180 / 20` | `(192,48,64,16)` | `0x72` | `Barbarian` |

The record bytes were rechecked in the extracted shipped `data.bin` at
`0x226..0x2A5` (`0x26 + typeId * 0x20`). The executable's 24-region loop and
the `typeId + 0x5F` draw path above establish which of those texture labels
each record uses. In particular, the shipped executable/assets map **Beast to
record 18 (HP 230)** and **Wyvern to record 17 (HP 190)**; the incompatible
212/140 guide values are not used here.

The record selection is also executable-proven: `FUN_00607123` obtains the
record-section offset from the first word of `data.bin`, then adds
`typeId * 0x20` at `0x00607129-0x00607158`. It copies record byte `+2` into
both live HP bytes `+0x10/+0x11` at `0x006072D9-0x006072EE`, and record byte
`+5` into live attack byte `+0x12` at `0x006072F1-0x006072FA`.

There is also a direct live-unit-to-panel path: `FUN_005F88F3` multiplies
`unitUnderCurrentCursor` by the `0x78` slot stride at
`0x005F8DF8-0x005F8DFF`, reads that live slot's type word at `unit+0x06`
(`0x005F8E02`), and passes the type and slot to `FUN_006092C2` at
`0x005F8E09-0x005F8E0A`. This rules out a second enemy-only remapping between
the live type and the name region.

## Q3. Battle progress and the two different front lines

### Enemy count, spawned count, and the script total

The directly supported runtime fields are:

| Meaning | Global | Code evidence |
| --- | --- | --- |
| Active, non-removing enemies currently on the field | `uint32 0x00CBC7A4` | Spawn is blocked at 20 by `0x00607788-0x0060778F` and increments the count only after successful unit construction at `0x00607846-0x0060784F`. The removal path decrements it at `0x006051CF-0x006051D8`. |
| Successfully spawned entries / index of the next 8-byte spawn entry | `uint32 0x00CBCC8C` | Initialized to zero at `0x005F7A89`, used in the address calculation at `0x00607767-0x0060776F`, and incremented only after a successful spawn at `0x00607866-0x0060786E`. |
| Selected encounter script ID (`0..6`) | `int16 0x00CBEDD8` | Loaded from persistent byte `0x00DC0985` at `0x005F7776-0x005F7791`; values `>=7` are replaced by zero at `0x005F7798-0x005F77A4`. It selects one of seven 16-bit offsets at `dataBase+10+2*id` in `0x0060773A-0x00607764`. |
| Elapsed spawn ticks | `uint32 0x00C625A8` | Incremented by `0x00C752B4` at `0x0060779C-0x006077A3` and divided by 60 at `0x006077A8-0x006077C2`. |
| Current entry's spawn-time threshold | `uint16 0x00C72E48` | Copied from spawn-entry bytes `+4..+5` at `0x006077D0-0x006077D7` and compared with elapsed ticks/60 at `0x006077DE-0x006077EA`. |

`0x00CBC7A4` is also what a sighted player gets in the HUD's enemy counter.
`FUN_006091FC` reads it at `0x00609268`, converts it to tens and ones, and
writes the two enemy digit regions at `0x0060929F` and `0x006092AE`.
`FUN_005F88F3` invokes the allied/enemy count renderer at
`0x005F8A27-0x005F8A62`. It is an **active on-field count**, not the total
number still scheduled.

There is no independently established "enemy remaining" global. The spawn
routine instead obtains the current 8-byte entry and tests its first byte for
the signed `-1` terminator at `0x00607767-0x00607782`. Consequently the exact
remaining force, including active enemies and not-yet-spawned entries, is
derivable as:

```text
totalScheduled = number of 8-byte records before first type byte 0xFF
killedOrRemoved = 0x00CBCC8C - 0x00CBC7A4
remaining       = totalScheduled - 0x00CBCC8C + 0x00CBC7A4
```

The subtraction `spawned - active` is independently used when module 9
exports its statistics at `0x005F7842-0x005F7853`. Reading the shipped
`data.bin` tables with the executable's offset/stride/terminator rules gives
these code-selected totals:

| `0x00CBEDD8` | Table start in `data.bin` | Scheduled entries | Terminator offset |
| ---: | ---: | ---: | ---: |
| `0` | `0x0E06` | `11` | `0x0E5E` |
| `1` | `0x0E60` | `25` | `0x0F28` |
| `2` | `0x0F2A` | `30` | `0x101A` |
| `3` | `0x101C` | `35` | `0x1134` |
| `4` | `0x1136` | `40` | `0x1276` |
| `5` | `0x1278` | `47` | `0x13F0` |
| `6` | `0x13F2` | `52` | `0x1592` |

This is a scheduled-force count, not a promise that every listed unit must be
destroyed: the separate type-16 Commander removal writer can latch victory at
`0x005FF4C0-0x005FF4D9`.

### Wave/group request: no native counter established

A distinct current-wave/group index and total-wave global are
**UNESTABLISHED**. More specifically, the decompiled spawn path does not
advance waves: it advances one entry index (`0x00CBCC8C`) through a flat
8-byte list, and entries can share the same `+4` time threshold
(`0x00607767-0x006077EA`). Calling each set of equal timestamps a "wave" would
be a mod-defined grouping, not an executable field.

Spawn-script-entry byte `+6` (not the unit-stat-record byte `+0x06` mentioned
in Q2) is not a group ID. `FUN_0060747C` copies it at
`0x006074C8-0x006074D3`; `FUN_00607123` stores it in live-unit byte `+0x20`
at `0x00607349-0x00607352`; and `FUN_006008F0` dispatches on values `1..4` at
`0x006008FB-0x00600917` to choose among different target-selection behaviors.
This removes that field as a candidate for wave reporting.

### Native enemy-advance value

The sighted progress storage is `int32 0x00CBCCAC`, computed by
`FUN_005FF38A`; the renderer consumes its low signed 16 bits. The function
starts a minimum enemy Y at `1024`, scans all 40
live slots (`0x005FF390-0x005FF3BB`), requires allocation nonzero and removal
byte `unit+0x05 == 0` (`0x005FF3C1-0x005FF402`), and treats type IDs `>=16` as
enemies (`0x005FF4FE-0x005FF50A`). For those enemies it retains the minimum
signed `unitY` from `unit+0x4A` at `0x005FF50C-0x005FF51F`.

The exact conversion is:

```text
leadingEnemyY = minimum unitY among allocated, non-removing type >= 16 units

if leadingEnemyY <= 448:
    enemyAdvance = 96
else:
    enemyAdvance = 96 - ((leadingEnemyY - 448) / 6)

*(int32*)0x00CBCCAC = enemyAdvance
```

The clamp and formula are at `0x005FF61D-0x005FF649`. The raw minimum Y is a
local, not a separately established global. The result is visibly rendered:
`FUN_005F88F3` reads `0x00CBCCAC` at `0x005F8AD3` and uses it to size and draw
the segmented gauge beginning at `0x005F8ADD` (through the segment draws at
`0x005F8C18`). Numerically, the value increases as the leading enemy's Y
decreases toward 448. The threshold-crossing code at
`0x005FF64F-0x005FF66A` is also based on this value, not on the placement
frontier.

Enemy spawn coordinates confirm the same coordinate source without relying on
the live trace: spawn-entry byte `+2` is multiplied by 16 at
`0x006074E7-0x006074F5`, then `FUN_00607123` stores
`unitY = 1024 - thatValue` at `0x00607172-0x00607183`.

### What `0x00C60AE8` actually tracks

`0x00C60AE8` tracks the **allied advance/placement frontier**, not enemy
advance. In the same 40-slot scan, every non-removing type `<16` contributes:

```text
candidate = signed unitY at +0x4A + (read_u8(unit + 0x16) * 16)
frontier  = max(480, every allied candidate)
frontier  = min(frontier, 928)
```

The type split and calculation are at `0x005FF4FE-0x005FF549`; the initial
480 is set at `0x005FF397`; and the global write plus 928 cap are at
`0x005FF66D-0x005FF68C`. No enemy coordinate enters that maximum. Therefore
the observed `480 -> 698 -> 722` movement records the forward edge of the
player's live allied force (including its type-specific `+0x16` extent), not
the invaders' advance.

## Q4. Definitive battle result and leaving module 9

### `0x00CBEDC0` is the live result latch

The earlier suspicion is resolved: `int16 0x00CBEDC0` really is the
module-9 **pending battle-result latch**, not the banner ID and not an
unrelated state. `FUN_005F7979` initializes it to zero at `0x005F79E8`.
Across all 16 direct references in this executable, its only nonzero writes
are `1` and `2`, and `FUN_005F7E10` dispatches those exact values to the two
result overlays at `0x005F7F4E-0x005F7F6B`:

| Latch value | Exact writers / condition | Consumer |
| ---: | --- | --- |
| `0` | No result yet; initialized at `0x005F79E8` | Battle continues. |
| `1` | Active enemy count `0x00CBC7A4 == 0` while the latch is zero (`0x005F7EF4-0x005F7F08`); or a type-16 Commander finishes its removal sequence while the latch is zero (`0x005FF4C0-0x005FF4D9`) | Calls victory initializer `FUN_005F7D33` at `0x005F7F4E-0x005F7F5A`. |
| `2` | Allied count is zero, latch is zero, and `0x00CBC7E0 < 400` (`0x005F7F11-0x005F7F31`); or an enemy's 16-bit live field `+0x0A` is in inclusive range `204..207` (`0x0060013A-0x00600166`); or its live `+0x50,+0x52` pair equals `(48,-96)` (`0x0060016F-0x00600192`) | Calls invasion initializer `FUN_005F7D5B` at `0x005F7F5F-0x005F7F6B`. |

Both late enemy-position writers first require the latch to still be zero
(`0x0060015B-0x00600166` and `0x00600187-0x00600192`). The main tick likewise
waits for both modal state `0x00C625E0 == 0` and report state
`0x00C72DEC == 0` before it starts either result overlay
(`0x005F7F3A-0x005F7F6B`). Thus the latch is definitive even if another open
panel briefly delays presentation of the result.

### Result latch to the visible banner

The victory initializer `FUN_005F7D33` queues message ID `2` at
`0x005F7D3B-0x005F7D42`, passes argument `10` to `FUN_005FB3FE` at
`0x005F7D45-0x005F7D4C`, and sets modal state `0x00C625E0 = 0x0C` at
`0x005F7D4F`.

The invasion initializer `FUN_005F7D5B` queues **message ID `7`** at
`0x005F7D63-0x005F7D6A`, passes `0x0B` to `FUN_005FB3FE` for encounter script
6 or `0x0C` otherwise at `0x005F7D6D-0x005F7D8C`, and sets modal state
`0x00C625E0 = 0x0B` at `0x005F7D8F`.

The queue function `FUN_006027B3` writes its argument to pending-message
global `int16 0x00C60AC4` at `0x006027B6-0x006027BA`. At the start of a
subsequent battle tick, `FUN_005F7D9B` checks that pending value and copies it
to the displayed 32-bit message ID `0x00901B70` at
`0x005F7DC8-0x005F7DE1`. Therefore the logged `0x00901B70 == 7` is exact code
evidence that the **invasion banner** had been published. `0x00901B70` is a
message ID, not the result field; the corresponding result latch is
`0x00CBEDC0 == 2`.

### Timed overlay and the module-exit decision

Both result initializers first call `FUN_005F7CD5`, which sets
`0x00C72DF0 = 0` and `0x00C72DFC = -32` at
`0x005F7D1A-0x005F7D23`. `FUN_005FC96F` dispatches modal `0x0B` to the
invasion handler at `0x005FC9DA` and modal `0x0C` to the victory handler at
`0x005FC9E1` (the switch is based on `0x00C625E0` at
`0x005FC973-0x005FC98D`).

The two exact completion paths are:

- Victory: `FUN_005FCA8E` increments `0x00C72DF0` while it is below 360 and,
  once it is no longer below 360, writes control/return state
  `0x00CBC80C = 5` at `0x005FCA91-0x005FCADA`.
- Invasion: `FUN_005FCAE6` increments `0x00C72DF0` and exits automatically
  after it exceeds 600 (`0x005FCAF6-0x005FCB1D`). It also increments the
  secondary counter from its initial `-32`; once that counter reaches 64,
  input mask `0x60` in `0x00C74C54` can end the overlay early
  (`0x005FCB21-0x005FCB5B`). Either condition writes
  `0x00CBC80C = 4` at `0x005FCB5F-0x005FCB67`.

The early-input route cannot occur until 96 invasion-handler updates have
advanced the secondary counter from `-32` to `64`. The executable establishes
update counts, not wall-clock seconds. The live roughly two-second observation
is consistent with this early-input route, but an unconditional two-second
timeout is **not** present in the code. Without that masked input, the
automatic threshold is 601 invasion-handler updates.

The actual leave decision is in the module's main callback `FUN_005F4A47`:
after input/modal processing it compares `0x00CBC80C` with 4 at
`0x005F4A95-0x005F4A9C`. Values 4 and 5 both take the transition branch and
call `FUN_005F4971` at `0x005F4A9E-0x005F4AA7`; smaller values continue the
module-9 render/simulation callback at `0x005F4AAC-0x005F4AB5`.
`FUN_005F4971` then writes `9` to `0x00CC0D84` and `1` to `0x00CBF9DC` at
`0x005F4977-0x005F4980`, prepares five callback addresses at
`0x005F4989-0x005F49A5`, and calls `FUN_00666CF2` at
`0x005F49AC-0x005F49B9`.

The module-9 leave callback is `FUN_005F49C0`; generic engine function
`FUN_004090E6` contains the direct reference that installs address
`0x005F49C0` at `0x00409B12`. Its first operation is to export the Condor
result via `FUN_005F7818` at `0x005F49C3`. It then makes four cleanup calls at
`0x005F49C8-0x005F49E0` and performs its final three global writes at
`0x005F49E3-0x005F49F7`.

### Persistent result after module 9

The exported one-byte result is `0x00DC0988`. `FUN_005F7818` writes it as the
Boolean expression `0x00CBC80C == 4`: invasion state 4 writes `1` at
`0x005F7859-0x005F7865`, while victory state 5 writes `0` at
`0x005F786E-0x005F7871`. In short:

```text
live/pending result:  int16 0x00CBEDC0   0=none, 1=victory, 2=invasion/loss
visible banner:       int32 0x00901B70   message id (7 for invasion)
post-module result:   uint8 0x00DC0988   0=victory, 1=invasion/loss
```
