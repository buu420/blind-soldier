# Floor 65 model placement

The November 25 Downloads log contains the November 24 model-puzzle attempt.
Story offered the first four placements, then repeatedly logged `field=249: none`
after the outer floor had offered carrying the last piece into the model room.
The player eventually placed it manually. The log does not retain every bank
value, so it does not establish the exact order in which the native slots filled.
Its Story lines do show that the second part went into the bottom slot: at
00:45:57 the outer floor offered the fourth, third and final chests together,
which only the bottom slot unlocks at that point.

The previous final target required both the persistent final-chest flag and
exactly the four assumed prior slot bits. Other preferred placements also paired
one carried-piece bit with one model slot, with no alternative when the player
used another slot. That is stricter than the game: stock and all five reviewed
Echo-S layouts' CITYA-E routines accept any carried piece (1[68], bits 3-7), ask
the native placement question, clear those carry bits, and set their own slot bit
in 3[180]. A collected-chest flag alone is not evidence that a piece remains held.

The existing preferred route remains first. The last piece uses its live carry
bit; recovery targets offer native empty slots whenever a different placement
order leaves the preferred slot occupied. No inventory or puzzle state is written.
Navigation now reaches a native activation triangle before announcing arrival:
CITYA 75/31, CITYB 68/16, CITYC 33/6, CITYD 70/34, CITYE 26. Confirm and the game's
placement choice remain under player control.

## Chests and the way back

The outer floor's Talk scripts (blin65_1, the same in stock and all five reviewed
Echo-S archives) decide which chest opens:

| Chest | Opens when | Gives | Records |
| --- | --- | --- | --- |
| PARTD | always | 1[68] bit 6 | 1[57] bit 1 |
| PARTB | 3[180] bit 6 or bit 7 | 1[68] bit 4 | 1[56] bit 7 |
| PARTC | 3[180] bit 5 or bit 7 | 1[68] bit 5 | 1[57] bit 0 |
| PARTA | 3[180] bit 3 or bit 7 | 1[68] bit 3 | 1[56] bit 6 |
| PARTE | 3[180] bit 4 or bit 7 | 1[68] bit 7 | 1[57] bit 2 |
| TAKARA | all of 3[180] bits 3-7 | 1[224] = 66 | 1[57] bit 3, 1[68] bit 1 |

PARTB, PARTC, PARTA and PARTE also test 1[68] bits 3-7 and show dialog 2,
"Locked…", while any part is carried. PARTD has no such test, and nothing can be
carried before it. The bottom slot therefore unlocks every remaining chest, and a
part placed in the top slot unlocks PARTE, the chest labelled final, early.

Two Story rows did not follow these rules:

- "Return to the outer floor for the next unlocked chest" was complete once PARTE
  had been opened. Whenever PARTE was not the last chest, the model room offered
  nothing after that placement or any later one until the model was complete.
  It now follows the slots: it is offered whenever no part is carried, including
  before the first chest, and is complete when all five slots are filled. With no
  part carried every opened chest's part has been placed, and each of PARTB, PARTC,
  PARTA and PARTE is unlocked by a different slot, so with one to four slots
  filled at least one unlocked chest always remains.
- The PARTB, PARTC, PARTA and PARTE rows now also require that no part is carried,
  so the outer floor offers only "Carry the Midgar part into the model room" then,
  instead of chests that answer "Locked…". PARTD is unchanged.

Keycard 66 completes "Return to the outer floor and collect Keycard 66", and the
model room then offers "Return to the outer floor and continue to floor 66", the
way to the outer floor's existing "Continue to floor 66 with Keycard 66".

## Evidence

Ghidra read-only analysis of ff7_en.exe SHA256
`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`
confirms IFUB at 006116A6, IFUW at 00611A89, ASK at 00618E83, BITON at 00611098
and BITOFF at 00611102. The bit handlers use the native bank readers/writer and
set/clear the indexed bit, advancing four script bytes. The game is unmodified.

`MidgarModelPlacementTests` (`--midgar-model-only` in both test runners) walks
every legal native order of chests and slots: 816 complete orders through 277
distinct stages. At each stage both rooms must offer exactly the next native step:
the openable chests, only the carry target while a part is carried, only empty
slots, the single way back, then Keycard 66 and floor 66. The walk must include
orders that fill the top or bottom slot first and open PARTE before the last chest.
Before this change it failed on the silent model room, on a chest offered while
carrying, and on the keycard row after Keycard 66 had been collected.

It also enumerates all five carried pieces against every incomplete five-slot
arrangement and rejects placement with no carried piece. With the data roots set
it reads blin65_1 and blin65_2 from stock and all five Echo-S archives: each
chest's slot, carry, lock and record instructions, TAKARA's five slot tests and
its keycard write, each CITY entity's carry tests, carry clears, slot bit and
activation triangles, and a route from the model-room entrance to every
placement target. These are native-data and automated navigation checks, not a
live controller or listener test.

## Limits

- The chest and placement labels keep their ordinals ("second", "final"). They
  describe the preferred order. After a different placement order the location is
  still right, but the ordinal can be wrong; a chest labelled final may not be last.
- The generator contains the same 16 floor-65 rows, including native completion
  triangles and the carried-part condition. `tools/Complete-StoryCatalog.Tests.ps1`
  executes those definitions through the generator's real functions and compares
  every field with the shipping catalog, so regeneration preserves the fix.
