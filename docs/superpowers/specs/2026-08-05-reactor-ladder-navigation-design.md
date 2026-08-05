# Reactor Ladder Navigation Repair Design

## Scope

Repair the No. 1 Reactor upper piping/ladder room (`nmkin_3`, field 123) and
lower piping/save room (`nmkin_4`, field 124) for both Blind Soldier runtimes.
The repair must keep exits and story navigation available across the vertical
route, announce the intended climb direction while an objective is active, and
use the requested `214.wav` as a repeating ladder-entry cue until Cloud mounts
the ladder.

## Evidence

- The live x64 log publishes two native exits in field 123 but only one
  route-reachable exit. The lower save-room exit is therefore hidden by the
  reachability filter rather than absent from the game.
- Native field-script decoding finds four ladder routines and two scripted
  jumps in `nmkin_3`. The descending routines collapse to cleanup `JUMP`
  endpoints whose Z coordinate is not encoded. Treating the entrance Z as the
  missing landing Z prevents the descending routines from pairing with their
  matching upward routines and fractures the off-walkmesh graph.
- Field 124 exposes its Save Point object but has no story definition for
  continuing to the reactor core or returning after the boss.
- The current route controller announces an action-gated ladder once and then
  suppresses it. The general ladder proximity cue already stops on native
  mount state, but it is not restricted to the route's next ladder and uses a
  different sound.
- The walkthrough sequence is: speak to Jessie, descend the ladders and piping,
  pass the Save Point, enter the reactor core, then backtrack and help Jessie
  during the escape.

## Design

### Native ladder graph

Pair opposite ladder routines using their shared native endpoint and the
walkmesh component at the opposite entrance. When a collapsed cleanup jump has
no Z coordinate, do not invent entrance height. Resolve the counterpart from
the opposite routine's source/endpoint geometry, then use that counterpart's
source triangle and full XYZ as the walkable landing. Exact three-dimensional
matching remains preferred when both endpoints contain Z.

The change is generic because the missing-Z cleanup pattern occurs in native
field scripts, but tests pin the two Reactor 1 ladder pairs so it cannot create
shortcuts between unrelated ladders.

### Exit and story availability

Once the native ladder components are connected, the existing reachability
filter can publish both real exits without weakening its fail-closed behavior.
Add story targets in fields 123 and 124 for the intended descent to the Save
Point/core and for the escape return. The targets use native gateway/ladder
coordinates and game-moment/flag gates; they do not expose future objectives.

### Ladder cue and direction

Keep ordinary ladder discovery cues for players who are not navigating. While
a route is active and its next off-mesh action is a ladder, make that ladder the
only route-priority ladder cue. Pulse `214.wav` at the configured ladder cue
interval while Cloud is in range, and stop all pulses immediately when native
state reports that Cloud mounted the ladder, navigation is disabled, the field
changes, or field audio is suppressed.

At mount time, the pending route action's required input is authoritative.
Native ladder state is the fallback only when navigation begins in the middle
of a ladder and no pending route action exists. Initial and repeat guidance
therefore always say `climb up`, `climb down`, `climb left`, or `climb right`
for the objective's actual direction.

## Verification

- Focused tests for missing-Z opposite-ladder pairing and both installed
  `nmkin_3` ladder chains.
- Tests that both field 123 exits and the field 124 continuation are routable.
- Controller tests for repeated approach-cue eligibility, immediate stop on
  mount, and route-direction precedence over transient native direction.
- Asset/package tests confirming `214.wav` ships in x86 and x64 outputs.
- Build both runtimes, deploy the staged files to the active install, and check
  startup/log initialization without launching the game unless live testing is
  available.
