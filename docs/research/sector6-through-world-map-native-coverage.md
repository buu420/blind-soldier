# Sector 6 Through World Map Native Coverage

## Scope and evidence

This audit starts when control returns after the Sector 7 plate collapse and
ends at the first controllable world-map state after Motor Ball.

The ordered checklist comes from
[AbsoluteSteve's Final Fantasy VII walkthrough](https://gamefaqs.gamespot.com/ps/197341-final-fantasy-vii/faqs/45703).
Coordinates, entity identities, state gates, and availability windows come
from the Kujata extraction of the native FFVII field scripts. Ghidra inspection
of the legacy executable confirms that field movement retains the active
walkmesh triangle and performs collision probes from that triangle, so story
and route targets preserve native layer transitions rather than using
screen-space guesses.

The walkthrough is used only to establish what a sighted player can discover
and the order in which required actions occur. It is not used as a source of
coordinates.

## Required story path

| Walkthrough phase | Native-backed accessibility coverage |
| --- | --- |
| Post-collapse Sector 6 | `mds6_1` field 191, entity 6, exact LINE trigger and game moment 239 to 248: wait for Barret and Tifa to catch Cloud before directing the party onward. |
| Return to Sector 5 | Native exits through the Sector 6 playground and Sector 5 fields; story targets lead to Aeris's garden, house, and the upstairs Barret conversation. The bed remains an independently targetable object. |
| Return to Wall Market | Native exits back through Sector 5 and Sector 6 lead to the weapon shop, battery seller, and wall-climb entrance. |
| Wall climb | Native script stages cover the rope, first battery socket, propeller crossing, second socket, barricade crossing, swinging-bar prompt, landing, final ladder, and Shinra exterior. |
| Shinra entry choice | Separate native targets remain available for the front entrance and emergency-stair routes. Stair fields retain their exits and the Elixir pickup. |
| Lobby through floor 59 | Lobby elevators, exterior/stair entrances, floor elevator controls, guards, Keycard 60 award, and both elevator entrances are represented. |
| Floor 60 security | Four state-gated targets replace the old room-center target: reach the signaling point, signal Barret and Tifa, cross after both are safe, then continue to floor 61. The sequence follows temporary field bank 5, address 13, plus the native completion bit. |
| Floors 61 and 62 | The Keycard 62 employee, Mayor Domino, password challenge, password response, Hart, four library section signs, and all eight native file-shelf interaction lines are available. |
| Floor 63 | The door-control/coupon-exchange computer, all three coupon entities, and all three room-side duct interactions are targetable. The two usable entrances are identified by their rooms; the computer-room opening is identified as a one-way exit. Inside `blin63_t`, fixed native targets identify the shafts to the A Coupon room, B Coupon room, and floor 63 computer room, and all three route through the installed duct walkmesh. Coupon identity and collection state follow the native scripts: entity 42 A, entity 43 C, entity 44 B. |
| Floor 64 | Beds, vending machine, exercise machine, locker rows, Phoenix Down, Ether, unavailable megaphone, and Save Point are represented. First-visit and return-visit objects are separated by game-moment windows. |
| Floor 65 | All five Midgar Parts chests have distinct native collection bits. Story targets lead from each part to model slots A through E and then to the Keycard 66 chest. |
| Floor 66 | Restroom, toilet/vent choice, conference observation sequence, and the pursuit of Hojo are state-gated native targets. |
| Floors 67 and 68 | Hojo/Jenova approach, Poison Materia, Save Point, lab entrance, Aeris rescue, party selection, Enemy Skill Materia, four Potions, Keycard 68 employee, and the capture elevator are covered. |
| Prison and blood trail | Cell conversations, sleep offer, dead guard, party formation, specimen lift, blood trail entrances, Jenova chamber, President Shinra route, and floor 70 entrances are represented. |
| Rufus and escape | Rooftop approach, post-Rufus elevators, Tifa reunion, lobby escape trigger, party/equipment prompts, motorcycle start, and final party formation are ordered by native game moments. |
| World-map handoff | Field 170 exposes the party-selection target for moments 335 through 340 and the final `Leave Midgar for the world map` target at moment 341. |

## Guide-listed objects and optional interactions

| Area | Coverage |
| --- | --- |
| Sector 6 and Sector 5 | Sense Materia uses its native pickup entity. The optional Turbo Ether remains attached to the child NPC dialogue and its native reward state rather than being duplicated as a loose object. Aeris's bed is labeled. |
| Wall climb | Both required sockets are story stages. The third socket is separately labeled `Optional battery socket`; its required-battery and completion bits come from the native script, so it is not mislabeled as an Ether before use. |
| Shinra lobby and floor 2 | Turtle's Paradise flyer No. 2, Shinra bulletin, automated shop terminal, news screen, and both locked display chests are labeled. The contents of the display chests are hidden until the later revisit window. |
| Stairwell | The native Elixir pickup and Save Point are retained. |
| Floor 62 | Four readable library signs and eight readable file-shelf interaction lines are targetable for the password puzzle. |
| Floor 63 | A, B, and C Coupons use their native entity mapping and disappear according to their collection bits. The three duct shafts use the coordinates and destinations from CLOUD's native LADER and MAPJUMP scripts rather than inferred room centers. The exchange rewards are exposed only in the later object window. |
| Floor 64 | Phoenix Down, Ether, Save Point, vending machine, rest area, exercise machine, all locker sections, and the visible but currently unobtainable megaphone are covered without claiming the megaphone is collectible. |
| Floor 65 | Five separately named Midgar Parts chests and the Keycard 66 chest use six separate native collection bits. |
| Floors 67 and 68 | Poison Materia, Enemy Skill Materia, four Potions, and the relevant Save Points use native model/entity targets and collection state. |
| Floor 69 | The pre-Rufus Save Point remains available through its native save-point model. |

Battle rewards are announced by the battle/victory reader rather than being
invented as field objects. Dialogue-owned rewards such as the Turbo Ether and
keycard handoffs remain with the native dialogue/choice reader. Automatic
cutscenes, elevator battles, party-selection screens, the motorcycle minigame,
and Motor Ball are handled by the dialogue, cutscene-description, battle, and
Flash-menu readers; adding fake field beacons for those states would give the
player information that is not visually present.

## Regression boundaries

- First-visit Shinra objects cannot reveal later-return chest contents.
- Collected Midgar Parts and coupons stop appearing as available targets.
- The post-collapse party catch-up trigger completes before the next route is
  selected, preventing Cloud from entering combat alone.
- Floor 60 advances through its native signaling states instead of repeatedly
  routing to one point.
- The Sector 6 stacked route retains steep ascent gates so smoothing cannot aim
  at geometry on another walkmesh layer.
- The final target ends at world-map control; it does not prematurely add Kalm
  objectives to the Midgar sequence.
