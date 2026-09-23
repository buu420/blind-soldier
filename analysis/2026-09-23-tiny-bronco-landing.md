# Tiny Bronco sailing and the weapon seller's world entrance

The new x64 log from 23 September records repeated failed automatic routes to
Gongaga while aboard the Tiny Bronco. Across 1,677 boat samples the native terrain
is 4, 5 or 6. The old planner also admitted walking terrain, so it steered toward
land the game refused. The same session reached the weapon seller's house on
foot, but the planner rejected routes from immediately outside it.

## Native evidence

Evidence was extracted with Ghidra 12.1.2 from the user's installed legacy
`ff7_en.exe`, SHA-256
`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`.
The installed legacy and x64 `wm0.map` files have the same SHA-256:
`43E72295212311FCEC3A513051A96CA4475185A72211EBC3A5E4F89A50ECA78C`.
The x64 reader translates the shared world-state layout. The new rotation reads
are optional: failure disables landing prediction without invalidating the other
world-state fields.

[`DumpTinyBroncoLandingEvidence.java`](ghidra/DumpTinyBroncoLandingEvidence.java)
repeats the extraction against an analyzed executable. Pass a private output path
as its single script argument. Decompiled code and player logs remain outside the
repository.

| Native function | Relevant behavior |
| --- | --- |
| `0074CECA`, `007666FF` | Boat sailing mask `0x70`: terrain 4, 5 and 6. |
| `007667B2` | A short Cancel press, released after 1–14 frames, starts getting off. |
| `00766417`, `00761EEC` | Probe 800 units from the committed position along model rotation `+0x3C` plus slide rotation `+0x3E`. Flag `0x80` selects a different variant excluded from this prediction. |
| `00751EFC` | Require five valid contact points: center and 350 units in each cardinal direction. Try the original probe, then fanned alternatives. |
| `0074CECA` | Getting off uses mask `0x20800`: riverside 11 and beach 17. |
| `0074CC07`, `0076085F` | Use an edge-inclusive triangle test and the lowest boat surface. |
| `00761C07`, `00761DF5` | Ease model rotation toward facing and settle the slide rotation. |
| `00765F61` | Trigger world entrances by mesh cell and native terrain-script ID. |

Three actual disembarkations in the supplied logs satisfy the five-point shore
test. The two at rest move the party 799 and 800 units from the boat. Older logs
did not include model rotation; new world diagnostics include facing, both
rotations and flags so live results can be compared with the prediction.

## Navigation behavior

The boat travels only on its native water terrain. For an inland destination,
the controller plans to a shore connected to that destination on foot. It checks
the boat's contact points, the party's landing contact points and a range of
headings. It avoids unrelated field entrances on the onward walking route.

Boat path corners account for its 350-unit contact radius. The planner narrows
crossings to where the boat fits and uses sampled movement to form sailing legs.
Steering aims a short distance along each leg, so a small sideways
deviation does not send the boat back to the previous corner. This fixes the
replayed oscillation on the longer approach to the seller's beach.

The final approach aims toward the landing ground. At rest, the controller checks
the actual boat rotation and the arc it will settle through before announcing
that the player can press Cancel to get off. Automatic movement stops; the
selected destination remains active for spoken navigation after disembarking.
The player can restart automatic walking on foot. The mod does not press the
disembark button or modify game state.

If a landing cannot be confirmed, the mod says so instead of instructing the
player to get off. A landing catalog is cached for each map and planner. These
are conservative sampled routes, not proof that every other shore is unusable.

## Weapon seller entrance

At world mesh cell (15,21), script 7 belongs to the seller's house. Its trigger
has eight roof triangles and twelve vertical walls and gables. The old code
exempted only the roof triangles when avoiding other towns, leaving the house's
own walls as obstacles.

The selected destination now exempts its complete native trigger. Vertical
faces are crossable only for that destination's entrance, and the route's funnel
stops collecting portals at the first such wall. Other entrances remain blocked.
Arrival uses the native mesh cell and script, including the logged case where
the engine rolls the position back just before entering field 79.

## Verification boundaries

The regression suites use the installed map and field archives. They cover the
two refused on-foot starts, multiple camera angles and walking speeds, boat
approaches from three logged positions, native collision and rotation replay,
settled landing prompts and continuing on foot. Tests run in both runtime hosts.
The separate seller tests cover the actual NPC, story gates, reward permission
and all 325 combinations of its five targets and connected floor triangles.

This is automated replay plus native evidence, not a live in-game playthrough.
The first user test should check shore arrival and disembarkation against the new
rotation diagnostics. Existing non-Bronco terrain-mask discrepancies found during
research are outside this fix.

The graph search choosing a channel still uses triangle connectivity. The width
checks shape movement within that channel; they do not search for a different
channel if the first is too narrow. The logged trips have enough room and pass
the movement replays. This limitation should remain explicit when investigating
other boat routes.

## Final package verification, 23 September

Fresh Release builds and the dual-runtime package validator passed. Both full
test hosts then ran against the exact packaged runtime DLLs, using their own
installed game archives; both exited zero. Shared, parity and shared-source
checks also passed. Both full logs include the seller's 325 floor routes, native
talk-counter checks, world entrance tests and Tiny Bronco landing replays.

The packaged DLL SHA-256 values were:

- x86: `BC4DA83E2775026E173BED460E2A361F713D9C2589696F4C86999F94778B12D9`
- x64: `9F32C4E4B2E7C918A1910ECC1E688FFDA0367DF5A0F16A805C1102F1FDFE5843`

The local legacy and Steam installations each matched all 4,038 package payload
files after deployment. Their Configuration trees, including description history,
were unchanged. This is a local test build on version 0.6.6, not a new public
release. Live play remains unverified.
