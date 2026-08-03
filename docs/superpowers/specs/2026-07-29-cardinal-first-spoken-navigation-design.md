# Cardinal-First Spoken Navigation Design

## Goal

Speak `up-right`, `down-right`, `down-left`, or `up-left` only when the
route calls for meaningful movement on both controller axes. Lateral drift
and short funnel corners on otherwise straight paths must remain cardinal.

## Evidence

The captured `blinst_2` emergency-stair trace contains 72 stable waypoints
forming long cardinal switchbacks. At the first lower stair, Cloud is at
`(-137,257)` and the next funnel point is `(-86,167)`. The existing
minor-to-dominant axis threshold of `0.5` turns that short corner chord into
`down-right 2`, even though continuing down the stair is valid. Similar
one-to-three-count diagonal chords repeat at the ends of straight stair runs.

Ghidra confirms FFVII's field input checks consume native direction masks;
the mod should not synthesize a second axis merely because a funnel endpoint
is off center. Established navigation engines likewise collapse small or
redundant maneuvers instead of verbalizing every shape point.

## Design

This is a speech-formatting change only. Route planning, walkmesh visibility,
obstacle recovery, beacon audio, progress calculation, and ladder handling
remain unchanged.

A spoken segment is diagonal only when both conditions hold after applying
the field's native control transform:

1. The minor axis is at least `0.75` of the dominant axis.
2. The minor axis represents at least one configured spoken-distance count.

Otherwise the formatter speaks only the dominant cardinal axis. Explicit
obstacle-recovery waypoints still pass through the same formatter and can
produce a diagonal when their verified detour genuinely satisfies both
conditions.

## Regression Coverage

- Reproduce the captured first `blinst_2` stair chord and require cardinal
  `down` rather than `down-right`.
- Require a sub-count secondary axis to remain cardinal.
- Retain the existing `240,240` genuine diagonal expectation of
  `up-right 4`.
- Run Reloaded, shared-layout, Steam 2026 x64, and parity suites before
  deployment.
