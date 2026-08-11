# Kalm Through Lower Junon Description Cue Evidence

## Scope and method

This audit covers the original-game story from the Kalm inn flashback through the first Lower Junon scenes and the transition into upper Junon. Visual evidence came from original FFVII footage split into ten clips under `.artifacts/vidscribe-clips/`, direct frame inspection, and Vidscribe playback. Vidscribe's free tier exposed its narration only through playback, so its output was treated as a supplemental check rather than copied as text. Vanilla `flevel.lgp` scripts are the trigger authority; Echo-S data is not used.

The x86 movie lifecycle flag at `0x00CC1638` was checked in Ghidra against the installed `ff7_en.exe`. Ghidra found 15 native references, including writes of zero and one and reads by the movie loop. This confirms that the existing movie-active signal is appropriate for ending or skipping FMV-owned narration. The retained FMVs need one concise description each, so no delayed multi-beat timeline is required for this slice.

## Implemented cue table

| Sequence | Visual evidence | Sighted-equivalent narration | Native trigger | Opcode | Disposition |
|---|---|---|---|---|---|
| Kalm framing transition | Clip 1, opening | The upstairs room fades away as Cloud's story becomes a memory from five years earlier. | `elminn_2`, field 332, entity 5, script 3, byte 238 | `WAIT` `0x24` | Add |
| Shinra truck | Clip 1, opening truck scene | Inside a swaying Shinra truck, sixteen-year-old Cloud rides through heavy rain beside Sephiroth and two masked infantrymen. | `trackin`, field 277, entity 4, script 1, byte 0 | `SOUND` `0xF1` | Add |
| Nibelheim arrival | Clip 1, after the dragon battle | Cloud, Sephiroth, and two infantrymen arrive outside the misty mountain town of Nibelheim. | `nivgate`, field 279, entity 2, script 1, byte 4 | `REQEW` `0x03` | Add |
| Tifa revealed as guide | Clip 2, start | Tifa arrives as their guide, wearing a wide-brimmed cowboy hat, boots, and a short skirt. | `nivl`, field 282, entity 8, script 1, byte 48 | `REQEW` `0x03`; calls Cloud's reveal reaction | Add |
| Group photograph | Clip 2, start | The photographer snaps a picture of Tifa standing between Cloud and Sephiroth. | `nivl`, field 282, entity 11, script 13, byte 32 | `SOUND` `0xF1` | Add |
| Mt. Nibel establishing FMV | Clip 2, mountain ascent | Jagged peaks and deep ravines surround Mt. Nibel as the group climbs toward the reactor high on the mountainside. | `mtnvl2`, field 311, entity 0, script 0, byte 207 | `MOVIE` `0xF9`, movie 31 `mtnvl.avi` | Add |
| Rope bridge collapse | Clip 2, bridge | The rope bridge tears loose. Tifa, Cloud, Sephiroth, and the two infantrymen plunge into the ravine. | `mtnvl3`, field 312, entity 10, script 3, byte 106 | `MOVIE` `0xF9`, movie 33 `brgnvl.avi` | Add |
| Party regroups | Clip 2, after the fall | Cloud, Sephiroth, Tifa, and one infantryman regroup on a rocky ledge below the broken bridge. | `mtnvl4`, field 313, entity 0, script 0, byte 50 | `WAIT` `0x24` | Add |
| Mako spring | Clip 2, cave | The cavern opens around a luminous turquoise Mako spring, with glowing energy streaming through the rock. | `nvdun2`, field 318, entity 8, script 3, byte 26 | `SPLIT` `0x09` | Add |
| Reactor pod inspection | Clip 3, reactor chamber | Cloud peers through the pod's small window and recoils from a malformed human shape suspended inside. | `nvmkin21`, field 323, entity 8, script 1, byte 48 | `REQ` `0x01` | Add |
| Reactor pod FMV | Clips 3-4, reactor conclusion | A metal pod bursts open, spilling a twisted human-shaped creature onto the reactor floor. | `nvmkin21`, field 323, entity 9, script 7, byte 236 | `MOVIE` `0xF9`, movie 34 `nvlmk.avi` | Add |
| Kalm interlude | Clip 4, after the reactor | The memory pauses. Back in the Kalm inn, Cloud's companions sit around him as he continues the story. | `elminn_2`, field 332, entity 5, script 4, byte 3 | `REQEW` `0x03` | Add |
| Mansion basement library | Clip 4, Shinra Mansion | In the mansion basement, Sephiroth sits alone at a circular library desk, reading research notes by lamplight. | `sininb31`, field 304, entity 0, script 0, byte 66 | `WAIT` `0x24` | Add |
| Burning Nibelheim | Clip 5, town return | Nibelheim is ablaze. Flames pour from the houses as injured villagers lie across the square. | `nivl_b1`, field 290, entity 1, script 1, byte 4 | `SOUND` `0xF1` | Add |
| Sephiroth in the flames, entry variant one | Clip 5, fire FMV | Framed by the burning town, Sephiroth turns toward Cloud, then walks away through the flames with his sword in hand. | `nivl_b2`, field 292, entity 1, script 1, byte 22 | `MOVIE` `0xF9`, movie 35 `nivlsfs.avi` | Add |
| Sephiroth in the flames, entry variant two | Same FMV, alternate native entry | Same narration as the other entry variant. | `nivl_b2`, field 292, entity 2, script 1, byte 10 | `MOVIE` `0xF9`, movie 35 `nivlsfs.avi` | Add |
| Tifa takes the Masamune | Clip 5, reactor return | Existing shared cue: Tifa rises, seizes Sephiroth's sword, and runs deeper into the reactor. | `nvmkin1`, field 322, entity 6, script 3, byte 104 | `WAIT` `0x24` | Already covered; do not duplicate key |
| Jenova chamber FMV | Clip 6, confrontation | Sephiroth tears away the metal figure covering Jenova's chamber. Cloud confronts him beneath the exposed form. | `nvmkin32`, field 327, entity 0, script 0, byte 290 | `MOVIE` `0xF9`, movie 36 `jenova_e.avi` | Add |
| Flashback ends | Clip 6, return to Kalm | The flashback ends. Back at the Kalm inn, Cloud sits with the others, unable to remember how the confrontation ended. | `elminn_2`, field 332, entity 4, script 0, byte 85 | `REQEW` `0x03` | Add |
| Chocobo dance | Clip 7, Chocobo Farm | Four yellow chocobos line up and perform a lively synchronized dance. | `farm`, field 343, entity 9, script 1, byte 24 | `REQ` `0x01` | Add |
| Impaled Midgar Zolom | Clip 8, marsh aftermath | A gigantic Midgar Zolom hangs impaled high on a dead tree, its body twisted around the trunk. | `sichi`, field 348, entity 0, script 0, byte 13 | `SPLIT` `0x09` | Add |
| Turks in Mythril Mine | Clip 8, mine encounter | In the mine, Rude blocks the passage while Elena and Tseng stand behind him in dark blue Turk suits. | `psdun_1`, field 349, entity 0, script 0, byte 99 | `REQ` `0x01` | Add |
| Lower Junon arrival | Clip 9 | The party enters Lower Junon, a dim fishing village beneath the towering Shinra fortress. | `ujunon1`, field 428, entity 5, script 0, byte 142 | `REQEW` `0x03` | Add |
| Priscilla attacked | Clip 10, before Bottomswell | A flying sea creature snatches Priscilla from the shore and drags her toward the water. | `ujunon2`, field 429, entity 2, script 0, byte 117 | `REQEW` `0x03` | Add |
| Bottomswell aftermath | Clip 10, after victory | After the fight, Priscilla lies motionless on the wet beach while the party gathers around her. | `ujunon4`, field 434, entity 1, script 0, byte 9 | `REQSW` `0x02` | Add |
| Junon establishing FMV | Part 19 transition into upper Junon | The view sweeps from Lower Junon up the vast metal fortress to the Mako cannon and airfield above. | `junon`, field 359, entity 0, script 0, byte 79 | `MOVIE` `0xF9`, movie 37 `junon.avi` | Add |

## Explicit exclusions

- The dragon and Bottomswell fights are ordinary battles. Enemy names, attacks, damage, and battle results belong to the battle accessibility system, not cutscene narration.
- Choco Billy's catching tutorial, the Turks' conversation, Kalm exposition, and CPR instructions are readable dialogue or menu text. Describing them again would duplicate speech.
- Ordinary town, cave, mine, and world-map walking remains under zone, object, story-event, and navigation speech.
- The CPR gauge is an interactive UI and should be handled by its native text/state reader; no visual-only strategy is added.
- Each scoped FMV has one short, meaningful visual beat. A delayed timeline would risk narration surviving a skip without adding equivalent information, so the native `MOVIE` opcode cue is the safer implementation.
