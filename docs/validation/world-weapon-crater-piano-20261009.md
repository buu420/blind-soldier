# Native world tracking and piano repairs

The October 9 tester log was inspected against the installed native archives,
Ghidra's world entity and script functions, and the shared mod implementation.
The log predates the 0.8.10 controller scheme; its precise earlier version is
not recorded. It lacks individual Weapon positions and native defeat state,
so the log alone cannot identify the enemy's final game state.

## Ultimate Weapon

Native model 11 is the live flying Weapon. Its position can lie above a field
entrance. The prior catalog removed those arrival triangles and then applied
walking-route reachability to Events. A regression using an actual installed
entrance patch reproduces a live Weapon being omitted by those rules.

The catalog now retains the native position and visibility. A Highwind route
aims at current XYZ and refreshes movement within a triangle. Native proximity
uses wrapped absolute XYZ distance, shifted by four; the mod uses the smallest
encounter bound, 80, rather than claiming arrival from horizontal distance.
After battle the unique live model may be reacquired at its newly allocated
pointer. A valid empty list retires the chase. An unreadable list discards route
coordinates, releases flight input and retains only a cancellable requested
identity until a fresh read. No enemy health, stage or coordinates are written.

## Northern Crater

The logged flight position was X=86804, Y=2081, Z=138550. Adding signed wrapped
X and Z differences could cancel, making remote triangles appear within the
native point-14 entry bound. Both differences are now absolute. Point objectives
route to the triangle nearest the native point and finish at its actual X/Z.
Regressions retain native altitude, story-state and model arrival requirements,
including refusal to claim arrival while too high or on foot. Manual landing
continues to use the native crater terrain condition.

## Piano controls and instructions

The installed fields are niv_ti2 (287) and sinin1_2 (298). Their native molody and
code entities run script 2 at priority 6 while playing; Start ends the threads
through script 3. The reader requires the expected field/entity layout and two
consistent captures. It does not exempt the entire room or all busy scenes.

While those threads run, both hosts lease exclusive native pad input. Notes,
shoulders, D-pad chords and Start pass through even with resting triggers or a
keyboard settings page open. Only previously withheld presses still held drain
until release. R3 remains reserved by the input hooks. Tests exercise the shared
policy and each real hook, with piano, ordinary-room and stale-lease cases.

The paired F6 glyph tables were checked against native field dialogue and the
FFNx parser. The verified native order is OK, L1, L2, R1, R2, Start, Select, Up,
Down, Left, Right. Unknown and Japanese single-byte decoding is retained.

## Evidence limits

Native analysis and automated regression checks establish the reproduced mod
faults and corrected behavior. They do not establish the tester's exact final
Weapon state or replace live flight, piano, hardware-controller and speech
validation. Extracted licensed archives and executables remain private.
