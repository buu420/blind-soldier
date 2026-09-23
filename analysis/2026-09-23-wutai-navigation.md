# Walking to Wutai

The x64 log of 23 September ends every automatic walk to Wutai on foot with "Could
not get closer": at 11:23:19 pinned at the edge of a pass at 52254,1938,156844, and
at 11:23:55 pacing the Wutai Bridge while the route line stayed 600 to 790 units
away. The earlier boat attempts (09:29:15, 09:32:29) were the Tiny Bronco being
routed over land, which the boat's water-only rule and shore landings already fixed.
The 11:19:08 walk entering field 572 is Yuffie's scene, the game's own behaviour.

## Native evidence

Extracted with Ghidra 12.1.2 from the installed legacy `ff7_en.exe` (SHA-256
`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`).
[`DumpWutaiWalkingEvidence.java`](ghidra/DumpWutaiWalkingEvidence.java) repeats it;
pass a private output path as its only argument.

| Native function | Relevant behavior |
| --- | --- |
| `0074CECA` | Terrain a model may stand on. Models 0–2: mask `0x721B6F83`, or `0x20006000` (13, 14, 29) while the party stands on 13 or 14. Wild chocobo `0x321B6F83`, Buggy `0x331B6F13`. None has bit 21. |
| `00762136` | The terrain the bridge rule tests: the low five bits of the walkmap word at player `+0x4A`. |
| `00751EFC`, `007530B3` | A step is taken only if all five contact points pass: the centre and `local_44` along each axis, `0x15E` for model 5 and `0xC8` for everyone else. Refused steps are retried turned by 160, 320 … 1120 units to each side. |
| `0074CC07`, `0076085F` | The surface under a point: edge-inclusive; the lowest for models 3 and 5, else the one nearest the party's height. |
| `0074EA48` | On foot the party moves `0x1E` a frame; diagonals three quarters. |

`wm0.ev` (SHA-256 `A020DACE94F21B73094C204C54B01E13B3688002F65FFD9B1EDF8ADA5E372A64`,
from `world_us.lgp`): the call table entry at byte `0x190` maps Wutai's handler
`0x96C4` (mesh 4,10, script 7) to IP `0x2DB3`, byte `0x5F66`:

```
00 01 1B 01 08 00 10 01 00 00 70 00 1B 01 08 00 10 01 01 00 70 00 C0 00
1B 01 08 00 10 01 02 00 70 00 C0 00 01 02 06 2E
```

That is: player model (special 8) equal to 0, 1 or 2, else jump to the return at
`0x2E06`. Every other branch is opcode `0x318` with world entry 23, Wutai. Handler
`0xAB23` (mesh 6,19, script 6), the band across the pass, uses entry 34 — field 572,
Yuffie's scene. It runs for models 0, 1, 2 and 4 while progress is below 1602, bit
`0xE78` is set and bits `0xE7E` and `0xE7D` are clear. It is the one mandatory
transition on the way; once the scene has played, the band does nothing.

Log evidence: none of the on-foot samples in the user's eleven session logs stands
on terrain 21. Near the Corel rail bridge they are 13, 20 and 29. Those logs hold
41,175 distinct on-foot positions. The centre rule fails for 2 of them. The 200-unit
footprint, allowing one frame for a position read mid-move, leaves 12 unexplained:
10 on the Wutai bridges and 2 near 199000,170000. On the bridges the party stood
closer to the deck edge than 200 units, so the game is more lenient there than
this model.

## What stopped the walks

A replay under those rules — the controller's own keys, a party that moves only
where its footprint fits and slides like `00751EFC` — met four problems in turn.

1. **Terrain 21 counted as walkable.** 172 of its 236 triangles are the sides of the
   three Wutai bridges and the Corel rail bridge. Routes ran beside the bridges.
2. **The pulled string ran along the edge of where the party fits.** In the pass it
   hugged the cliff and cut the mountain's corner between two such edges.
3. **Steering straight at a far waypoint on eight directions drifted off the leg.** At
   camera 3888, 13 degrees off over a 4,200-unit leg left the party 300 units into
   the foot of a mountain. The waypoint back-off then pointed behind the party.
4. **A steep face lies over the flat western bridgehead.** Triangles 83096 and 83163
   overlap in plan, four units apart. A segment followed from the face alone could
   not leave it, so from every other position the way on read as blocked.

## Changes

- Terrain 21 left the walking set. This does not change on-foot reachability
  between any pair of the 37 locations.
- Walking portals are narrowed to where the 200-unit footprint fits. Corners are kept
  64 units inside that stretch. A pulled leg between two fitting points that still
  crosses ground the party cannot stand on is bent round it. Where the party fits
  nowhere, the string is the shipped one.
- Walking auto-walk follows its leg, as the boat does. It aims four frames down the
  leg and counts as on the leg within two frames. It backs off to an earlier waypoint
  when the straight line would take the footprint where it cannot go. That check is
  skipped where the party already stands somewhere the model says it cannot, as on
  the bridges.
- The centre-line segment test starts from every walkable surface at the party's
  point within 32 units of its height.
- Wutai is entered on foot only, like Cosmo Canyon, from handler `0x96C4`.

## Verification boundaries

The regression replays four logged starts at five camera classes. It walks each one
into Wutai's native trigger and checks the bridge, the pass, the bridgehead and the
chocobo hand-off. Ten Mount Corel walks were replayed under the same rules. All
arrive; the shipped build arrives in none of them. The Tiny Bronco walk-back passes
its own collision replay. The world-map and full suites pass in both hosts.

This is replay against native rules and the installed map, not a live playthrough.
The model is stricter than the game on bridges. It uses the mod's walking set, which
still admits 24 and 27 though the native mask does not. Nothing here was found to
need them, and they were left unchanged.
