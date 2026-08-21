# Fort Condor module 9 state findings

Date: 2026-08-21

Scope: static, read-only analysis of the original x86 `ff7_en.exe` used by the
2013/FFNx runtime. No deployed game or mod files were changed.

Analyzed executable:

- Path: `C:\Games\Final Fantasy VII\workingdir\ff7_en.exe`
- Size: 5,997,056 bytes
- MD5: `72DF0999B2FAD9AE2AA721CE67D8C3AB`
- SHA-256: `4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`

Repeatable Ghidra scripts:

- `TraceCondorModuleState.java` starts at the FFNx anchors and prints the
  caller/descendant graph and high-address data references.
- `DumpCondorInputMenuAndData.java` prints the focused input, cursor, menu,
  and resident-data references and decompiles the functions supporting the
  findings below.

## Result summary

The probe's old input address is not the input used by module 9. Fort Condor
refreshes the normal game input, remaps it into a dedicated Condor mask, and
then consumes that dedicated mask.

The three highest-value addresses are:

| Purpose | Address | Type |
| --- | ---: | --- |
| Condor current input mask | `0x00C72E80` | `uint32`, low 16 bits meaningful |
| Condor pressed-edge mask | `0x00C74C54` | `uint32`, low 16 bits meaningful |
| Resident `data.bin` buffer pointer | `0x00C606F0` | 32-bit pointer |

For an arrows-only confirmation capture, sample at least `0x00C72E80` and
`0x00C74C54`. Sampling the source masks `0x009A85D4` and `0x009A85E0` at the
same time will prove both sides of the remapping.

## 1. Module 9 tick and input path

FFNx commit `665b845f030d08d3e12e4dd1bf08ac2dad3e685f` supplies the independent
anchor chain:

- the engine's Condor main loop is resolved at main-loop offset `+0xA13`;
- its call at `+0x5B` is `FUN_005F4971`;
- its call at `+0x69` is `FUN_005F5042`;
- the enter/resource chain is `FUN_005F7756 -> FUN_005F4273 -> FUN_005F342C`;
- FFNx patches six unit-texture loads inside `FUN_005F342C`.

Ghidra finds exactly one function that calls both the `+0x5B` and `+0x69`
targets:

| Function | Address | Role |
| --- | ---: | --- |
| `FUN_005F4A47` | `0x005F4A47` | registered Condor/module 9 main loop |
| `FUN_005FD958` | `0x005FD958` | per-frame input and UI state coordinator |
| `FUN_005F5042` | `0x005F5042` | per-frame simulation/render worker |

`FUN_005F4A47` calls `FUN_005FD958` before it calls `FUN_005F5042`. The input
word that the Condor UI logic actually tests is `0x00C72E80`, constructed in
`FUN_005FD958` after `FUN_005FADD4` samples the normal game input.

The complete path is:

```text
FUN_005F4A47  module 9 main loop
  -> FUN_005FD958  input/UI update
       -> FUN_005FADD4  sample buttons
            -> FUN_00676578  obtain input context at 0x00DB2BB8
            -> FUN_0041A21E  refresh normal game input
            -> FUN_005FADB6(mask) -> FUN_0041AB67(mask)
                 reads 0x009A85D4 (current/held logical input)
            -> FUN_005FAFBB(mask) -> FUN_0041AB74(mask)
                 reads 0x009A85E0 (pressed logical input)
       -> writes 0x00C72E80  Condor current mask
       -> writes 0x00C74C54  Condor rising edges
       -> updates cursor and menu state
  -> FUN_005F5042  simulation/render worker
```

Relevant masks:

| Address | Meaning |
| ---: | --- |
| `0x009A85D4` | shared game logical input, current/held |
| `0x009A85E0` | shared game logical input, pressed edges |
| `0x00C72E80` | Condor current/held input after its remap |
| `0x00C74C4C` | previous Condor current mask |
| `0x00C74C54` | Condor rising-edge mask: `(current ^ previous) & current` |
| `0x00C74C48` | Condor repeat events used by cursor/menu navigation |

The Condor directional bits are proven by `FUN_005FE91B`:

| Direction | Bit |
| --- | ---: |
| Up | `0x1000` |
| Right | `0x2000` |
| Down | `0x4000` |
| Left | `0x8000` |

`0x20` is OK/confirm and `0x40` is Cancel in the modal handlers. Other action
bits should be named only after their individual state transitions are mapped;
the static analysis does not need guessed PlayStation button names.

The module-9 probe should stop treating the field-oriented address
`0x00CC0DF0` as its input word. It is not on the Condor input path.

## 2. Cursor and menu state

There is no single packed cursor/menu struct. The state is split across fixed
globals.

### World cursor and interaction mode

| Address | Type | Meaning |
| ---: | --- | --- |
| `0x00CBCCC0` | `int16` | normal world cursor X |
| `0x00CBCCC2` | `int16` | normal world cursor Y |
| `0x00C75268` | `int16` | destination-command cursor X |
| `0x00C7526A` | `int16` | destination-command cursor Y |
| `0x00C6097C` | `int16` | selected unit index; `-1` means none |
| `0x00C74C50` | `uint32` | interaction mode |

Verified `0x00C74C50` values:

- `1`: ordinary cursor mode. This is the initialized value.
- `2`: the Ally Unit command menu is open. `FUN_005FD832` enters it.
- `3`: the player is placing a destination for a unit command.
  `FUN_00603230` enters it and `FUN_005FE8CF` switches movement to the
  destination cursor pair.

### Ally Unit command menu

| Address | Type | Meaning |
| ---: | --- | --- |
| `0x00CBC930` | `int16` | current command row |
| `0x00C752D4` | `uint8` | available command-row count |
| `0x00C74CA8` | byte | command identifier for row 0 |
| `0x00C74CB0` | byte | command identifier for row 1 |
| `0x00C74CB8` | byte | command identifier for row 2 |

The row initializes to zero and wraps over `0x00C752D4`. Command identifiers,
not English strings, drive `FUN_00603230`; labels therefore need to be mapped
to those identifiers rather than scraped from a draw call.

### Setting Menu

`0x00C625E0` is a modal/overlay state. Zero means no modal overlay. Value `7`
is the Setting Menu used to hire and set a unit. This is not a texture-based
guess:

- `FUN_0060378B` builds the available unit-type list, enters modal state 7,
  reads the selected unit record from `data.bin`, and builds its stat panel;
- `FUN_00604208` changes the selection, refreshes the unit description and
  stats, checks gil on OK, and closes on Cancel.

The Setting Menu selection is:

| Address | Type | Meaning |
| ---: | --- | --- |
| `0x00C625E0` | `uint32` | modal state; `7` means Setting Menu |
| `0x00CBCCA0` | `int16` | relative visible/current row |
| `0x00C75254` | `int16` | rotation/base index into the unit list |
| `0x00C75264` | `int16` | number of available unit types |
| `0x00C75278` | byte array | unit-type identifiers in menu order |
| `0x00C625D0` | `int16`/12-bit phase | animated scroll position |
| `0x00C74CC8` | `int16` | per-row scroll stride |

The selected unit type can be read without following the animation:

```text
listIndex = (0x00CBCCA0 + 0x00C75254) % 0x00C75264
unitType  = signed_byte(0x00C75278 + listIndex)
```

In this executable `0x00C75254` is initialized to zero and has no other static
writer, but retaining it in the formula mirrors the native reader exactly.

This gives a stable state snapshot for a speech coordinator even while the
texture animation is moving.

## 3. Loaded Condor data

`FUN_005F33B4("data\\data.bin")` stores the result of `FUN_005F2F46` at
`0x00C606F0`. `FUN_005F2F46` allocates `fileSize + 2`, reads the archive entry
directly into that allocation, and appends a zero. It does not transform the
unit table.

Consumers such as `FUN_006036BF` and `FUN_00609420` calculate a unit record as:

```text
base       = *(uint32*)0x00C606F0
tableStart = base + *(uint16*)base
unit       = tableStart + unitType * 0x20
```

This agrees with the extracted file's first offset of `0x30` and its 32-byte
unit records.

The old signature scan missed the table because the file is allocated on the
heap, not because it is transformed. A scan capped at `0x02000000` is not
guaranteed to include that allocation. Dereferencing `0x00C606F0` is the
correct route.

Other resident Condor resources:

| Address | Resource |
| ---: | --- |
| `0x00C606F8` | `camera.bin` pointer |
| `0x00C60700` | loaded help texture, not `data.bin` |
| `0x00C60704` | `id.bin` pointer |
| `0x00C60708` | dynamic texture handle 0 |
| `0x00C6070C` | dynamic texture handle 1 |

## Recommended confirmation capture

The arrows-only pass can now be narrowly marked:

1. enter module 9 and remain idle for five seconds;
2. tap Up, Right, Down, and Left once each, with a one-second pause;
3. hold one direction long enough to trigger repeat;
4. press OK once to enter the Setting Menu, move one row, then Cancel;
5. select an existing allied unit, open its Ally Unit menu, move one row, and
   Cancel;
6. quit the minigame.

At every probe pass, before the 120 ms report throttle, sample:

```text
0x009A85D4 uint32  shared current input
0x009A85E0 uint32  shared pressed input
0x00C72E80 uint32  Condor current input
0x00C74C54 uint32  Condor pressed edges
0x00C74C48 uint32  Condor repeat events
0x00CBCCC0 int16   cursor X
0x00CBCCC2 int16   cursor Y
0x00C74C50 uint32  interaction mode
0x00C625E0 uint32  modal state
0x00C6097C int16   selected unit
0x00CBC930 int16   Ally Unit command row
0x00CBCCA0 int16   Setting Menu relative row
0x00C75254 int16   Setting Menu rotation/base index
0x00C75264 int16   Setting Menu unit count
```

For the first four marked arrow taps, the expected Condor bits are `0x1000`,
`0x2000`, `0x4000`, and `0x8000` respectively. That pass should confirm live
timing and executable parity; it no longer needs to search the 32 MB region
for candidate state.

## Reproduction

After importing and analyzing the matching executable in a Ghidra project:

```powershell
$env:JAVA_HOME = 'C:\Program Files\Microsoft\jdk-21.0.11.10-hotspot'
$env:XDG_CONFIG_HOME = '<worktree>\.artifacts\ghidra-xdg'

& '<worktree>\.tools\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat' `
  '<project-directory>' '<project-name>' `
  -process 'ff7_en.exe' -noanalysis `
  -scriptPath '<worktree>\analysis\ghidra' `
  -postScript TraceCondorModuleState.java

& '<worktree>\.tools\ghidra_12.1.2_PUBLIC\support\analyzeHeadless.bat' `
  '<project-directory>' '<project-name>' `
  -process 'ff7_en.exe' -noanalysis `
  -scriptPath '<worktree>\analysis\ghidra' `
  -postScript DumpCondorInputMenuAndData.java
```

These are static addresses for the executable hash above. A translated or
different executable must be re-anchored rather than assumed to share them.
