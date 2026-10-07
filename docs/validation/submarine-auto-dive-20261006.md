# Submarine automatic dive and destination follow-up

Scope: sail to a valid dive entry, press the configured native action, retain the
chosen destination across the world-map reload, list available underwater places,
reach Lucrecia's lake and improve the walking approach to Highwind. The user
explicitly requested automatic diving. Surfacing, dismounting, descent into fixed
underwater entrances and boarding retain their normal controls.

## Recorded failure

The newest Downloads log, `ff7_accessibility_steam2026_x64 (10).log`, is cumulative.
Its SHA-256 is
`4bc08887c55dadcf55f7f30f4ccf6dce20c7ff1e02a446714ad8467603f186c3`.
The final October 6 session boards model13 at 17:07:59 UTC. Navigation then
reaches terrain26 at 17:10:09, with the final position
`121103,-240,113875`, camera3672 and map0. No underwater map2 state appears in
this file. It cannot establish the later Gelnika report or the installed version.

## Native dive and reload

Read-only Ghidra inspection of the licensed legacy executable confirms
`FUN_0074EA48`, especially `0074f57e` through `0074f5ca`:

- Model13 dives on terrain3, Sea. Terrain26, Deep sea, is rejected.
- The rising edge of Cancel, native mask0x40 / control slot6, starts the dive.
  Confirm, slot5 / mask0x20, supplies submarine thrust.
- Native control `DE6B5C` must be nonzero, `DFC4B8` must be nonpositive, and
  movement enable `E28CDC` must be nonzero. Normal main state `E045E4` is1.
- `FUN_0074D6F6` enters main state4; reload state6 selects map2 and entry Y-3000.
  An observed accepted transition preserves the destination through a slow fade.

The implementation reads those words coherently and the current bindings. It
requires a neutral Cancel interval, requests one bounded press, releases before
the host scan throttle and resumes the selected fixed underwater target. It does
not write camera mode, position, world progress or transition state. Conflicting
or unreadable bindings release input and explain why automatic diving stopped.

The first underwater sample may arrive before its entities or availability flags
can be read. The journey retains the selected destination and retries for up to
eight seconds, with one waiting announcement. Coherent flags proving that a Key
or wreck is absent stop the journey immediately. A failed sailing input clears
automatic travel intent, so a later manual dive cannot restart movement.

The primary reverse-engineered native source independently supports these
branches: [world input and transitions](https://raw.githubusercontent.com/ergonomy-joe/ff7-worldmap/master/NEWFF7/C_0074C9A0.cpp).

## Fixed destinations and native availability

`FUN_00763C35` assigns world script bank0 to `DC08DC`; the native bit readers and
writers index `bit >> 3` with `bit & 7`. `wm2.ev` allocates the red wreck while
bit6787 is set and the Key while bit6801 is clear. Huge Materia collection is
bit6800. The wreck remains after collection, so Locations can still name it while
Story stops promising remaining Materia. Gelnika and the lake are fixed places.
Unreadable/torn flags never invent Key or wreck availability on the surface.
Underwater entity allocation is a fallback when flags cannot be read.

Surface rows retain native surface events and story choices. Dive entry candidates
must lie on reachable Sea, fit the native submarine footprint in the underwater
map and lead to the selected destination's underwater component. The actual
arrival point is checked again before Cancel is requested.

## Lucrecia's lake and tunnel

The native underwater floor lookup selects the lowest floor by X/Z. It does not
require matching floor heights across map cells. Two water edge shapes, repeated
in the six installed copies, have different Y coordinates on opposite sides of
the cell boundary. Loader adjacency now joins only those native passable
cross-cell water edges; surface map adjacency retains its XYZ rules.

Examples are triangles34303/34602, at heights-4904/-4771, and34455/34603,
at heights-4904/-4896, across mesh12,16 to12,17. This corrects the earlier
0.8.6 investigation, which found no tunnel connection in the height-matched graph.

The lake handoff is `101908,-3000,144580`. The underlying underwater triangle34752
is Sea; surface triangle88384 is Sea at Y1761. The surface map has space for all
five native contact samples and a small terrain18 submarine pen. The cave entry
is on foot in mesh12,17/script7. Guidance asks for normal Cancel to surface, then
the pen, dismount and cave entry. It does not promise a Highwind landing in this
lake pocket, where there is no grass.

Underwater travel checks the native center and four offsets of200 units against
the lowest floor, keeps travel at Y-3000 and excludes unselected fixed entrance
volumes. Emerald avoidance and selection still require positive renderer
visibility; no hidden live Emerald position is used.

## Highwind contact

`FUN_00762993` writes the native contact witness at player+4.
`FUN_00762A21` requires both axes inside1024 wrapped units and tests the actual
model masks. The measured Highwind mask is `00 00 18 3C 3C 18 00 00`; party
models0/1/2 use `00 00 00 18 18 00 00 00`. Movement clears contact in
`FUN_007631DF`; stale witnesses outside native reach are rejected.

`FUN_0076420A` dispatches the contacted vehicle's function4 on Confirm, subject
to native control. Highwind function4304 accepts the party on foot. Its disabled
flags0x10/0x80 do not receive invented boarding points. Guidance ends at measured
contact instead of a neighboring triangle and asks the player to press Confirm.
Selecting it while aboard the submarine explains the pen and dismount first.

A witnessed collision can roll movement back across a triangle boundary. Native
boarding still uses the contacted entity, so an otherwise valid contact witness
does not require the rollback triangle to contain a computed boarding point.
The installed regression parks Highwind at `171487,147725`, approaches from
`170991,147437`, and confirms contact across triangles92871/93543. Witnesses
still require the correct entity, another entity pointer and native reach;
geometry-only arrival retains its triangle restriction.

Primary native references:
[entity contact](https://raw.githubusercontent.com/ergonomy-joe/ff7-worldmap/master/NEWFF7/C_00760FB0.cpp),
[boarding and dismount](https://raw.githubusercontent.com/ergonomy-joe/ff7-worldmap/master/NEWFF7/C_007663E0.cpp).

## Verification boundary

The focused regressions replay the logged surface position, the normal dive
action and reload, cancellation and slow accepted transitions, changed bindings,
allocation flags, native boarding contact and installed underwater routes.
The installed route matrix covers all twelve directed journeys between Gelnika,
the Key, the wreck and the lake at50,100,30 and200 ms scans, plus50 ms with
deterministic jitter of15 ms either way. Native frame accumulation follows actual
elapsed time, including scans with no update and alternating one/two updates.
Each replay checks native floor/body clearance and the normal descent or manual
surfacing handoff. Heading selection accounts for measured scan timing; travel
checks the full input lease, and final approach follows the native turn lattice.
Positioning counts actual native displacement and turning as progress. It stops
after five seconds without motion or thirty seconds on one positioning leg;
pauses and new routes reset that clock. The installed frozen-state regression is
`101925,-3000,144448`, camera3221. This closes the review finding where ordinary
route-distance resets could keep a blocked positioning leg alive indefinitely.

Final positioning searches native headings and up to three straight native legs.
The first planning poll releases turn and thrust without searching. Later polls
search from an observed neutral pose, and the result poll remains neutral. A
following observation must match position, camera, multiplier and scan timing;
each delivered command then checks the actual full 500 ms lease plus96 margin
with the advisory search cache disabled. A changed pose starts neutral planning
again. Search work is split at12000 counted units per poll, with one bounded
iterator step of overshoot, a600000 total counted search budget and finite
retries. The formal replays assert a14000-unit poll bound; the returned candidate
measured12054 units and44.7 ms in Debug. These are measurements on this machine,
not a wall-clock guarantee on every host. The no-motion and stage clocks keep
running during preparation, search and retries.

Missing native control data, camera modes outside2/3, multipliers outside1..4 or
camera angles outside0..4095 cannot be replaced by a saved final heading. The
resolver releases input and discards that plan while retaining ordinary guidance
and automatic intent. Recovery requires a fresh neutral preparation poll. The
installed Final and Maneuver regressions use an actual input owner and recording
sink to verify release for each invalid observation.

The changed-cadence regression starts at102338,-3000,145010, camera3584 and
changes50 ms scans to200 ms after one, two or three thrust polls. After one, the
first six-frame movement misses the true128-unit handoff; the controller measures
the larger scan immediately, prepares again and eventually stops with an
explanation and no held key. The other two offsets reach legitimate handoffs.
The arrival area is not enlarged to accommodate a missed observation.

Very slow irregular hosts may step over the fixed arrival area: the additional
200 ms jitter sweep reached59 of60 routes and safely stopped on the remaining
attempt. The default50 ms jitter sweep reached all60. This is an offline timing
limit, not a promise of live gameplay coverage.
Release evidence is retained separately with the exact archive hashes and full
dual-runtime gate results. Historical failing logs establish the regression;
they do not count as final verification.

No live gameplay or physical keyboard/controller input was exercised. The current
encrypted Steam executable was not freshly decompiled for these native branches;
cached translated x64 evidence agrees with the legacy dive behavior. Actual
Steam SDL hook installation and player-facing diving/boarding remain to be
verified in game. The older post-mission freeze is not claimed fixed.
