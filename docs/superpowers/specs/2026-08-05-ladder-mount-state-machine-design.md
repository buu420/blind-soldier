# Ladder Mount State Machine Design

## Goal

Make field ladder navigation behave consistently in both FFVII runtimes while
preserving the existing traversal-locator sound.

## Confirmed failure

The 2026 x64 trace from field 123 shows the native movement mode alternating
between mounted and not mounted at the landing. The controller announces
`Ladder complete`, immediately accepts the stale mounted sample again, and then
retains `climb down` when later ladder reads become unavailable. Separately,
the previous config migration replaced the existing `ladder_061.wav` traversal
locator with `ladder_approach_214.wav` instead of adding a distinct mount cue.

## Required behavior

- `ladder_061.wav` remains the ordinary spatial traversal/ladder locator.
- `ladder_approach_214.wav` is a separate cue used only at the active route's
  ladder entrance.
- Until Cloud mounts, the route remains locked to the exact native ladder
  entrance. At the entrance, speech and `214.wav` repeat. If Cloud moves out of
  the entrance radius, the mount prompt stops and ordinary route directions
  guide him back. Returning to the entrance restarts the prompt and cue.
- A native mounted sample belongs to the route only when it matches the
  pending ladder's destination, or when navigation was enabled while Cloud was
  already mounted. A stale mounted sample after completion cannot reacquire the
  completed traversal.
- A coherent not-mounted sample at the expected landing completes the ladder.
  The live position may also complete it when the native movement sample is
  temporarily unreadable, or when the native phase says completion at the
  expected landing.
- Completion explicitly advances the route action, clears all mounted
  direction state, and continues from the landing. The completed ladder cannot
  be captured again during the same locked route.
- All native reads remain fail-closed away from a verified entrance or landing.

## Architecture

The shared `FieldNavigationController` owns the route-aware ladder phases and
uses live XYZ/triangle position for entrance and landing decisions. A new
shared mount-cue tracker emits entrance-only pulses for the prioritized ladder.
The x86 and x64 hosts each keep the existing traversal-cue player and add a
second player for the mount cue. Configuration exposes the two sounds and
intervals separately and migrates the accidentally combined shipped defaults.

## Verification

Behavior tests reproduce approach, drift, return, mount, native state bounce,
unreadable dismount, and post-landing route continuation. Both runtime test
suites, the dual-runtime package build, packaged-asset checks, and installed
hash verification must pass before deployment.
