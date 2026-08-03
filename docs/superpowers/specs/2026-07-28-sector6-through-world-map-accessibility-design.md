# Sector 6 Through World Map Accessibility Design

## Goal

Make the complete story path from the post-pillar Sector 6 scene through the
motorcycle escape and first controllable world-map state navigable without
sighted assistance in both supported FFVII runtimes.

The pass covers every required story target and every sighted-player-relevant
interaction along the route: pickups, key items, puzzle controls, traversal
points, exits, required NPCs, optional guide-listed treasures, save points,
shops, minigame objectives, party-formation transitions, and the world-map
handoff.

## Evidence order

1. Native field scripts and walkmeshes from the installed Steam data.
2. The project's Kujata-derived field-script extraction.
3. Ghidra evidence for native movement and triangle-state behavior.
4. AbsoluteSteve's FFVII walkthrough for narrative order and optional
   guide-listed interactions.
5. Runtime traces for the exact positions and game moments observed in play.

Guide text supplies the checklist and intended order. It does not supply
coordinates or state gates. Coordinates, entity identities, visibility, and
completion conditions must come from native data.

## Coverage boundary

The audit begins when control returns in Sector 6 after the Sector 7 plate
collapse. It ends when the motorcycle sequence and Motor Ball battle are
complete, the party is formed outside Midgar, and the player first receives
control on the world map.

The coverage checklist includes:

- Barret and Tifa catching up with Cloud before the player is sent through
  Sector 6.
- Sense Materia, Sector 6 backtracking, the Sector 5 slum return, the optional
  Turbo Ether, Aeris's house, Barret upstairs, the bed, and the route back to
  Wall Market.
- The battery seller, all three batteries, the rope, both required sockets,
  the optional Ether socket, the propeller and barricade crossings, the
  swinging bar, and the Shinra Building entrance choice.
- Shinra Building lobby and stairs routes, stairwell Elixir, Turtle's Paradise
  flyer, second-floor shop, elevator controls and stops, floor 59 guards and
  Keycard 60.
- Floor 60 statue cover points and party-crossing stages; floor 61 Keycard 62
  employee; floor 62 Domino, Hart, all four library sections and inspectable
  files; floor 63 computer, doors, ducts, coupons, and exchange computer.
- Floor 64 vending machine, lockers, Phoenix Down, Ether, save point, and
  usable facilities; floor 65 Midgar model, all five part chests, insertion
  positions, and Keycard 66 chest.
- Floor 66 restroom, toilet, ventilation shaft, conference observation point,
  and Hojo pursuit; floor 67 laboratory scene, Poison Materia, save point,
  specimen lift, and prison progression.
- Floor 68 rescue, party selection, Enemy Skill Materia, four Potions,
  Keycard 68 employee, stairs/lifts, and the capture elevator.
- Prison conversations and door, sleep transition, dead guard, party
  formation, blood trail, Jenova tank, President Shinra, rooftop/Rufus path,
  party split, elevators, post-Rufus stairs and save point.
- Escape entrance, party/equipment formation prompts, motorcycle controls and
  objective, Motor Ball transition, final party formation, and first world-map
  control.

## Story target behavior

Story targets are game-state gated. A target is announced only while the
native event is available and incomplete.

The immediate post-collapse state must not direct Cloud toward a distant exit
while Barret and Tifa are still joining. It instead exposes the native
catch-up trigger or a bounded wait target derived from the field script. Once
the party joins, the next story target advances to the Sense Materia/playground
route and then to Aeris's house.

Puzzle sequences expose the next actionable native control, not a static room
center. Multi-stage puzzles advance only when their native banks, bits, game
moment, model visibility, or script state confirms completion.

Arrival clears a beacon only when the target's native completion condition is
true or a completing arrival target is reached. A field transition also clears
the old route immediately.

## Object behavior

Objects use native entity/model targets whenever available. Static targets are
allowed only for script-owned interaction lines or immutable walkmesh points
whose coordinates are extracted from native data.

Collected, opened, inserted, disabled, or unavailable objects disappear
according to native state. Objects must not remain targetable merely because
their field is loaded.

Required puzzle controls use descriptive labels that communicate what a
sighted player can distinguish, such as `First battery socket`, `A Coupon`,
`Lower-left Midgar Parts chest`, or `Second statue cover point`. Labels do not
reveal hidden outcomes or strategies.

## Stacked-walkmesh routing

Native movement retains the current triangle and checks adjacent collision
probes from that triangle. The accessibility route must preserve the same
layer continuity.

The route graph remains native triangle adjacency. The planar funnel is split
at elevation transitions so it cannot collapse a lower-level ramp entry and an
upper-level continuation into a false straight shortcut. A transition gate is
mandatory when:

- consecutive native portals form a meaningful elevation change;
- the transition cannot be reached directly on the current layer; or
- removing it would make the next waypoint's elevation inconsistent with the
  intervening native portal sequence.

The tracker may smooth within a verified same-layer corridor. It may not skip a
mandatory transition gate. If layer continuity cannot be verified, navigation
fails closed instead of steering through scenery.

Obstacle recovery uses actual progress along the connected corridor. Repeated
input that produces no forward progress asks for the next verified corridor
gate; alternating guidance caused by the same blocked location does not reset
the blocked state.

## Verification

Tests replay the captured Sector 6 trace from `(1162,269,12)` through the
ground-level obstacle and assert that the route retains the native transition
entry rather than aiming at elevated geometry.

Catalog tests enumerate the complete guide checklist and require every entry to
resolve to a native-backed target and state gate. Existing route, story,
objects, NPC, exit, traversal, save, battle, and dual-runtime tests remain
green.

The release package is installed into Reloaded-II for both x86 and x64. Live
gameplay remains the final validation for timing and native event progression.
