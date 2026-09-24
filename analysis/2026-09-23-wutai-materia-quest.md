# Wutai: the Materia quest

The x64 log of 23 September (Downloads `(11)`) enters Wutai at GameMoment 566 and
goes to the Item Store first. There the Objects list offers "MP Absorb Materia",
which says "direction unavailable" or walks to the counter and pauses on a Confirm
that does nothing. Story says nothing in any Wutai room, and the log never visits
field 580, where the prerequisite conversation takes place. The 0.6.7 Wutai change
addressed the world-map walk; it did not add the town's quest sequence to Story.

## Why nothing was offered

The quest writes no GameMoment at all, so milestone extraction found none of it: all
25 Wutai fields (575–599) had zero Story rows. Its order lives in five bytes its own
scripts write, and the chest in the Item Store was catalogued from its "Received"
message with neither of the bits that govern it.

## Native evidence

Read from the installed archives with `tools/FieldInteractionAudit`; fields 575–599
have identical native hashes in the legacy and Steam 2026 archives. Bank 3 is
`Bank[3]`, 5 is the temporary bank, 13 is `Bank[13]`.

| Step | Native gate and write |
| --- | --- |
| Arrival | uutai1 Director requests AD Script 3 while 3[207] bit 5 is set and 3[189] bit 5 is clear; it sets bit 5. |
| Turtle's Paradise | utapb RENO 15 and LUDE 14 Talk test 3[189] bit 1 and `REQ 0A C3` (AD Script 3), which sets it. IRENA 13 Talk does nothing until it is set. |
| Item Store chest | uta_im TAKARA 7 Talk: `IFUB 3[189] & 2` or return; `IFUB 1[59] & 2` or return; `BITON 1[59] 1`; the theft; `BITON 3[190] 0`. OYAJI2 6 Init stands at (146,158), in front of the chest, while 3[189] bit 1 is clear. |
| Folding screen | utmin2 BYOBU 16 and BYOBUB 17 are enabled only while 3[190] bit 0 is set and bit 1 clear; their Go tests Confirm and runs YUFI Script 3, which sets 3[190] bit 1. |
| Shaking pot | uutai1 AD2 holds triangles 212 and 213 and requests YUFI 16 Script 4 while 3[190] bit 1 is set and 3[189] bit 2 clear: `XYZI (-1175,-776)` triangle 214, `TLKON 0`, `TALKR 180`, no `VISI`. Her Talk (Script 1) is the only caller of the capture and its own `VISI 1` is what first shows her. AD Script 5 sets 3[189] bit 2. |
| Yuffie's house | uutai1 AD holds triangle 170 until 3[189] bit 2. yufy1 Director plays "Follow me" while 3[190] bit 2 is clear (YUFI Script 4 sets it); yufy1 AD holds triangle 1 until 3[189] bit 0. |
| The levers | yufy2 SWITCH 11 Init turns itself off before the trap. YUFI 7 Talk requests AD Script 3 while 5[0] is 0; her Script 7 sets 5[0] and turns SWITCH on. Either answer sets 3[190] bit 4 and drops the cage; the first time YUFI Script 9 sets 3[189] bit 0. SWITCH with bit 4 set raises the cage, clears bits 4–6 and sets bit 7. |
| The bell | uutai2 AD holds triangle 114 before 3[189] bit 0, and triangle 128 after it unless 5[3] is 1 (set on arrival from uttmpin4). KANE 14 polls triangles 138, 139, 140 and 86 with Confirm, releases 128 and sets 5[3]. |
| Corneo | uttmpin4 AD sets 3[191] bit 0 as Corneo runs to (280,446) beside gateway 0. uttmpin3 AD fights battle 622 on arrival and sets bit 1; its Director holds 18, 19 and 22 on arrival from uttmpin4 and 21 on arrival from uttmpin2. |
| Reno | uutai2 RENO 12 shows while 3[191] bit 1 is set and bit 2 clear, holds triangle 33, and his Talk sets 3[191] bit 2 and 13[80] bit 5. |
| Da-chao and the return | datiao_6 plays the rescue on arrival while 13[80] bit 5 is set and 15[136] bit 7 clear, sets 13[80] bit 6 and map-jumps into yufy1, whose Director returns the Materia (3[189] bit 4, 3[207] bit 6). yufy1 MATE 13 then holds the MP Absorb reward, already an Object. |

## What changed

- **Story.** `tools/story-regions/Wutai.ps1` adds 45 rows, every one inside the
  quest's own window (3[207] bit 5 set, bit 6 clear). A step's rows are priority 0.
  The related shops and houses, the Pagoda exterior, and Da-chao paths also carry
  return rows at priority 50. The step's row, where there is one, is the only row.
  Godo's house is an optional
  puzzle house and has rows only for the long way back to Reno after the fight.
  Labels name only what is seen: the pot shakes, the bell hangs in view, and Corneo
  runs out of the Hidden Room. Neither lever is named, and the Hidden Room is not
  mentioned until the bell has opened its door.
- **Hidden Talk.** `FieldStoryEventDefinition.UsesHiddenTalkTarget` lets one Model
  row ask what `TLKON` says instead of whether the model is drawn. Only the pot uses
  it; every other Talk row still needs a visible model.
- **The chest.** 576/7 is now "Treasure chest" with a chest cue, required
  3[189] bit 1 and collected 1[59] bit 1. The label does not promise Materia that
  is stolen the moment it is received. `tools/Generate-FieldNavigationObjects.ps1`
  produces the same row and fails if the two native writes drift.
- **The hanging scroll.** Coming back from hideway1, uttmpin1/JIKU's Init holds
  triangle 22, leaving triangles 94–97, and the catalog only had the front of the
  scroll. The log loops between 583 and 588 for minutes. "Hanging scroll, other
  side" sits at (-440,-44), inside the reverse handler's triangle 95 and its
  leader-X > -619 guard. Objects gained optional required and excluded player
  triangles so each face is offered only from its own side.
- **The bell.** The optional Bell object now requires 3[189] bit 0. Before the trap
  uutai2/AD holds triangle 114, and a route replay from each native arrival into the
  Pagoda (from 579, 586 and 588) finds no way up to the bell without it. This is
  consistent with the log's three "Bell. direction unavailable" reports.
- **The Hidden Room entrance.** The Exits list now offers `gateway:587:0:591` only
  when the bell-open flag, temporary 5[3], equals 1. KANE writes it after opening
  the passage; AD also writes it when returning from the Hidden Room. Both runtimes
  use `WutaiBellDoorStateReader`, which reads the flag twice between field/module
  ownership checks. Unreadable, changing, or wrong-field state cannot publish the
  entrance. This prevents a pre-bell route from the platform above the closed
  passage without changing general pathfinding or any other exit.

## Tests

`WutaiMateriaQuestTests` runs in both default suites, `--story-coverage-only`, and
`--wutai-quest-only`. It replays each phase through the production readers, checks
the native gates and every trigger against the installed archive, and plans 75
routes from native arrivals with the production planner under the triangle locks
each field's scripts hold at that point and the NPC bodies in the way.
`tools/StoryCoverageAudit/checkpoints.json` gained 14 Wutai checkpoints.
`WutaiHiddenRoomExitTests` covers the entrance policy, native bell/return writes,
failed or changing memory reads, reopening during the same visit, and both runtime
callers. It runs in both default suites and `--wutai-quest-only`.

## Limits

- None of this is a live playthrough. The capture, the second bar scene, "Follow
  me", the Hidden Room scene, the fight and the Da-chao rescue are automatic scenes
  whose outcomes are read from the scripts, not observed.
- The native Talk routine was not traced in Ghidra. That hidden Talk works is taken
  from the pot's own script, which cannot work any other way.
- Route replays use static locks and fixed NPC positions. Moving NPCs, and the
  Turks walking into place outside the Pagoda, are not simulated.
- Godo's house, the Pagoda of the Five Gods and the hidden passages off 588 and 589
  have no Story rows outside the way back to Reno.
