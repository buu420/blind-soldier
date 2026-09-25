# Pagoda, North Corel, Gold Saucer chase and Temple clock

The reported sessions predate v0.6.9. Rechecking that release's code and packaged
runtime reproduced the North Corel route defect and confirmed that the three field
gaps remained. Having decoded scripts does not itself verify the routes presented
by the running accessibility layer.

## North Corel on foot

The logged landing at `(132730,152,165599)` produces the same 98-triangle,
20-waypoint route in the released x64 package. Four triangles cross terrain 28,
the Gold Saucer desert boundary. Native geometry permits a footstep there, but
eight world-event handlers invoke a script that takes control, pushes the party
back, displays the quicksand warning, and waits for Confirm. A geometrically
walkable triangle is therefore not necessarily a usable route.

The shared routing profile excludes this terrain on world map 0 for walking
characters and the existing ordinary Chocobo profile. Buggy and Highwind access
is unchanged. Special bred-Chocobo capabilities are not inferred from model 19;
the existing router does not yet read that separate capability state.

The installed-data regression runs the actual Story target at GameMoment 566
from five logged positions under five camera angles. Its movement simulator uses
the original native physical terrain mask, so the test independently detects an
attempt to enter the scripted boundary. All 25 routes must reach North Corel
without entering quicksand or stopping navigation.

## World dialogue and input ownership

Both runtimes read the rendered world-message buffers rather than extracting
unshown text from a world script. The relevant guest addresses are:

| State | Address | Native evidence |
| --- | --- | --- |
| Player control | `DE6B5C` | `FUN_0074D438` |
| Four window states, stride `0x30` | `CFF5E4` | `FUN_00769836` |
| Shared window owners | `CC0960` | `FUN_00769836` |
| Current text pointers | `E3B210` | `FUN_00769836`, `FUN_00769C02` |
| Rendered text buffers, stride `0x100` | `E3B220` | `FUN_00769C02` |

The reader verifies module ownership, matching header snapshots and matching
text reads. It speaks completed pages in native input-wait states. Automatic
movement pauses while the script or window owns input, retaining its destination
without consuming the movement-stall timeout. It never dismisses the warning or
changes native quest state.

Tests cover partial typing, closed windows, duplicate pages, script ownership
before the window opens, failed reads, torn reads and module changes. A controller
regression pauses near the movement-stall threshold and checks that navigation
can resume with its destination intact.

An x64 coordinator integration test starts real autowalk output against a controlled
memory fixture, revokes native control, and verifies that held movement is released
while the selected autowalk destination remains. It then opens the rendered
quicksand warning, checks that speech occurs once, closes the window, and checks
that a later occurrence can speak again. Native world choice-cursor announcements
are outside this repair; this reader supplies the visible message page.

## Field coverage

Pagoda field 586 contains all five floors. Bank 15 byte 138 selects the floor and
byte 139 records the five victories. The Story rows follow those native states:
the current opponent, the newly available stairs, and the return route. There is
no save point inside fields 586 or 587; Wutai street field 579 has the save point.
The stairs also appear in Exits with floor-specific names and identities. The
catalog verifies their native floor increment/decrement before allowing these
two same-field transitions; ordinary same-field reset lines stay excluded.
Native floor state and walkmesh locks decide which stairs are available.
Controller regressions check that a staircase crossed just before the floor
reload completes navigation, while a target lost farther away is not called
reached. Navigation must stay off on the new floor until the player starts it.
This disappearance-based completion is restricted to the verified Pagoda stairs.
A separate negative case closes an ordinary doorway while the party stands nearby;
it must still report the target unavailable, rather than announce an arrival.

The Gold Saucer chase at GameMoment 598 follows the escape directions shown in
the native scenes: Terminal Floor, Battle Square, Speed Square, Wonder Square,
Chocobo Square and Ticket Office. Story targets also return the player from the
other squares. The hidden three-minute timer is not announced or used to invent
a current visible position for Cait Sith.

Clock field 607 now uses the shared activity observation builder in both runtimes.
The supplied September 24 log already records the Time Guardian's introductory
text and native button labels being spoken (21:31:51 and 21:31:58 UTC, with later
repeats). The missing x64 observation path left the changing clock state silent.
The reader describes the visible long, short and second hands, and checks the
native IDLCK bridge state before saying a bridge is open. The Story predicates
account for the player's starting platform as well as both hand positions. When
doorway six is disconnected, the target explains the native clock controls.
After the mural sequence, the game itself sets the hands for doorway twelve.
The readout identifies the numbered doorway side or middle occupied by the party;
unclassified triangles receive no invented location. In x64, R repeats the
current activity state. The moving second hand is no longer offered as a static
exit to the room below, because its initial script coordinates are not its live location.
Its bearing remains available in the activity readout, including on repeat;
automatic speech does not fire on every movement of the second hand. Avoiding
that moving hand during a crossing still depends on the player's timing.

## Field verification

The field changes and their native script evidence are recorded in the region
generators and regression tests. The final verification must cover both runtime
assemblies, both installed field archives, and the exact packaged DLLs installed
for testing. Static script and movement replays do not constitute a live
playthrough of the five Pagoda battles, the chase, or the moving clock puzzle.

The clock regression compares 2,028 combinations of hand positions and arrival
platforms against routes through the installed walkmesh and native bridge locks.
The x64 activity test invokes the real coordinator over translated-memory
fixtures, checks the spoken hands and bridges, and checks the disabled setting.
