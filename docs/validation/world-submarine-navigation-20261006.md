# World-map submarine navigation evidence

User-confirmed scope: use the existing airship-style destination menu for the
ordinary world-map submarine on both runtimes; avoid Emerald unless selected.
This extends module 3 world navigation. The submarine mission is module 9 and
retains its released 0.8.5 behavior.

## Native coordinate and movement evidence

Fresh read-only Ghidra decompilation of the licensed legacy executable
(`4274AB2D52B67E547786FD959474E020FD3052A34DBCD7DA708F86BCF5E48225`)
and the [primary world-map reconstruction](https://github.com/ergonomy-joe/ff7-worldmap)
establish the following. Cached translated x64 exports show the same guest rules.

- `FUN_00750F3C` maps the underwater map's 12 physical blocks into the global
  9 by 7 grid: physical column is global column modulo 3 and physical row is
  `(global row + 2) modulo 4`. World wrap is `0x48000` by `0x38000`.
- Player model 13 uses terrain mask `0x04048008`: terrains 3, 15, 18 and 26.
  Model 28 is the red wreck. It is not a playable submarine.
- `FUN_0074EA48` turns the underwater camera with Left/Right and moves forward
  with Confirm (action slot 5, mask `0x20`). Up reduces Y and Down raises Y.
  Thrust stops immediately when Confirm is released. No minigame throttle or
  Highwind strafe action is used. Bindings come from one current control table;
  aliases to an unintended action are rejected.
- On the surface, modes 0 and 1 move in world axes. Mode 2 rotates the movement
  by the camera front, turning and moving sideways together on Left/Right.
  Down reverses the turn keys. Surface travel uses directional controls alone:
  Confirm would add unwanted forward motion. A complete native input lease is
  forecast against the terrain and unselected entrances before each command.
  The native map0 surface is the one closest to player height; map2 instead
  selects the lowest surface.

## Emerald avoidance without hidden tracking

`wm2.ev` sets Emerald model 30 to Y = -4250 and flag `0x80` retains that height.
Native dive entry puts the player at Y = -3000. Without Up, Down and the native
floor correction preserve Y at or above that level. The world script's distance
condition is at most 75 after `FUN_00753C23`'s wrapped three-axis Manhattan
distance is shifted right by four: the largest raw trigger distance is 1215.
The entry-depth vertical separation is 1250, which exceeds it.

Ordinary auto travel therefore holds entry depth and recovers there in place
before thrust. It never reads hidden Emerald coordinates for steering. It
stops above the selected underwater destination and hands descent to the player.
That final manual descent, or manual Up held during auto travel, is outside the
depth guarantee and can encounter Emerald. A negative visibility observation
never authorizes automatic descent.

The Gelnika and red wreck require native three-axis proximity of at most 975.
The Key requires its two native terrain-script-3 triangles and less than 500
units above the interpolated seabed. Being above a destination is not arrival.
The generic red wreck label does not promise unrecovered Huge Materia.

Explicit Emerald approach protects the fixed Gelnika and present red-wreck
entrances with a horizontal Manhattan bound of 975. This guards descent in
place as well as every point of the forward lease, including wrapped segments.
It uses fixed script locations, never hidden live enemy motion. The first
refused command releases input; the next observation stops with the site's name.
Current Key trigger triangles also block deliberate Emerald commands, including
descent in place: approaching within 500 of the seabed can fire the Key handler
without horizontal movement. A moving target cannot replan away a pending warning.
At approach depth the probe covers the complete lease and is not capped at
the next waypoint. Installed replays also confirm that manual descent from
each actual ordinary handoff position satisfies the selected native arrival.

## Visible Emerald observations

The shared reader uses the native per-frame chunk draw flag and hidden bit,
model slot/record, drawn camera-space position, underwater fade, projection,
viewport and camera transform. Repeated complete captures must agree and
the rendered record must match the current entity position. Seabed occlusion
is checked against installed map geometry because the renderer uses its depth
buffer rather than publishing a CPU visibility result.

Only a positive fresh observation can expose Emerald in Events or sustain an
explicit approach. Missing/torn observations, fog, off-screen or occluded origins
release input immediately. A 500 ms gap ends the route with an explanation;
shorter gaps retain only its selection. Sighting announcements have the same
debounce. No hidden movement is inferred. Its position is never extrapolated. The origin check can
underreport a partly visible large model, and observations beyond the verified
curvature-free range are refused.

## Verification and limits

The shared submarine suite checks native controls, remapped aliases, releasing
inputs, safe depth recovery, native arrival, manual descent handoff and Emerald
leaving view. Installed-data replays use the native yaw and thrust equations on
the actual underwater terrain. Surface cases cover camera modes 0, 1 and 2,
sideways drift, overlapping terrain and unselected entrances. Both hosts register the same navigation and
visibility suites. The visibility reader has positive/negative frame cases and
mutation checks of its safeguards.

The surface lease uses the native multiplier: world init sets it to 2;
FFNx's 60 FPS mode halves it to 1. The forecast uses 60 Hz divided by that
multiplier. Native shore refusal can deflect the boat along the coast; the
replays model accepted direct steps and do not claim to simulate that sliding
or the full native footprint. Installed submarine-water triangles have no
field entrances at any world progress level. Trig-table rounding and the
native recent-triangle cache remain limits of the prediction.
The deep entrance guard checks the straight predicted lease; native terrain
deflection can bend the actual path near a bank. Its direct-segment and descent
regressions do not establish live collision or sliding behavior.

FFNx only replaces these controls when both `enable_worldmap_external_mesh`
and `enable_analogue_controls` are enabled. The legacy host reads the actual
FFNx.toml beside the game once at initialization and stops automation with
guidance for that combination or unreadable/malformed relevant settings.
The local flags are both false. Native Steam x64 is outside FFNx's patch.
The [FFNx player source](https://github.com/julianxhokaxhiu/FFNx/blob/master/src/ff7/world/player.cpp)
and [world initialization](https://github.com/julianxhokaxhiu/FFNx/blob/master/src/ff7/world/world.cpp)
were checked against the native timing evidence.

Final release verification is recorded in the fresh 0.8.6 release workspace.
No live game, renderer hook or physical controller was exercised. The Steam
installation currently lacks its native executable; static fingerprint tests
use the existing checksum-verified licensed fixture and remove the temporary
copy afterwards. This is not live gameplay evidence.

Research, Ghidra output and focused logs:
`C:\Users\buu42\Documents\FFVII-ActiveBuild\submarine-navigation-20261006`.
