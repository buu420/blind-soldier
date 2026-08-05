# Reactor Elevator Exit and Story Repair Design

## Scope

Restore the exit and Story route immediately after the No. 1 Reactor elevator
ride in field 121 (`elevtr1`), continue that route through the main staircase
in field 122 (`nmkin_2`) to the upper piping and ladder room, and preserve the
equivalent return path during the timed escape. Apply the repair to both x86
and x64 runtimes without weakening reachability checks.

## Evidence

- The live x64 log records field 121 at GameMoment 12 and publishes one
  branch-insensitive scripted exit whose destinations are both fields 120 and
  122. Story reports no target for the field.
- Native `elevtr1` script entity `jp0` branches on Bank 1 address 225 bit 0.
  Its MAPJUMPs lead upstairs to field 120 or downstairs to field 122 for
  Reactor 1, and to fields 128 or 129 for the reused Reactor 5 layout.
- Native walkmesh data gives field 122 a gateway back to field 121 and a
  gateway forward to field 123. The forward route is connected and routable.
- The original-game walkthrough sequence is elevator, main staircase, then
  upper piping and ladders; the escape traverses the same fields in reverse.

## Design

Create one shared branch policy that rewrites only the conditional field-121
script exit to its single progression-correct destination. GameMoment selects
the Reactor 1 or Reactor 5 visit and whether the elevator has just travelled
down or up. All other fields and exits remain unchanged. Both runtime adapters
must invoke this policy before exit labels and reachability are computed, so
the visible label and selected-route completion refer to the real destination.

Add four state-gated Story locations: leave the elevator toward field 122,
descend field 122 toward field 123, return through field 122 during the escape,
and leave the elevator toward field 120 during the escape. Each target uses the
native trigger line and completes on arrival. Existing field-123 ladder and
field-124 Save Point objectives remain the continuation of the same chain.

## Verification

- A failing x64 policy regression proves the raw field-121 target is ambiguous
  before the change and resolves to field 122 at GameMoment 12 and field 120 at
  GameMoment 27 afterward.
- Shared Story-catalog tests prove all four targets exist only in their intended
  GameMoment windows and expose their native coordinates.
- Focused tests, both project builds, generated-catalog reproducibility, package
  contents, and installed-file hashes are checked before handoff.
