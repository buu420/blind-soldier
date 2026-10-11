# Chocobo navigation and Emerald observation: 0.9.4

The October 11 tester report supplies a cumulative Steam x64 log. Its 570,949,375
byte prefix matches log (13) exactly; only the 22,112,899 new bytes in log (14)
were investigated. Its mounted player becomes model 19 and ends on terrain 6
at 236632,-50,114494, progress 4. The old model-only walking profile excluded
that water surface, so Locations and Regions could both become empty.

All underwater visits in this log precede 2026-10-11 00:04:07Z; 0.9.2 was
published at 2026-10-11 02:07:17Z. Native model 30 occurs in the entity list, but
entity presence alone does not establish screen visibility. The log lacks
renderer rejection reasons and a loaded-build identifier. It cannot show whether
the shipped 0.9.2 packed-camera repair still fails.

## Native evidence

Read-only Ghidra output from the licensed legacy ff7_en.exe (SHA-256
4274ab2d52b67e547786fd959474e020fd3052a34dbcd7da708f86bcf5e48225)
confirms FUN_0074DB52's color at guest 0xDE6B54 and FUN_0074CECA's model-19
mask table at 0x969A40. Its five masks are 321B6F83, 321B6F87, 321B6FF3,
325B7FF7 and 375B7FFF. Retained Steam Ghidra decompilations 7ff7029dfc40.c
and 7ff7029e3ed0.c use the same guest color and table through translation.
Fresh output is retained privately in chocobo-emerald-20261011.

The primary [world control reconstruction](https://github.com/ergonomy-joe/ff7-worldmap/blob/master/NEWFF7/C_0074C9A0.cpp)
corroborates these native permissions. Its control update gives chocobos a
60-unit movement step; the [ground routine](https://github.com/ergonomy-joe/ff7-worldmap/blob/master/NEWFF7/C_0074FFC0.cpp)
uses a 200-unit contact radius. Complete journey replays retain these native
rules independently of production passability. Mounted route construction and
steering now use that 60-unit step and 200-unit footprint instead of the former
point-sized route and walking-step approximation.

Installed wm0.ev SHA-256 is
a020dace94f21b73094c204c54b01e13b3688002f65ffd9b1edf8ada5e372a64.
Function 530F checks bank-0 bit 0x03F6 before its quicksand-repulsion path;
the mask's terrain-28 bit alone cannot authorize crossing. The shared reader
observes this flag through the native world-event bank pointer, with coherence
checks and a closed crossing when unreadable.

Native handlers 8444, A224, 9E24 and A854 compare the current model with
0/1/2 before EnterField 29/24/22/12. Knights, Mime, HP<->MP and Quadra Magic
require entering on foot. Navigation holds for normal dismounting and resumes
when the native player model changes, instead of announcing a false arrival.

## Regression coverage and limits

The new routing tests failed first on green mountain access, gold connectivity
and torn native color. Separate failures reproduced false cave arrival and
the missing script-flag guard. Both runtime hosts now exercise all five masks,
unsupported surfaces, invalid/missing/torn reads, color changes, route release,
region membership and entrance-cue cache changes.

The catalog-wide cue check also respects Corel Prison's native terrain-24
boundary. Its terrain-16 interior is isolated from ordinary, green, blue and
black birds; the former walking approximation admitted the wall. The updated
native cases reject those cues and retain the gold bird's reachable entrance.

Installed-data checks start at the logged water position, verify routes and
entry handoff for all four Materia caves, and replay full Knights/Mime journeys
at camera angles 0, 512 and 1776 with native movement, contact and bridge rules.
Independent review also reproduced Wutai stalls for yellow, green and black
birds. Updating only the movement step still reproduced the yellow stall;
planning and following the native contact footprint repaired it. Twelve complete
Wutai journeys cover caught ordinary birds and all five bred colors at camera
angles 0 and 512, with a normal dismount handoff at the native entrance.
The current Emerald reader-to-Events-to-pursuit test verifies moving target
updates, disappearance on undrawn/hidden frames and ordinary-route avoidance.
The runtime now retains visibility reasons for change-only host logging, and
clears them on reset/unavailable observations. Startup logs include the baked
release version and loaded assembly module identifier.

No Emerald visibility boundary was loosened from entity presence or proximity.
No native game state, inventory or combat state is written. There was no live
game traversal, hardware-controller delivery or screen-reader listening for
this release. Release-gate, exact-package, deployment and publication evidence
is retained separately under release-0.9.4-20261011.
