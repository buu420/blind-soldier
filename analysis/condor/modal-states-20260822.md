# Fort Condor modal and overlay states — 2026-08-22

## Scope and evidence standard

This investigation enumerates every value written to module 9's 32-bit modal-state word at
`0x00C625E0`, identifies the native update and draw paths for each state, and reconstructs every
player-facing choice that Blind Soldier must speak. It also distinguishes modal state from
interaction mode (`0x00C74C50`) and battle phase (`0x00C625D4`).

Findings are appended as they are proved. “Proved” means the native writer, consumer, selection
state and draw path were traced in Ghidra against the existing read-only x86 analysis project.
Texture wording is attributed separately to the decoded `condor.lgp` evidence already checked
into this repository or to an authoritative game-data extraction. Runtime-log observations are
identified as observations rather than static proof.

## Investigation status

Complete. The native state map is reproduced by the checked-in Ghidra passes. A shared reader
and speech implementation for every proved choice state is present in the worktree and remains
uncommitted for review. No game-installation file was changed.

## Finding 1 — the modal is a 32-bit dispatcher, and its direct-write set is larger than the reader models

**Proved from code.** The repeatable pass in
`analysis/ghidra/DumpCondorModalStates.java` finds 75 direct references to the 32-bit word at
`0x00C625E0`. The executable directly writes these nonzero values:

```text
2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 15, 16, 17
```

Zero closes an overlay or returns to ordinary play. There are no direct constant writes of 1 or
13. An indirect writer has not yet been ruled out, so absence from this set is not yet a claim
that those values can never occur.

The exact write sites are:

| Modal | Native write site(s) |
| ---: | --- |
| `2` | `FUN_005FD958` at `0x005FDB7E` and `0x005FDEAE` |
| `3` | `FUN_005FD958` at `0x005FDC99` |
| `4` | `FUN_005F7979` at `0x005F7BCF` |
| `5` | `FUN_005FCE95` at `0x005FCF58` |
| `6` | `FUN_005FD245` at `0x005FD2CC` |
| `7` | `FUN_0060378B` at `0x00603B39` and `0x00603F56` |
| `8` | `FUN_00604009` at `0x00604032` and `0x00604166` |
| `9` | `FUN_005FD958` at `0x005FE44F` |
| `10` | `FUN_005FD958` at `0x005FDDFE` |
| `11` | `FUN_005F7D5B` at `0x005F7D8F` |
| `12` | `FUN_005F7D33` at `0x005F7D4F` |
| `14` | `FUN_005FD8ED` at `0x005FD93A` |
| `15` | `FUN_005FD3A0` at `0x005FD72F` |
| `16` | `FUN_00603230` at `0x006033A0` |
| `17` | `FUN_006027C2` at `0x0060285F` |

The released shared snapshot named only modal 7. The implementation described below now models
every overlay that exposes a proved choice, without inventing choices for transition-only states.

## Finding 2 — interaction mode is an independent axis, and mode 2 is an unspoken command menu

**Proved from code.** `FUN_005FD958` dispatches modal state first: when
`0x00C625E0 != 0`, it calls `FUN_005FC96F` and returns. When the modal is zero, the same input
routine switches independently on interaction mode `0x00C74C50`. The three observed values have
these meanings:

| Interaction mode | Native behavior |
| ---: | --- |
| `1` | Battlefield cursor. OK opens the Setting Menu on empty ground or selects the unit under the cursor. |
| `2` | **Ally Unit command list.** Up/down changes its highlighted command; OK executes it; Cancel returns to mode 1. |
| `3` | Destination cursor. Directional input chooses a destination; OK confirms it; Cancel returns to the Ally Unit list. |

This proves that `mode=2, modal=0` in the live log is not an idle state. It is a player-facing,
selectable menu that the current `CondorBattleSpeechTracker` never announces.

`FUN_005FD832` opens the menu only outside setup phase (`0x00C625D4 != 1`) and only when a
selected allied slot is valid. It writes mode 2 and initializes the highlighted row at
`0x00CBC930` to zero. `FUN_005FE800` then derives the available commands from the selected
unit's command category at unit offset `+0x13` and writes:

```text
row count:       byte  0x00C752D4
highlighted row: int16 0x00CBC930
row 0 command:   byte  0x00C74CA8
row 1 command:   byte  0x00C74CB0
row 2 command:   byte  0x00C74CB8
```

The native category-to-command construction is:

| Unit command category | Count | Command IDs |
| ---: | ---: | --- |
| `0` | 1 | `2` |
| `1` | 1 | `3` |
| `2` | 1 | `3` |
| `3` or `4` | 2 | `5`, `2` |
| `5` | 1 | `3` |
| `6` | 2 | `3`, `0` |
| `7` or other | 0 | none |

`FUN_005F7F9D` draws each row from the game's `eunit01` command texture at source Y
`commandId * 16` and source X `0xC0`, and draws the selection arrow from `0x00CBC930`.
`FUN_005FD958` wraps the row against `0x00C752D4`; OK dispatches the selected ID through
`FUN_00603230`. IDs used by this constructor map to the texture's actual selectable-command
column as follows: `0 = Bomb`, `2 = Remove`, `3 = Action`, and `5 = Direction`. This was
verified independently by decoding the shipped `eunit01.tex`. The yellow words `ATTACK`,
`CHASE`, `REPAIR`, `MOVE`, and `WAIT` at the left of that texture are unit-status labels, not
the menu cells sampled at X `0xC0`. In native behavior, Action enters interaction mode 3 and
therefore blocks on a destination selection; Direction enters modal 16. A blind player cannot
reliably play the combat phase if this list is silent.

## Finding 3 — phase has only setup and combat meanings; it does not identify the open menu

**Proved from code.** The direct writers of the 32-bit phase word at `0x00C625D4` write only
zero, one, or two. `FUN_005F7756` writes one at `0x005F7765`; this is the unit-placement setup
phase. `FUN_005F7893` briefly writes zero at `0x005F78F5`, then one at `0x005F7901`, during
initialization. `FUN_005F7979` writes two at `0x005F7986`; this is live combat. No other direct
writer exists in the module-9 reference set.

Phase and modal state are separate axes:

- Phase 1 permits the Setting Menu and the Start Game prompt. It has no moving enemy line.
- Phase 2 permits selection of allied units, their command menu, destination selection, reports,
  pause and help.
- Transition modals 2, 3 and 4 consult the phase while moving the camera or panel, but do not turn
  a modal number into a different choice list.

This agrees with the native input split in `FUN_005FD958`: Cancel in phase 1 opens modal 10,
whereas Start and Assist are handled only when phase is not 1. The reader must therefore expose
both phase and modal; neither substitutes for the other.

## Finding 4 — complete modal dispatcher map

**Proved from code except where explicitly labelled.** `FUN_005FC96F` has handlers for every
integer from 1 through 17. The direct writers prove the reachable nonzero set is 2 through 17
except 13. State 1's handler (`FUN_005FCE86`) immediately writes zero; state 13's handler
(`FUN_005FD0B3`) only advances a camera interpolation counter. Neither has a direct writer in
the executable, so they are defensive/legacy dispatcher cases rather than a currently proved
overlay. Modal zero is ordinary interaction and can still contain the independent report or
Ally Unit interfaces described below.

| Modal | Meaning and what is drawn | Choice? | Native evidence |
| ---: | --- | :---: | --- |
| `0` | Ordinary battlefield UI. Depending on the independent axes, this can be cursor mode, the Ally Unit command list, destination selection, or an actionable report panel. | Sometimes | `FUN_005FD958` |
| `1` | Defensive clear state; its entire handler sets modal zero. No direct writer was found. | No | `FUN_005FCE86` |
| `2` | Camera/selection transition into a report target or destination. The existing battlefield remains visible while the camera and selected-unit hit test settle. | No | `FUN_005FCE95` |
| `3` | Camera/selection transition back from destination mode. | No | `FUN_005FCFB4` |
| `4` | Battle entrance/camera reveal. It brings the active-play UI on screen, then clears when the camera reaches its target. | No | `FUN_005FD112` |
| `5` | Unit action/combat resolution transition for the selected unit. It advances the action until the unit can return to ordinary play. | No | `FUN_005FD245` |
| `6` | Unit action/removal resolution. It resolves the selected slot through `FUN_005FF2E0`/`FUN_005FBD2F` and returns it through `FUN_005FC2CA`. | No | `FUN_005FD2F6` |
| `7` | **Setting Menu**, the hire list already spoken by Blind Soldier. | **Yes** | `FUN_0060378B`, `FUN_00604208` |
| `8` | **Direction selection immediately after hiring certain stationary/directional units.** The game draws its direction indicator; OK commits, Cancel removes the new unit and refunds it. | **Yes, blocking** | `FUN_00604009`, `FUN_006046A7`, `FUN_006047AC` |
| `9` | **PAUSE** overlay. Start resumes. Directional input can move the view; Cancel cycles the internal battle-wave record rather than selecting a text row. | Actionable | `FUN_005FCB75`, `eunit01` texture |
| `10` | **Start the game? Yes / No.** | **Yes, blocking** | `FUN_005FCD6D`, `FUN_005FD958`, `emes00` texture |
| `11` | End/result hold. It lasts up to 600 ticks and can be dismissed after its panel reaches the final position, then requests module return state 4. | No menu | `FUN_005FCAE6` |
| `12` | Timed end/result hold lasting 360 ticks, then requests module return state 5. | No menu | `FUN_005FCA8E` |
| `13` | Defensive camera interpolation handler with no direct writer found. | No | `FUN_005FD0B3` |
| `14` | **ASSIST help overlay**, drawn from `ehelp`/`ehelp1`; it shows the controls for Cursor, Setting Menu, Report and Ally Unit modes. It closes when the external help display returns. | Informational | `FUN_005FD8ED`, `FUN_005FBCDF` |
| `15` | **Crowded-unit selector** when more than one unit overlaps the cursor. Up/down chooses one; OK selects it; Cancel restores the prior selection. | **Yes, blocking** | `FUN_005FD3A0` and its nested `FUN_005FB754` loop |
| `16` | **Direction selection for an existing stationary unit's Direction command.** OK commits the direction; Cancel restores the old values. | **Yes, blocking** | `FUN_00603230`, `FUN_0060484B`, `FUN_006047AC` |
| `17` | Report-panel slide-in animation. When it reaches its final position modal returns to zero, but the independent report state remains active and actionable. | No by itself | `FUN_006027C2`, `FUN_005FCA51`, `FUN_005F88F3` |

States 2 through 6 and 11 through 13 do not expose text rows or a selection index. Speaking
invented options for them would give information the sighted interface does not provide. States
8, 10, 15 and 16 do expose choices and currently go completely silent.

## Finding 5 — exact option state for every missing blocking choice

### Start Game — modal 10

**Proved from code and game data.** Cancel in setup initializes the 16-bit selection at
`0x00CBC7D8` to `0x10` and writes modal 10. The directional branch toggles it between `0` and
`0x10`. OK with zero writes module return state 3, starting combat; OK with `0x10`, or Cancel,
closes the prompt. The `emes00` texture supplies the exact text `Start the game?`, `Yes`, `No`.
The native draw uses the 16-pixel offset, so `0 = Yes` and `16 = No`. This prompt is mandatory:
the setup phase cannot become combat until Yes is confirmed.

### Crowded-unit selector — modal 15

**Proved from code.** `FUN_005FD958` calls `FUN_005FD3A0` when the hit-test count at
`0x00CBCC98` is greater than one. The selector uses:

```text
candidate pointer list: 0x00C60980, one pointer every 8 bytes
candidate count:        int16 0x00C61BF4
highlighted row:        int16 0x00C74C68
selected live slot:     int16 0x00C6097C
```

Each pointer names a live record in the 40-slot table at `0x00CBCCD8`, stride `0x78`; therefore
`slot = (pointer - 0x00CBCCD8) / 0x78` after range and alignment validation. The selector also
builds display records at `0x00C74CD0`, stride `0x2C`, whose unit pointer is at `+0x18` and slot
at `+0x1E`. Up/down wraps the highlighted row. OK leaves the chosen slot selected; Cancel
restores the prior one. It is blocking whenever two hit boxes overlap because no unit command
can be opened until one is chosen.

### Direction selection — modals 8 and 16

**Proved from code.** Both use the same signed 16-bit selection word at `0x00C625D0` and the same
`FUN_006047AC` input path. One direction adds `0x20`, the other subtracts `0x20`; the value is
clamped from `0` through `0x400`, inclusive. The game stores `selection - 0x200` into the unit's
direction field at `+0x34`/`+0x36`, making 33 discrete selectable positions.

`FUN_00605D59` converts that signed angle to the arrow vector through the executable's sine and
cosine helpers (`FUN_007AFECD` contains `FSIN`; `FUN_007AFE1D` contains `FCOS`). The angle scale
is `0x1000` units per full turn. Therefore the actual visual arc is proved, not inferred:

```text
selection 0x000: 45 degrees right of down
selection 0x200: straight down
selection 0x400: 45 degrees left of down
```

Each `0x20` step is 2.8125 degrees. The reader rounds to the nearest whole degree and also says
the one-based position out of 33, giving the same arrow orientation a sighted player sees.

Modal 8 follows a new hire. OK commits; Cancel deletes that just-bought unit, decrements the
allied count and refunds its price. Modal 16 follows command ID 5 (`Direction`) for an existing unit;
it preserves the old direction in `0x00C75284` and old command byte in `0x00CBC808`, then restores
them on Cancel. Both block the player until OK or Cancel.

### Ally Unit command list — interaction mode 2, modal 0

The complete address and command-ID mapping is in Finding 2. This is the most likely match for
the original report: it is a normal-looking option list, but because its modal is zero the old
tracker treated it as if no menu existed. The selectable **Action** command then enters mode 3,
where the player must choose a destination and confirm it. The yellow `MOVE` word elsewhere in
the texture is the selected unit's status, not this menu row.

### Destination cursor — interaction mode 3, modal 0

**Proved from code.** Destination mode does not use the ordinary battlefield cursor at
`0x00CBCCC0/2`. Its independent adjacent signed 16-bit pair is:

```text
destination X: 0x00C75268
destination Y: 0x00C7526A
```

`FUN_00603230` initializes this pair when Action opens destination mode. `FUN_005FE8CF` selects
`0x00C75268` as the cursor base for the mode-3 movement path, and `FUN_005FD958` reads the pair
when OK or Cancel handles the destination. Speaking the ordinary cursor here would give a stable
but wrong coordinate. The shared reader therefore reads and confirms this separate pair while
mode 3 owns input; the speech tracker coalesces its movement and says the coordinate where it
settles.

### Pause and Assist — modals 9 and 14

**Proved from code.** Pause is not a hidden row menu. `FUN_005FCB75` handles modal 9: Start input
(`0x800`) resumes or returns, directional input moves the view, Cancel (`0x40`) cycles
`0x00CBEDD8`, and the Assist-like input (`0x10`) requests a module return. `0x00CBEDD8` is loaded
from battle-data byte `0x00DC0985`, clamped to `0..6`, selects the battle-wave record consumed by
the enemy spawn script in `FUN_00607727`, and is consulted by the result branch in
`FUN_005F7D5B`. It is not drawn as a list of seven text options; `FUN_006091FC` only rebuilds the
visible allied/enemy count sprites after it changes. The honest accessible
equivalent is therefore to announce `Paused.` once and `Battle resumed.` on exit, while leaving
the ordinary visible status available through its existing hotkey; naming seven invented rows
would give information the game does not show as a menu.

Modal 14 is likewise informational rather than selectable. `FUN_005FD8ED` opens the external
`ehelp`/`ehelp1` control reference and `FUN_005FBCDF` waits for it to close. The reader speaks the
four mode-specific control descriptions once on entry.

### Game-speed gauge — independent of modal and interaction mode

**Proved from code.** The signed 16-bit speed level is at `0x00C752B4`. `FUN_005F7979`
initializes it to 2. Four input branches in `FUN_005FD958` decrement or increment it and clamp it
to `1..4` (`0x005FE562..0x005FE617`); the duplicate branches cover the two corresponding input
bindings. `FUN_005F88F3` compares it with 2, 3 and 4 and draws three successive gauge markers,
so the unlit minimum plus those markers give four visible levels. This is not a modal row list,
but it is immediate visual feedback for the Page Up/Page Down controls. The shared snapshot now
carries the level, status includes it, and a change is announced as `Game speed N of 4.`

## Finding 6 — reports are a third independent axis, not a modal menu

**Proved from code and game data.** `FUN_006027C2(messageCell, unitSlot)` writes:

```text
report state:        int16 0x00C72DEC = messageCell + 1
reported unit slot:  int16 0x00C72E3C = unitSlot
message texture row: int16 0x00C60AC4 = messageCell
panel Y offset:      int16 0x00C72DFC = -32
modal state:         17
```

`FUN_005F88F3` draws the report whenever report state is nonzero. Modal 17 merely slides it in;
its handler then clears the modal while leaving the report open. With modal zero,
`FUN_005FD958` gives the report priority over all three interaction modes: OK sends a command to
the reporting unit and Cancel lets it move freely. This proves the live `mode=1, modal=5` and
`mode=2, modal=0` observations cannot be interpreted from the modal alone.

Only three native call sites open reports in this executable:

| Call site | Message cell | Exact `emes00` text |
| --- | ---: | --- |
| `FUN_005FFCE9` | `0` | `Encountered enemy.` |
| `FUN_00600247` | `3` | `Arrived at the directed position.` |
| `FUN_005FBD2F` | `10` | `Set units.` |

The resulting report-state values are 1, 4 and 11. The decoded help texture states the choice
semantics explicitly: OK sends a command to the reporting unit; Cancel lets it move freely.
That instruction is actionable and must be spoken when the report opens.

## Finding 7 — implementation follows the native input ownership

The uncommitted implementation is shared source, so the same reader and speech state machine are
compiled into x86 and x64:

- `Ff7.Accessibility.Reloaded/CondorBattleSnapshot.cs` defines the two native list shapes and
  carries every proved selection, report and destination field.
- `Ff7.Accessibility.Reloaded/CondorBattleStateReader.cs:231-240` activates the relevant reader
  only when its native mode/modal/report state owns input. Ally Unit rows are resolved at
  `:280`; crowded-unit pointers at `:328`; report coherence at `:407`; and the independent
  destination cursor at `:448`.
- The translated x64 address space can observe two game frames during a multi-read snapshot.
  Consequently the reader rechecks menu count/row pairs, Start and Direction selections, the
  report state/message/slot triple, the destination X/Y pair, and the top-level mode/modal/report
  anchors. A changing choice is delayed one 100 ms sample rather than announced as a selection
  the game never displayed.
- `Ff7.Accessibility.Reloaded/CondorBattleSpeechTracker.cs:461` mirrors native ownership order:
  report first, then modal choices, then independent interaction mode. It announces menu entry
  and selection changes, suppresses ordinary cursor speech while an interface owns input, and
  leaves the current blocking choice as the last line after a completed hire. Direction wording
  is derived from the proved vector at `:592`.
- `Ff7.Accessibility.Reloaded.Tests/CondorBattleReaderTests.cs:146` exercises the native memory
  layout for every blocking choice. The speech sequence begins at `:200`, and the translated-read
  destination tear regression is at `:269`.

## Finding 8 — independent review closed two paths back to apparent silence

An adversarial review caught two defects before live deployment.

First, every native access to the direction selector at `0x00C625D0` is a word access
(`MOV AX/CX`, `MOVSX ..., word ptr`, or `MOV word ptr`). The first implementation read four bytes,
which incorrectly included the unrelated two-byte gap before phase at `0x00C625D4`; nonzero gap
bytes would have made both direction modals fail validation forever. The shared reader now reads
and confirms a signed 16-bit value. Its regression leaves those two adjacent bytes deliberately
nonzero and proves modals 8 and 16 still read, while malformed steps and a changing confirmation
read fail closed.

Second, a correct row returned with noninterrupting delivery can still be inaccessible: rapid
navigation queues choices the player has already left. Stateful interface updates now supersede
older state on both hosts. If a banner, casualty, hire completion, or other one-shot event occurs
in the same sample, the ordered event and blocking prompt are joined into one utterance before it
is allowed to interrupt, so curing stale rows cannot erase a once-only event. Unknown command IDs
and report cells are rejected against the finite native sets `{0,2,3,5}` and `{0,3,10}` rather
than exposing developer-facing numeric IDs to the player.

This intentionally does not speak synthetic choices for transition states 2 through 6 or result
holds 11 through 13. Those states draw animation or a hold, not a selectable list. Pause and
Assist are announced once because they are visible overlays; reports include both their exact
message and the visible OK/Cancel meaning.

## Repeating the native analysis

All passes are read-only against the existing Ghidra project:

- run them from the repository root;
- the checked-in runners use the repository's `.tools/ghidra_12.1.2_PUBLIC` and the installed
  Microsoft JDK 21;
- `%TEMP%\BlindSoldierCondorGhidra\CondorPlacement` must contain `ff7_en.exe`, imported from the
  supported x86 executable with MD5 `72DF0999B2FAD9AE2AA721CE67D8C3AB`.

The direction pass is a candidate-discovery pass over `+0x34/+0x36` operands. The conclusion
above is not based on that text match alone: it is confirmed in the anchored command dispatcher
and direction-update functions dumped by `RunCondorFunctions.cmd`.

```cmd
analysis\ghidra\RunCondorModalStates.cmd
analysis\ghidra\RunCondorFunctions.cmd 005FD958 005FC96F 005FCB75 00603230 006047AC
analysis\ghidra\RunCondorGlobals.cmd 00C625E0 00C74C50 00C625D4 00C75268 00C7526A 00C752B4
analysis\ghidra\RunCondorRange.cmd 005FE520 005FE620
analysis\ghidra\RunCondorDirectionEvidence.cmd
```

`DumpCondorModalStates.java` discovers references to the modal, interaction-mode, phase and
choice globals, expands one call layer, and emits instructions plus decompiled C.
`DumpCondorFunctions.java` is the targeted function pass. `DumpCondorGlobals.java` classifies
read, write and address-taking references for exact globals. `DumpCondorDirectionEvidence.java`
finds the selected unit's `+0x34/+0x36` direction fields and decompiles their consumers so the
33-step visual arc can be reconstructed from native trigonometry instead of guessed from the
texture label.
